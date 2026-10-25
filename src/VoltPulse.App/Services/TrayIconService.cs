using System.Drawing;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using VoltPulse.Core.Models;
using VoltPulse.Services;

namespace VoltPulse.App.Services;

/// <summary>
/// System tray icon with a dynamically drawn battery glyph, live tooltip and full menu.
/// Uses the WinForms NotifyIcon - battle-tested, zero extra dependencies.
/// </summary>
public sealed class TrayIconService : IHostedService, IDisposable
{
    private readonly SamplerService _sampler;
    private readonly SettingsService _settings;
    private readonly AlertEngine _alerts;
    private readonly Dispatcher _dispatcher;
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private uint? _iconHandle;

    public TrayIconService(SamplerService sampler, SettingsService settings, AlertEngine alerts)
    {
        _sampler = sampler;
        _settings = settings;
        _alerts = alerts;
        _dispatcher = Dispatcher.CurrentDispatcher;

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "VoltPulse",
            Visible = true,
        };
        _icon.Icon = DrawStateIcon(BatteryLiveState.Unknown, 0);
        _icon.MouseClick += OnIconClick;
        BuildMenu();
    }

    private void BuildMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        _ = menu.Items.Add("Show VoltPulse", null, (s, e) => ShowWindow());
        var pause = new System.Windows.Forms.ToolStripMenuItem("Pause logging");
        pause.Click += (s, e) => _dispatcher.BeginInvoke(TogglePause);
        _ = menu.Items.Add(pause);
        var mini = new System.Windows.Forms.ToolStripMenuItem("Mini widget");
        mini.Click += (s, e) => _dispatcher.BeginInvoke(ToggleMiniWidget);
        _ = menu.Items.Add(mini);
        var snooze = new System.Windows.Forms.ToolStripMenuItem("Snooze alerts for 1 hour");
        snooze.Click += (s, e) => _alerts.SnoozeAll(TimeSpan.FromHours(1));
        _ = menu.Items.Add(snooze);
        _ = menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var settings = new System.Windows.Forms.ToolStripMenuItem("Settings");
        settings.Click += (s, e) => _dispatcher.BeginInvoke(() => NavigateTo("Settings"));
        _ = menu.Items.Add(settings);
        var exit = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exit.Click += (s, e) => _dispatcher.BeginInvoke(ExitApp);
        _ = menu.Items.Add(exit);
        _icon.ContextMenuStrip = menu;
    }

    private static void ShowWindow()
    {
        var window = Application.Current.MainWindow;
        if (window is null) return;
        window.Show();
        window.WindowState = System.Windows.WindowState.Normal;
        window.Activate();
    }

    private static void NavigateTo(string label) =>
        (Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel)?.NavigateToCommand.Execute(label);

    private void TogglePause()
    {
        _sampler.SetPaused(!_sampler.Paused);
    }

    private void ToggleMiniWidget()
    {
        _settings.Update(s => s.ShowMiniWidget = !s.ShowMiniWidget);
    }

    private static void ExitApp()
    {
        try
        {
            if (Application.Current is App app)
            {
                app.IsExiting = true;
                app.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    private void OnIconClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Left)
        {
            ShowWindow();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady += OnSnapshot;
        _settings.Changed += OnSettingsChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sampler.SnapshotReady -= OnSnapshot;
        _settings.Changed -= OnSettingsChanged;
        _icon.Visible = false;
        return Task.CompletedTask;
    }

    private void OnSettingsChanged(AppSettings settings) =>
        _dispatcher.BeginInvoke(() => _icon.Visible = settings.ShowTrayIcon);

    private void OnSnapshot(BatterySnapshot snapshot) =>
        _dispatcher.BeginInvoke(() => UpdateIcon(snapshot));

    private void UpdateIcon(BatterySnapshot snapshot)
    {
        var battery = snapshot.Combined ?? (snapshot.Batteries.Count > 0 ? snapshot.Batteries[0] : null);
        if (battery is null)
        {
            UpdateVisual(BatteryLiveState.Unknown, null, "no battery");
            return;
        }

        UpdateVisual(battery.LiveState(), battery.Percent.Value, Describe(battery));
    }

    private static string Describe(BatteryData battery)
    {
        string percent = battery.Percent.Value is { } p ? $"{p:0}%" : "?";
        string rate = battery.CurrentMilliamps() is { } ma
            ? $"{(ma >= 0 ? "+" : string.Empty)}{ValueFormat(ma)}"
            : "idle";
        return $"{percent} | {rate}";
    }

    private static string ValueFormat(double ma) =>
        Math.Abs(ma) >= 1000 ? $"{ma / 1000.0:0.00} A" : $"{ma:0} mA";

    private BatteryLiveState _lastState = BatteryLiveState.Unknown;
    private int _lastPercentBucket = -1;
    private string _lastTooltip = "";

    private void UpdateVisual(BatteryLiveState state, double? percent, string tooltip)
    {
        // Redraw the GDI icon only when something visible changed (avoids per-second GDI churn).
        int bucket = percent is { } p ? (int)Math.Round(p) : -1;
        bool changed = state != _lastState || bucket != _lastPercentBucket || tooltip != _lastTooltip;
        if (!changed) return;
        _lastState = state;
        _lastPercentBucket = bucket;
        _lastTooltip = tooltip;

        _icon.Text = Truncate($"VoltPulse - {tooltip}", 63);
        uint? previous = _iconHandle;
        _icon.Icon = DrawStateIcon(state, percent);
        // destroy the previous handle only after the new icon is in place
        if (previous is { } handle)
        {
            _ = DestroyIcon((IntPtr)handle);
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    /// <summary>Draws the tray glyph: battery outline, level fill in the state color, charging bolt.</summary>
    private Icon DrawStateIcon(BatteryLiveState state, double? percent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            string colorHex = state switch
            {
                BatteryLiveState.Charging => "#00BFA5",
                BatteryLiveState.Discharging => "#FF9100",
                BatteryLiveState.Full => "#66BB6A",
                BatteryLiveState.Critical => "#FF5252",
                _ => "#9E9E9E",
            };
            using var stateBrush = new SolidBrush(ColorTranslator.FromHtml(colorHex));
            using var outline = new Pen(Color.FromArgb(230, 235, 235), 2);

            // body
            var body = new Rectangle(2, 9, 24, 14);
            using (var bodyPath = RoundedRect(body, 3))
            {
                g.DrawPath(outline, bodyPath);
            }
            // terminal
            g.FillRectangle(stateBrush, 27, 13, 3, 6);

            if (percent is { } p)
            {
                int fillWidth = (int)Math.Round(20 * Math.Clamp(p, 0, 100) / 100.0);
                if (fillWidth > 0)
                {
                    using var fillPath = RoundedRect(new Rectangle(4, 11, fillWidth, 10), 2);
                    g.FillPath(stateBrush, fillPath);
                }
            }
        }

        IntPtr handle = bitmap.GetHicon();
        _iconHandle = (uint)handle;
        return Icon.FromHandle(handle);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        if (_iconHandle is { } handle)
        {
            _ = DestroyIcon((IntPtr)handle);
            _iconHandle = null;
        }
        _icon.Dispose();
        GC.SuppressFinalize(this);
    }
}
