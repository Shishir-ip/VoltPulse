using Microsoft.Win32;
using VoltPulse.Core.Models;

namespace VoltPulse.App.Services;

/// <summary>
/// Registers a Windows per-app GPU preference ("High performance") for VoltPulse so hybrid-GPU
/// laptops run the UI and rendering pipeline on the discrete GPU (e.g. an RTX 4060) instead of
/// the integrated one. This is the same mechanism as Windows Settings > Display > Graphics
/// (HKCU\Software\Microsoft\DirectX\UserGpuPreferences, "GpuPreference=2;").
/// </summary>
public static class GpuPreference
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerformance = "GpuPreference=2;";

    public static void Apply(AppSettings settings)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (key is null) return;

            if (settings.PreferDiscreteGpu)
            {
                key.SetValue(exePath, HighPerformance, RegistryValueKind.String);
            }
            else if (key.GetValue(exePath) is not null)
            {
                key.DeleteValue(exePath, false);
            }
        }
        catch
        {
            // GPU preference is an optimization; never let it break startup
        }
    }
}
