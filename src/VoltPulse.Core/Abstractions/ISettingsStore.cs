using VoltPulse.Core.Models;

namespace VoltPulse.Core.Abstractions;

/// <summary>Displays alerts to the user (toast window, balloon tip, sound...).</summary>
public interface IAlertPresenter
{
    void Show(AlertEvent alert);
}

/// <summary>Persistence of application settings.</summary>
public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
