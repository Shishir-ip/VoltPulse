using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoltPulse.Core.Models;
using VoltPulse.Core.Paths;
using VoltPulse.Services;

namespace VoltPulse.App.ViewModels;

/// <summary>Write-through settings page: every change is applied instantly and persisted.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly double[] Intervals = [0.5, 1, 2, 5, 10];
    private static readonly string[] Providers = ["Automatic", "Windows battery driver", "WMI", "Windows power API"];

    private readonly SettingsService _settings;
    private readonly Serilog.ILogger _logger;

    public static string[] ThemeNames { get; } = ["Dark", "Light", "Follow Windows"];
    public static int[] SmoothingChoices { get; } = [1, 2, 3, 5, 8, 10];
    public static int[] FullResChoices { get; } = [1, 3, 7, 14, 30, 60];
    public static int[] DownsampleChoices { get; } = [90, 180, 365, 730];
    public static double[] IntervalChoices { get; } = Intervals;
    public static string[] ProviderNames { get; } = Providers;

    public SettingsViewModel(SettingsService settings, Serilog.ILogger logger)
    {
        _settings = settings;
        _logger = logger.ForContext<SettingsViewModel>();
        var s = settings.Current;
        _selectedThemeName = s.Theme switch
        {
            ThemeMode.Light => "Light",
            ThemeMode.FollowWindows => "Follow Windows",
            _ => "Dark",
        };
        _useStateColors = s.UseStateColors;
        _fontScale = s.FontScale;
        _useFahrenheit = s.UseFahrenheit;
        _use24HourClock = s.Use24HourClock;
        _selectedInterval = s.EffectiveSamplingInterval();
        _smoothingSamples = s.SmoothingSamples;
        _invertSign = s.InvertSignConvention;
        _startWithWindows = s.StartWithWindows;
        _startMinimizedToTray = s.StartMinimizedToTray;
        _closeToTray = s.CloseToTray;
        _showTrayIcon = s.ShowTrayIcon;
        _fullResolutionDays = s.FullResolutionDays;
        _downsampledDays = s.DownsampledDays;
        _exportFolder = s.ExportFolder ?? AppPaths.ExportsDir;
        _includeSerial = s.IncludeSerialInExports;
        _preferredProvider = MapProviderIn(s.PreferredProvider);
        _showRawValues = s.ShowRawValues;
        _debugLogging = s.DebugLogging;
        _demoMode = s.DemoMode;
    }

    [ObservableProperty]
    private string _selectedThemeName;

    partial void OnSelectedThemeNameChanged(string value) =>
        Set(s => s.Theme = value switch
        {
            "Light" => ThemeMode.Light,
            "Follow Windows" => ThemeMode.FollowWindows,
            _ => ThemeMode.Dark,
        });

    [ObservableProperty]
    private bool _useStateColors;

    [ObservableProperty]
    private double _fontScale;

    [ObservableProperty]
    private bool _useFahrenheit;

    [ObservableProperty]
    private bool _use24HourClock;

    [ObservableProperty]
    private double _selectedInterval;

    [ObservableProperty]
    private int _smoothingSamples;

    [ObservableProperty]
    private bool _invertSign;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimizedToTray;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _showTrayIcon;

    [ObservableProperty]
    private int _fullResolutionDays;

    [ObservableProperty]
    private int _downsampledDays;

    [ObservableProperty]
    private string _exportFolder;

    [ObservableProperty]
    private bool _includeSerial;

    [ObservableProperty]
    private string _preferredProvider;

    [ObservableProperty]
    private bool _showRawValues;

    [ObservableProperty]
    private bool _debugLogging;

    [ObservableProperty]
    private bool _demoMode;

    partial void OnUseStateColorsChanged(bool value) => Set(s => s.UseStateColors = value);
    partial void OnFontScaleChanged(double value) => Set(s => s.FontScale = value);
    partial void OnUseFahrenheitChanged(bool value) => Set(s => s.UseFahrenheit = value);
    partial void OnUse24HourClockChanged(bool value) => Set(s => s.Use24HourClock = value);
    partial void OnSelectedIntervalChanged(double value) => Set(s => s.SamplingIntervalSeconds = value);
    partial void OnSmoothingSamplesChanged(int value) => Set(s => s.SmoothingSamples = value);
    partial void OnInvertSignChanged(bool value) => Set(s => s.InvertSignConvention = value);
    partial void OnStartWithWindowsChanged(bool value) => Set(s => s.StartWithWindows = value);
    partial void OnStartMinimizedToTrayChanged(bool value) => Set(s => s.StartMinimizedToTray = value);
    partial void OnCloseToTrayChanged(bool value) => Set(s => s.CloseToTray = value);
    partial void OnShowTrayIconChanged(bool value) => Set(s => s.ShowTrayIcon = value);
    partial void OnFullResolutionDaysChanged(int value) => Set(s => s.FullResolutionDays = value);
    partial void OnDownsampledDaysChanged(int value) => Set(s => s.DownsampledDays = value);
    partial void OnIncludeSerialChanged(bool value) => Set(s => s.IncludeSerialInExports = value);
    partial void OnShowRawValuesChanged(bool value) => Set(s => s.ShowRawValues = value);
    partial void OnDebugLoggingChanged(bool value) => Set(s => s.DebugLogging = value);
    partial void OnDemoModeChanged(bool value) => Set(s => s.DemoMode = value);

    partial void OnExportFolderChanged(string value) => Set(s => s.ExportFolder = string.IsNullOrWhiteSpace(value) ? null : value);

    partial void OnPreferredProviderChanged(string value) => Set(s => s.PreferredProvider = MapProviderOut(value));

    private void Set(Action<AppSettings> mutate)
    {
        try
        {
            _settings.Update(mutate);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Applying a setting failed");
        }
    }

    private static string MapProviderIn(string? name) =>
        name is null ? "Automatic" : Providers.FirstOrDefault(p => p == name, "Automatic");

    private static string? MapProviderOut(string choice) => choice == "Automatic" ? null : choice;

    [RelayCommand]
    private void PickExportFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the export folder",
        };
        if (dialog.ShowDialog() == true)
        {
            ExportFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BackupSettings()
    {
        try
        {
            File.Copy(AppPaths.SettingsFile, AppPaths.SettingsBackupFile, true);
            Status = $"Settings backed up to {AppPaths.SettingsBackupFile}";
        }
        catch (Exception ex)
        {
            Status = $"Backup failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RestoreSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsBackupFile))
            {
                Status = "No settings backup found.";
                return;
            }
            File.Copy(AppPaths.SettingsBackupFile, AppPaths.SettingsFile, true);
            Status = "Backup restored. Restart VoltPulse to apply it everywhere.";
        }
        catch (Exception ex)
        {
            Status = $"Restore failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        _settings.ResetToDefaults();
        Status = "All settings were reset to their defaults.";
    }

    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    private void ClearHistory()
    {
        HistoryClearRequested?.Invoke();
    }

    /// <summary>The view confirms before invoking the store clear.</summary>
    public event Action? HistoryClearRequested;
}
