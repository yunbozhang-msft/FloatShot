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
        var result = RegionCapture.Pick(settings.SaveFolder, folder =>
        {
            settings.SaveFolder = folder;
            settings.Save();
        });
        if (result is null) return null;

        var bmp = result.Image;   // already cropped from the pre-overlay snapshot

        switch (result.Action)
        {
            case RegionAction.Save:
            case RegionAction.SaveAs:
                return SaveAndPostProcess(bmp, settings, settings.CopyToClipboard, settings.OpenFolderAfterCapture,
                    ownsBmp: true, saveAs: result.Action == RegionAction.SaveAs);

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

    private static string? SaveAndPostProcess(Bitmap bmp, Settings settings, bool copyToClipboard, bool openFolder, bool ownsBmp = false, bool saveAs = false)
    {
        try
        {
            var fileName = string.Format(settings.FileNamePattern, DateTime.Now);
            string fullPath;
            if (saveAs)
            {
                var initialFolder = Directory.Exists(settings.SaveFolder)
                    ? settings.SaveFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                using var dialog = new SaveFileDialog
                {
                    Title = "Save screenshot as",
                    Filter = "PNG image (*.png)|*.png",
                    DefaultExt = "png",
                    AddExtension = true,
                    OverwritePrompt = true,
                    CheckPathExists = true,
                    RestoreDirectory = true,
                    InitialDirectory = initialFolder,
                    FileName = GetAvailableFileName(initialFolder, fileName)
                };
                if (dialog.ShowDialog() != DialogResult.OK) return null;
                fullPath = dialog.FileName;
                bmp.Save(fullPath, ImageFormat.Png);
            }
            else
            {
                Directory.CreateDirectory(settings.SaveFolder);
                fullPath = Path.GetFullPath(Path.Combine(settings.SaveFolder, GetAvailableFileName(settings.SaveFolder, fileName)));
                using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
                bmp.Save(output, ImageFormat.Png);
            }

            var saveFolder = Path.GetDirectoryName(fullPath)!;
            if (!string.Equals(settings.SaveFolder, saveFolder, StringComparison.OrdinalIgnoreCase))
            {
                settings.SaveFolder = saveFolder;
            }

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

    internal static string GetAvailableFileName(string folder, string fileName)
    {
        var candidate = fileName;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (int suffix = 1; File.Exists(Path.Combine(folder, candidate)) || Directory.Exists(Path.Combine(folder, candidate)); suffix++)
            candidate = $"{stem}_{suffix}{extension}";
        return candidate;
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
