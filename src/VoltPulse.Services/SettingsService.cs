using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using VoltPulse.Core.Models;
using VoltPulse.Core.Paths;

namespace VoltPulse.Services;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON in %APPDATA%\VoltPulse\settings.json.
/// Missing keys keep their defaults; a corrupt file is backed up and defaults are used.
/// </summary>
public sealed class SettingsService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Serilog.ILogger _logger;
    private readonly object _gate = new();

    public SettingsService(Serilog.ILogger logger)
    {
        _logger = logger.ForContext<SettingsService>();
        Current = Load();
    }

    /// <summary>The current settings instance. Replaced atomically on update; read freely from any thread.</summary>
    public AppSettings Current { get; private set; }

    /// <summary>Raised after settings were changed and saved.</summary>
    public event Action<AppSettings>? Changed;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                // Deserializing onto a fresh instance keeps constructor defaults for absent keys.
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                Normalize(settings);
                return settings;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load settings; using defaults");
            TryBackupCorruptFile();
        }
        var defaults = new AppSettings();
        Normalize(defaults);
        return defaults;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            AppPaths.EnsureDirectories();
            string tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, AppPaths.SettingsFile, true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
        }
    }

    /// <summary>Mutates a clone of the current settings, saves it, and publishes it atomically.</summary>
    public AppSettings Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        AppSettings snapshot;
        lock (_gate)
        {
            snapshot = Clone(Current);
            mutate(snapshot);
            Normalize(snapshot);
            Current = snapshot;
            Save(snapshot);
        }
        Changed?.Invoke(snapshot);
        return snapshot;
    }

    /// <summary>Replaces all settings with factory defaults and notifies subscribers.</summary>
    public void ResetToDefaults()
    {
        lock (_gate)
        {
            Current = new AppSettings();
            Normalize(Current);
            Save(Current);
        }
        _logger.Information("Settings reset to defaults");
        Changed?.Invoke(Current);
    }

    public static AppSettings Clone(AppSettings settings)
    {
        return JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions) ?? new AppSettings();
    }

    public static void Normalize(AppSettings settings)
    {
        settings.FullResolutionDays = Math.Clamp(settings.FullResolutionDays, 1, 60);
        settings.DownsampledDays = Math.Clamp(settings.DownsampledDays, settings.FullResolutionDays, 3650);
        settings.SmoothingSamples = Math.Clamp(settings.SmoothingSamples, 1, 30);
        settings.FontScale = Math.Clamp(settings.FontScale, 0.8, 1.5);
        settings.BackgroundSamplingIntervalSeconds = Math.Clamp(settings.BackgroundSamplingIntervalSeconds, 2, 60);
        settings.MiniWidget.Opacity = Math.Clamp(settings.MiniWidget.Opacity, 0.2, 1.0);
    }

    private static void TryBackupCorruptFile()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".corrupt", true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
