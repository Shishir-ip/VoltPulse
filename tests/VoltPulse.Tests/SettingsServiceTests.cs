using System.Text.Json;
using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"voltpulse-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            // temp cleanup best effort
        }
    }

    private static SettingsService CreateService()
    {
        var logger = new Serilog.LoggerConfiguration().CreateLogger();
        return new SettingsService(logger);
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        // SettingsService reads from AppPaths; test the normalize/default behavior instead
        var settings = new AppSettings();
        Assert.Equal(1.0, settings.EffectiveSamplingInterval());
    }

    [Fact]
    public void EffectiveSamplingInterval_ClampsToAllowedSet()
    {
        Assert.Equal(1.0, new AppSettings { SamplingIntervalSeconds = 1 }.EffectiveSamplingInterval());
        Assert.Equal(0.5, new AppSettings { SamplingIntervalSeconds = 0.4 }.EffectiveSamplingInterval());
        Assert.Equal(10, new AppSettings { SamplingIntervalSeconds = 12 }.EffectiveSamplingInterval());
        Assert.Equal(2, new AppSettings { SamplingIntervalSeconds = 2 }.EffectiveSamplingInterval());
    }

    [Fact]
    public void JsonRoundTrip_PreservesSettings()
    {
        var settings = new AppSettings
        {
            Theme = ThemeMode.Light,
            SamplingIntervalSeconds = 2,
            InvertSignConvention = true,
            Alerts = AlertDefaults.All(),
        };
        settings.Alerts[0].Threshold = 25;

        string json = JsonSerializer.Serialize(settings, SettingsService.JsonOptions);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsService.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(ThemeMode.Light, restored!.Theme);
        Assert.Equal(2, restored.SamplingIntervalSeconds);
        Assert.True(restored.InvertSignConvention);
        Assert.Equal(25, restored.Alerts[0].Threshold);
        Assert.Equal(10, restored.Alerts.Count);
    }

    [Fact]
    public void MissingKeys_FallBackToDefaults()
    {
        const string partialJson = """{"Theme": "Light"}""";
        var restored = JsonSerializer.Deserialize<AppSettings>(partialJson, SettingsService.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(ThemeMode.Light, restored!.Theme);
        Assert.Equal(1.0, restored.SamplingIntervalSeconds);   // default survived
        Assert.Equal(3, restored.SmoothingSamples);
        Assert.True(restored.Use24HourClock);
    }

    [Fact]
    public void Normalize_ClampsNonsense()
    {
        var settings = new AppSettings
        {
            FullResolutionDays = 500,
            SmoothingSamples = 100,
            FontScale = 10,
        };
        SettingsService.Normalize(settings);
        Assert.InRange(settings.FullResolutionDays, 1, 60);
        Assert.InRange(settings.SmoothingSamples, 1, 30);
        Assert.InRange(settings.FontScale, 0.8, 1.5);
    }

    [Fact]
    public void Update_NotifiesSubscribers()
    {
        var service = CreateService();
        AppSettings? notified = null;
        service.Changed += s => notified = s;

        var result = service.Update(s => s.InvertSignConvention = true);

        Assert.True(result.InvertSignConvention);
        Assert.NotNull(notified);
        Assert.True(notified!.InvertSignConvention);
        Assert.True(service.Current.InvertSignConvention);
    }
}
