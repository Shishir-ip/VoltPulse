using Microsoft.Win32;
using VoltPulse.Core.Models;

namespace VoltPulse.Services;

/// <summary>Manages the "start with Windows" Run registry key (per-user, no admin needed).</summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoltPulse";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? AppContext.BaseDirectory + "VoltPulse.exe";
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception)
        {
            // registry may be locked down; the toggle simply won't stick
        }
    }

    /// <summary>Syncs the registry with the setting (called at startup and on change).</summary>
    public static void Apply(AppSettings settings)
    {
        bool currently = IsEnabled();
        if (settings.StartWithWindows && !currently)
        {
            SetEnabled(true);
        }
        else if (!settings.StartWithWindows && currently)
        {
            SetEnabled(false);
        }
    }
}
