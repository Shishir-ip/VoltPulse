using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.App.Services;

/// <summary>
/// Always-on-top, draggable, semi-transparent overlay showing the live reading.
/// Position, opacity and click-through persist in settings.
/// </summary>
public sealed class MiniWidgetManager : IHostedService, IDisposable
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;

    private readonly SamplerService _sampler;
    private readonly SettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private MiniWidgetWindow? _window;

    public MiniWidgetManager(SamplerService sampler, SettingsService settings)
    {
        _sampler = sampler;
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady += OnSnapshot;
        _settings.Changed += OnSettingsChanged;
        ApplyVisibility(_settings.Current);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady -= OnSnapshot;
        _settings.Changed -= OnSettingsChanged;
        CloseWindow();
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(AppSettings settings) =>
        _dispatcher.BeginInvoke(() => ApplyVisibility(settings));

    private void ApplyVisibility(AppSettings settings)
    {
        if (settings.ShowMiniWidget)
        {
            _window ??= new MiniWidgetWindow(_settings, () => _settings.Update(s => s.ShowMiniWidget = false));
            _window.Opacity = settings.MiniWidget.Opacity;
            _window.Left = settings.MiniWidget.X;
            _window.Top = settings.MiniWidget.Y;
            SetClickThrough(_window, settings.MiniWidget.ClickThrough);
            if (!_window.IsVisible)
            {
                _window.Show();
            }
        }
        else
        {
            CloseWindow();
        }
    }

    private void CloseWindow()
    {
        _window?.Close();
        _window = null;
    }

    private void OnSnapshot(BatterySnapshot snapshot) =>
        _dispatcher.BeginInvoke(() =>
        {
            var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
            if (battery is null || _window is null) return;
            _window.Update(battery);
        });

    private static void SetClickThrough(Window window, bool enabled)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int exStyle = GetWindowLong(hwnd, GwlExstyle);
            int target = enabled
                ? exStyle | WsExTransparent | WsExLayered
                : exStyle & ~WsExTransparent;
            if (target != exStyle)
            {
                _ = SetWindowLong(hwnd, GwlExstyle, target);
            }
        }
        catch
        {
            // click-through is optional
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public void Dispose()
    {
        CloseWindow();
        GC.SuppressFinalize(this);
    }
}

public partial class MiniWidgetWindow : Window
{
    private readonly SettingsService _settings;
    private readonly Action _hideRequested;
    private bool _persisting;

    public MiniWidgetWindow(SettingsService settings, Action hideRequested)
    {
        InitializeComponent();
        _settings = settings;
        _hideRequested = hideRequested;
        LocationChanged += OnLocationChanged;
    }

    public void Update(BatteryData battery)
    {
        double? ma = battery.CurrentMilliamps(_settings.Current.InvertSignConvention);
        if (ma is null)
        {
            ValueText.Text = battery.ChargeState.Value is ChargeState.FullyCharged or ChargeState.IdleOnAc ? "Idle" : "—";
            UnitText.Text = string.Empty;
        }
        else
        {
            double abs = Math.Abs(ma.Value);
            ValueText.Text = abs >= 1000 ? $"{ma.Value / 1000.0:0.00}" : $"{ma.Value:0}";
            UnitText.Text = abs >= 1000 ? "A" : "mA";
        }
        ValueText.Foreground = StateBrush(battery.LiveState());
        PercentText.Text = battery.Percent.Value is { } p ? $"{p:0}%" : "—";
        StateText.Text = battery.ChargeState.Value switch
        {
            ChargeState.Charging => "charging",
            ChargeState.Discharging => "on battery",
            ChargeState.FullyCharged => "full",
            ChargeState.IdleOnAc => "idle",
            _ => "",
        };
    }

    private static System.Windows.Media.Brush StateBrush(BatteryLiveState state) =>
        System.Windows.Application.Current.TryFindResource(state switch
        {
            BatteryLiveState.Charging => "BrushStateCharging",
            BatteryLiveState.Discharging => "BrushStateDischarging",
            BatteryLiveState.Full => "BrushStateFull",
            BatteryLiveState.Critical => "BrushStateCritical",
            _ => "BrushStateUnknown",
        }) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;

    private void OnDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;
        try
        {
            DragMove();
            PersistPosition();
        }
        catch
        {
            // DragMove throws if the mouse was released; harmless
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        // persist occasionally via drag end (PersistPosition is called there); nothing per-move
    }

    private void PersistPosition()
    {
        if (_persisting) return;
        _persisting = true;
        try
        {
            double x = Left, y = Top;
            _settings.Update(s =>
            {
                s.MiniWidget.X = Math.Clamp(x, -100, SystemParameters.VirtualScreenWidth - 50);
                s.MiniWidget.Y = Math.Clamp(y, 0, SystemParameters.VirtualScreenHeight - 30);
            });
        }
        finally
        {
            _persisting = false;
        }
    }

    private void OnHide(object sender, RoutedEventArgs e)
    {
        _hideRequested();
    }
}
