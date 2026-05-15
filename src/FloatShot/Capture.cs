using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FloatShot;

internal static class Capture
{
    /// <summary>Returns saved file path, or null if cancelled / pinned / copied (no file written).</summary>
    public static string? Run(CaptureMode mode, Settings settings, FloatingButton? hideMe = null)
    {
        var wasVisible = hideMe is { Visible: true };
        if (wasVisible) hideMe!.Visible = false;
        Application.DoEvents();
        Thread.Sleep(80);

        try
        {
            if (mode == CaptureMode.Region)
                return RunRegion(settings);

            using var bmp = mode switch
            {
                CaptureMode.FullScreen    => CaptureBounds(SystemInformation.VirtualScreen),
                CaptureMode.PrimaryScreen => CaptureBounds(Screen.PrimaryScreen!.Bounds),
                CaptureMode.ActiveWindow  => CaptureActiveWindow(),
                _                         => CaptureBounds(SystemInformation.VirtualScreen)
            };
            return SaveAndPostProcess(bmp, settings, settings.CopyToClipboard, settings.OpenFolderAfterCapture);
        }
        finally
        {
            if (wasVisible && hideMe is not null) hideMe.Visible = true;
        }
    }

    private static string? RunRegion(Settings settings)
    {
        var result = RegionCapture.Pick();
        if (result is null) return null;

        var bmp = result.Image;   // already cropped from the pre-overlay snapshot

        switch (result.Action)
        {
            case RegionAction.Save:
                return SaveAndPostProcess(bmp, settings, settings.CopyToClipboard, settings.OpenFolderAfterCapture, ownsBmp: true);

            case RegionAction.Copy:
                try { Clipboard.SetImage(bmp); } catch { }
                bmp.Dispose();
                return null;

            case RegionAction.Pin:
                PinnedImage.ShowAt(bmp, result.ScreenBounds.Location);
                return null;

            default:
                bmp.Dispose();
                return null;
        }
    }

    private static string? SaveAndPostProcess(Bitmap bmp, Settings settings, bool copyToClipboard, bool openFolder, bool ownsBmp = false)
    {
        try
        {
            Directory.CreateDirectory(settings.SaveFolder);
            var fileName = string.Format(settings.FileNamePattern, DateTime.Now);
            var fullPath = Path.Combine(settings.SaveFolder, fileName);
            bmp.Save(fullPath, ImageFormat.Png);

            if (copyToClipboard)
                try { Clipboard.SetImage(bmp); } catch { }

            if (openFolder)
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{fullPath}\""); } catch { }

            return fullPath;
        }
        finally
        {
            if (ownsBmp) bmp.Dispose();
        }
    }

    private static Bitmap CaptureBounds(Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    private static Bitmap CaptureActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return CaptureBounds(SystemInformation.VirtualScreen);
        if (!GetWindowRect(hwnd, out var r)) return CaptureBounds(SystemInformation.VirtualScreen);
        var rect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
        rect.Intersect(SystemInformation.VirtualScreen);
        if (rect.Width <= 0 || rect.Height <= 0) return CaptureBounds(SystemInformation.VirtualScreen);
        return CaptureBounds(rect);
    }
}
