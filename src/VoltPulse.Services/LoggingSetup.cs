using Serilog;
using Serilog.Events;
using VoltPulse.Core.Models;
using VoltPulse.Core.Paths;

namespace VoltPulse.Services;

/// <summary>Configures the Serilog rolling file logger under %APPDATA%\VoltPulse\logs.</summary>
public static class LoggingSetup
{
    public static void Configure(AppSettings settings)
    {
        var level = settings.DebugLogging ? LogEventLevel.Debug : LogEventLevel.Information;
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.LogsDir, "voltpulse-.log"),
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 20_000_000,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }

    public static void Shutdown() => Log.CloseAndFlush();
}
