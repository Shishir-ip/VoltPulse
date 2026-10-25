using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VoltPulse.Core.Abstractions;
using VoltPulse.Core.Models;

namespace VoltPulse.App.Services;

/// <summary>Creates the share snapshot: a PNG of the Live screen plus a text summary.</summary>
public static class ShareService
{
    public static void CaptureLiveSnapshot(FrameworkElement source, string summary, string exportFolder)
    {
        try
        {
            if (source.ActualWidth < 10 || source.ActualHeight < 10) return;

            var rtb = new RenderTargetBitmap(
                (int)Math.Round(source.ActualWidth), (int)Math.Round(source.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(source);

            string dir = Directory.Exists(exportFolder) ? exportFolder : VoltPulse.Core.Paths.AppPaths.ExportsDir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"voltpulse-snapshot-{DateTime.Now:yyyyMMdd-HHmm}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            TryCopy(summary, rtb);
            ShowDone(path);
        }
        catch
        {
            // sharing must never break the app
        }
    }

    private static void TryCopy(string summary, RenderTargetBitmap image)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(image);
                System.Windows.Clipboard.SetText(summary);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
    }

    private static void ShowDone(string path)
    {
        var presenter = TryResolveAlertPresenter();
        presenter?.Show(new AlertEvent(
            AlertKind.FullyCharged, "Snapshot ready",
            $"The image was saved to {path} and the summary was copied to the clipboard.",
            DateTime.UtcNow, PlaySound: false));
    }

    private static IAlertPresenter? TryResolveAlertPresenter()
    {
        try
        {
            return App.HostServices?.GetService(typeof(IAlertPresenter)) as IAlertPresenter;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Choose-file dialog helper used by the Reports page.</summary>
public static class FileDialogs
{
    public static string? PickExportTarget(string defaultName, string filter)
    {
        var dialog = new SaveFileDialog
        {
            FileName = defaultName,
            Filter = filter,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
