using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VoltPulse.App.Services;
using VoltPulse.App.ViewModels;
using VoltPulse.Services;

namespace VoltPulse.App;

public partial class MainWindow : Window
{
    private readonly SamplerService _sampler;
    private readonly SettingsService? _settings;

    public MainWindow(MainViewModel viewModel, SamplerService sampler, SettingsService settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _sampler = sampler;
        _settings = settings;
        Closing += OnWindowClosing;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
            {
                Dispatcher.BeginInvoke(AnimatePageTransition);
            }
        };

        ApplyFontScale(settings.Current.FontScale);
        settings.Changed += s => Dispatcher.BeginInvoke(() => ApplyFontScale(s.FontScale));

        IsVisibleChanged += (_, e) => UpdateWindowVisibility();
        StateChanged += (_, _) => UpdateWindowVisibility();
        Services.ThemeManager.ThemeApplied += _ => Dispatcher.BeginInvoke(UpdateTitleBarTheme);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateTitleBarTheme();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void UpdateTitleBarTheme()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = Services.ThemeManager.IsDarkMode ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            _ = DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int)); // older builds
        }
        catch
        {
            // dark title bar is a nicety, never a failure
        }
    }

    private void AnimatePageTransition()
    {
        try
        {
            var transform = new TranslateTransform(26, 0);
            PageHost.RenderTransform = transform;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            var slideIn = new System.Windows.Media.Animation.DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            PageHost.BeginAnimation(OpacityProperty, fadeIn);
            transform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }
        catch
        {
            // animation is decorative
        }
    }

    private void UpdateWindowVisibility()
    {
        _sampler.WindowVisible = IsVisible && WindowState != WindowState.Minimized;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // close-to-tray unless a real exit is in progress (tray Exit sets the flag first)
        if (_settings is not null && _settings.Current.CloseToTray && Application.Current is not App { IsExiting: true })
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void ApplyFontScale(double scale)
    {
        if (Math.Abs(scale - 1.0) < 0.01)
        {
            RootGrid.LayoutTransform = Transform.Identity;
        }
        else
        {
            RootGrid.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }
}
