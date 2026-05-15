using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using Svg;

namespace FloatShot;

/// <summary>
/// Renders embedded Microsoft Fluent UI System Icons (SVG) into colorized bitmaps.
/// Cache by (name, size, color) to avoid re-rasterising on every paint.
/// </summary>
internal static class FluentIcon
{
    public const string Camera      = "camera";
    public const string Pen         = "pen";
    public const string Pin         = "pin";
    public const string Copy        = "copy";
    public const string Save        = "save";
    public const string Dismiss     = "dismiss";
    public const string Settings    = "settings";
    public const string Folder      = "folder";
    public const string FullScreen  = "fullscreen";
    public const string Window      = "window";
    public const string ZoomIn      = "zoom_in";
    public const string ZoomOut     = "zoom_out";
    public const string ArrowReset  = "arrow_reset";
    public const string Info        = "info";
    public const string SignOut     = "sign_out";

    private static readonly Dictionary<string, SvgDocument> _docs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string name, int size, int color), Bitmap> _cache = new();
    private static readonly object _lock = new();

    /// <summary>Get a colorized rasterised icon at the requested square size.</summary>
    public static Bitmap Get(string name, int size, Color color)
    {
        lock (_lock)
        {
            var key = (name, size, color.ToArgb());
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var doc = LoadDoc(name);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

            if (doc is null)
            {
                // Fallback: draw a coloured square so missing icon is obvious in dev
                using var g = Graphics.FromImage(bmp);
                using var br = new SolidBrush(color);
                g.FillRectangle(br, 0, 0, size, size);
                _cache[key] = bmp;
                return bmp;
            }

            // Apply colour via SVG style override on every shape
            ApplyFill(doc, color);
            doc.Width  = size;
            doc.Height = size;
            doc.Draw(bmp);

            _cache[key] = bmp;
            return bmp;
        }
    }

    /// <summary>Draw an icon centred in <paramref name="bounds"/>.</summary>
    public static void Draw(Graphics g, string name, Rectangle bounds, Color color, int? sizeOverride = null)
    {
        int size = sizeOverride ?? Math.Min(bounds.Width, bounds.Height);
        var bmp = Get(name, size, color);
        int x = bounds.X + (bounds.Width  - size) / 2;
        int y = bounds.Y + (bounds.Height - size) / 2;
        g.DrawImage(bmp, x, y, size, size);
    }

    private static SvgDocument? LoadDoc(string name)
    {
        if (_docs.TryGetValue(name, out var d)) return d;
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($".fluent.{name}.svg", StringComparison.OrdinalIgnoreCase));
        if (resName is null) return null;
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream is null) return null;
        var doc = SvgDocument.Open<SvgDocument>(stream);
        _docs[name] = doc;
        return doc;
    }

    private static void ApplyFill(SvgElement el, Color color)
    {
        var paint = new SvgColourServer(color);
        el.Fill = paint;
        foreach (var child in el.Children)
            ApplyFill(child, color);
    }
}
