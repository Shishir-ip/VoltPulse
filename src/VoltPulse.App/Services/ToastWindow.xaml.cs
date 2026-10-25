using System.Windows;
using System.Windows.Threading;

namespace VoltPulse.App.Services;

/// <summary>Bottom-right toast popup used for alerts. Click dismisses; auto-closes after 6 s.</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    public ToastWindow()
    {
        InitializeComponent();
        _closeTimer.Tick += (_, _) => HideToast();
    }

    public void ShowToast(string title, string message)
    {
        TitleText.Text = title;
        MessageText.Text = message;

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 14;
        Top = area.Bottom - Height - 14;

        if (!IsVisible)
        {
            Show();
        }
        Opacity = 1;
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    public void HideToast()
    {
        _closeTimer.Stop();
        Hide();
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        HideToast();
    }
}
