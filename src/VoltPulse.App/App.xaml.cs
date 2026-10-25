using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VoltPulse.App.Services;
using VoltPulse.App.ViewModels;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;
using VoltPulse.Core.Paths;
using VoltPulse.Providers;
using VoltPulse.Services;

namespace VoltPulse.App;

public partial class App : System.Windows.Application, IDisposable
{
    /// <summary>True while a real exit is in progress; close-to-tray checks this.</summary>
    public bool IsExiting { get; set; }

    /// <summary>Access point for UI services resolved outside the DI graph (share, dialogs).</summary>
    internal static IServiceProvider? HostServices { get; private set; }

    void IDisposable.Dispose()
    {
        _singleInstanceMutex?.Dispose();
        GC.SuppressFinalize(this);
    }

    private Mutex? _singleInstanceMutex;
    private IHost? _host;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\VoltPulse.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "VoltPulse is already running. Look for its icon in the system tray.",
                "VoltPulse", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        RegisterExceptionHandlers();

        if (e.Args.Any(a => string.Equals(a, "--diagnostics", StringComparison.OrdinalIgnoreCase)))
        {
            RunDiagnosticsDump();
            return;
        }

        AppPaths.EnsureDirectories();
        LoggingSetup.Configure(ReadPreSettings());

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(RegisterServices)
            .UseSerilog(Log.Logger)
            .Build();
        HostServices = _host.Services;

        await _host.StartAsync().ConfigureAwait(true);

        var settings = _host.Services.GetRequiredService<SettingsService>();
        ThemeManager.Apply(settings.Current);
        settings.Changed += s => Dispatcher.BeginInvoke(() => ThemeManager.Apply(s));
        StartupManager.Apply(settings.Current);
        settings.Changed += s => StartupManager.Apply(s);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(2)).Wait(TimeSpan.FromSeconds(3));
            _host?.Dispose();
        }
        catch
        {
            // best effort during shutdown
        }
        LoggingSetup.Shutdown();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<Serilog.ILogger>(Log.Logger);
        services.AddSingleton<SettingsService>();
        services.AddSingleton<MockBatteryProvider>();
        services.AddSingleton<IoctlBatteryProvider>();
        services.AddSingleton<WmiBatteryProvider>();
        services.AddSingleton<Win32PowerProvider>();
        services.AddSingleton<SystemInfoProvider>();
        services.AddSingleton<IAggregatedBatteryProvider>(sp =>
        {
            var settings = sp.GetRequiredService<SettingsService>();
            return new AggregatedBatteryProvider(
                new IBatteryProvider[]
                {
                    sp.GetRequiredService<IoctlBatteryProvider>(),
                    sp.GetRequiredService<WmiBatteryProvider>(),
                    sp.GetRequiredService<Win32PowerProvider>(),
                },
                sp.GetRequiredService<SystemInfoProvider>(),
                Log.Logger,
                demoProvider: sp.GetRequiredService<MockBatteryProvider>(),
                shouldUseDemoProvider: () => settings.Current.DemoMode,
                preferredProviderSelector: () => settings.Current.PreferredProvider);
        });
        services.AddSingleton<SamplerService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<SamplerService>());
        services.AddSingleton<IBatteryProvider>(sp => sp.GetRequiredService<IoctlBatteryProvider>());
        services.AddSingleton<IBatteryProvider>(sp => sp.GetRequiredService<WmiBatteryProvider>());
        services.AddSingleton<IBatteryProvider>(sp => sp.GetRequiredService<Win32PowerProvider>());
        services.AddSingleton<IHistoryStore>(sp => new SqliteHistoryStore(AppPaths.DatabaseFile, Log.Logger));
        services.AddSingleton<HistoryWriterService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<HistoryWriterService>());
        services.AddSingleton<PowerCfgReportImporter>();
        services.AddSingleton<IAlertPresenter, AlertPresenter>();
        services.AddSingleton<AlertEngine>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<AlertEngine>());
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<TrayIconService>());
        services.AddSingleton<MiniWidgetManager>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<MiniWidgetManager>());
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<SessionsViewModel>();
        services.AddSingleton<InsightsViewModel>();
        services.AddSingleton<PowerUsageViewModel>();
        services.AddSingleton<AlertsViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<LiveViewModel>();
        services.AddSingleton<ComingSoonViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    /// <summary>--diagnostics CLI mode: dumps every raw field from every provider.</summary>
    private void RunDiagnosticsDump()
    {
        try
        {
            _ = AttachConsole(-1); // ATTACH_PARENT_PROCESS
        }
        catch
        {
            // no parent console (e.g. double-clicked) - the file output still works
        }

        try
        {
            AppPaths.EnsureDirectories();
            LoggingSetup.Configure(ReadPreSettings());
            var logger = Log.Logger;
            var real = new IBatteryProvider[]
            {
                new IoctlBatteryProvider(logger),
                new WmiBatteryProvider(logger),
                new Win32PowerProvider(logger),
            };
            var aggregated = new AggregatedBatteryProvider(real, new SystemInfoProvider(logger), logger);
            string dump = DiagnosticsDump.Build(real.Append(new MockBatteryProvider()), aggregated);
            try
            {
                Console.Out.Write(dump);
            }
            catch
            {
                // stdout unavailable without a console
            }
            File.WriteAllText(Path.Combine(AppPaths.LogsDir, "diagnostics.txt"), dump);
        }
        catch (Exception ex)
        {
            try
            {
                Console.Error.Write(ex.ToString());
            }
            catch
            {
                // ignore
            }
        }
        finally
        {
            LoggingSetup.Shutdown();
        }
        Shutdown(0);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private static AppSettings ReadPreSettings()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), SettingsService.JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // fall through to defaults; the real loader will handle diagnostics
        }
        return new AppSettings();
    }

    private int _consecutiveUiFailures;

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "Unhandled UI exception");
            _consecutiveUiFailures++;
            if (_consecutiveUiFailures >= 5)
            {
                // The same failure keeps recurring (usually layout): keeping the app alive
                // would just spin. Stop handling so the process terminates cleanly.
                Log.Fatal("UI exception loop detected; letting the process exit");
                return;
            }
            ShowKeptAliveDialog(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error(e.ExceptionObject as Exception, "Unhandled non-UI exception");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
        Dispatcher.Hooks.DispatcherInactive += ResetFailureCounter;
    }

    /// <summary>When the dispatcher goes idle, everything rendered fine - reset the loop guard.</summary>
    private void ResetFailureCounter(object? sender, EventArgs e) => _consecutiveUiFailures = 0;

    private static void ShowKeptAliveDialog(Exception ex)
    {
        try
        {
            string details = ex.ToString();
            try
            {
                System.Windows.Clipboard.SetText(details);
            }
            catch
            {
                // clipboard can be busy; not critical
            }
            _ = System.Windows.MessageBox.Show(
                "Something went wrong, but VoltPulse kept running.\n\n" +
                "Technical details were copied to the clipboard:\n\n" + details,
                "VoltPulse", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        catch
        {
            // never crash the crash handler
        }
    }
}
