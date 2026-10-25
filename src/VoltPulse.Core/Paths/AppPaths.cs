namespace VoltPulse.Core.Paths;

/// <summary>Well-known file system locations used by VoltPulse (all under %APPDATA%\VoltPulse).</summary>
public static class AppPaths
{
    public static string Base { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoltPulse");

    public static string LogsDir { get; } = Path.Combine(Base, "logs");
    public static string DataDir { get; } = Path.Combine(Base, "data");
    public static string ExportsDir { get; } = Path.Combine(Base, "exports");
    public static string CacheDir { get; } = Path.Combine(Base, "cache");

    public static string SettingsFile { get; } = Path.Combine(Base, "settings.json");
    public static string DatabaseFile { get; } = Path.Combine(DataDir, "history.db");
    public static string SettingsBackupFile { get; } = Path.Combine(Base, "settings.backup.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ExportsDir);
        Directory.CreateDirectory(CacheDir);
    }
}
