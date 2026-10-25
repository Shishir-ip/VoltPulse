using System.Windows;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;

namespace VoltPulse.App.Services;

/// <summary>
/// Presents alerts as an in-app toast (works on any Windows version, no packaging needed)
/// with an optional system sound.
/// </summary>
public sealed class AlertPresenter : IAlertPresenter, IDisposable
{
    private readonly SynchronizationContext? _ui;
    private ToastWindow? _toast;

    public AlertPresenter()
    {
        _ui = SynchronizationContext.Current;
    }

    public void Show(AlertEvent alert)
    {
        void Render()
        {
            try
            {
                _toast ??= new ToastWindow();
                _toast.ShowToast(alert.Title, alert.Message);
                if (alert.PlaySound)
                {
                    System.Media.SystemSounds.Exclamation.Play();
                }
            }
            catch
            {
                // notifications must never crash the sampler
            }
        }

        if (_ui is not null)
        {
            _ui.Post(_ => Render(), null);
        }
        else
        {
            Application.Current?.Dispatcher.BeginInvoke(Render);
        }
    }

    public void Dispose()
    {
        _toast?.Close();
        _toast = null;
    }
}
