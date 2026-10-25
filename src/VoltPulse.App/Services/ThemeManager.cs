using System.Windows;
using VoltPulse.Core.Models;

namespace VoltPulse.App.Services;

/// <summary>Applies the Dark/Light/Follow-Windows theme by swapping the first merged dictionary.</summary>
public static class ThemeManager
{
    private static readonly Uri DarkUri = new("pack://application:,,,/Theme/Dark.xaml");
    private static readonly Uri LightUri = new("pack://application:,,,/Theme/Light.xaml");

    /// <summary>Raised after the active theme changed (dark mode = true).</summary>
    public static event Action<bool>? ThemeApplied;

    public static bool IsDarkMode { get; private set; } = true;

    public static void Apply(AppSettings settings)
    {
        var mode = settings.Theme;
        if (mode == ThemeMode.FollowWindows)
        {
            mode = IsWindowsLightTheme() ? ThemeMode.Light : ThemeMode.Dark;
        }
        if (SystemParameters.HighContrast)
        {
            IsDarkMode = false;
            ThemeApplied?.Invoke(false);
            return; // let the system high-contrast colors win entirely
        }

        Uri uri = mode == ThemeMode.Light ? LightUri : DarkUri;
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source == DarkUri || d.Source == LightUri);
        if (existing is not null)
        {
            int index = dictionaries.IndexOf(existing);
            dictionaries[index] = new ResourceDictionary { Source = uri };
        }
        else
        {
            dictionaries.Insert(0, new ResourceDictionary { Source = uri });
        }
        IsDarkMode = mode == ThemeMode.Dark;
        ThemeApplied?.Invoke(IsDarkMode);
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }
}
