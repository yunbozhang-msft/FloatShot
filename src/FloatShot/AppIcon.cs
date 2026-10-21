using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

namespace FloatShot;

internal static class AppIcon
{
    private static Icon? _cached;
    private static readonly Dictionary<int, Bitmap> _bitmaps = new();

    public static Bitmap GetBitmap(int size)
    {
        if (_bitmaps.TryGetValue(size, out var cached)) return cached;
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith("floatshot.ico", StringComparison.OrdinalIgnoreCase));
        using var stream = resource is null ? null : assembly.GetManifestResourceStream(resource);
        var sourceSize = new Size(Math.Max(256, size), Math.Max(256, size));
        using var icon = stream is null ? new Icon(Get(), sourceSize) : new Icon(stream, sourceSize);
        using var source = icon.ToBitmap();
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (source.Size == bitmap.Size) graphics.DrawImageUnscaled(source, Point.Empty);
            else graphics.DrawImage(source, new Rectangle(Point.Empty, bitmap.Size));
        }
        _bitmaps[size] = bitmap;
        return bitmap;
    }

    public static Icon Get()
    {
        if (_cached is not null) return _cached;
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("floatshot.ico", StringComparison.OrdinalIgnoreCase));
        if (resName is not null)
        {
            using var s = asm.GetManifestResourceStream(resName);
            if (s is not null)
            {
                _cached = new Icon(s);
                return _cached;
            }
        }
        _cached = SystemIcons.Application;
        return _cached;
    }
}
