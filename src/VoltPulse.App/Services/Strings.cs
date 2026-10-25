using System.Globalization;
using System.Resources;

namespace VoltPulse.App.Services;

/// <summary>
/// Localization access. Ship English in Resources\Strings.resx; add a language by copying it to
/// Strings.&lt;code&gt;.resx (e.g. Strings.bn.resx) and translating the values - no code changes.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("VoltPulse.App.Resources.Strings", typeof(Strings).Assembly);

    public static string T(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}
