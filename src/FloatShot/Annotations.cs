using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json.Serialization;

namespace FloatShot;

internal enum AnnotationTool { None, Rectangle, Arrow, Pen, Highlight, Mosaic, Text }

internal sealed class AnnotationStyle
{
    public int ColorArgb { get; set; } = Color.FromArgb(230, 45, 45).ToArgb();
    public int Width { get; set; } = 3;
    public bool Filled { get; set; }
    public bool Brush { get; set; }
    public int Strength { get; set; } = 16;
    public int FontSize { get; set; } = 24;
    public bool Bold { get; set; }

    public AnnotationStyle Copy() => (AnnotationStyle)MemberwiseClone();
    [JsonIgnore]
    public Color Ink => Color.FromArgb(255, Color.FromArgb(ColorArgb));

    public void Normalize()
    {
        ColorArgb = Ink.ToArgb();
        Width = Math.Clamp(Width, 2, 80);
        Strength = Math.Clamp(Strength, 2, 40);
        FontSize = Math.Clamp(FontSize, 12, 72);
    }
}

internal sealed class AnnotationPreferences
{
    public AnnotationStyle Rectangle { get; set; } = new();
    public AnnotationStyle Arrow { get; set; } = new();
    public AnnotationStyle Pen { get; set; } = new();
    public AnnotationStyle Highlight { get; set; } = new() { ColorArgb = Color.Yellow.ToArgb(), Width = 24 };
    public AnnotationStyle Mosaic { get; set; } = new() { Width = 40 };
    public AnnotationStyle Text { get; set; } = new();

    public AnnotationStyle For(AnnotationTool tool) => tool switch
    {
        AnnotationTool.Rectangle => Rectangle,
        AnnotationTool.Arrow => Arrow,
        AnnotationTool.Pen => Pen,
        AnnotationTool.Highlight => Highlight,
        AnnotationTool.Mosaic => Mosaic,
        AnnotationTool.Text => Text,
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    public void Normalize()
    {
        Rectangle ??= new(); Arrow ??= new(); Pen ??= new(); Text ??= new();
        Highlight ??= new() { ColorArgb = Color.Yellow.ToArgb(), Width = 24 };
        Mosaic ??= new() { Width = 40 };
        foreach (var tool in Enum.GetValues<AnnotationTool>().Where(tool => tool != AnnotationTool.None)) For(tool).Normalize();
        Rectangle.Width = Math.Min(16, Rectangle.Width); Arrow.Width = Math.Min(16, Arrow.Width); Pen.Width = Math.Min(16, Pen.Width);
        Highlight.Width = Math.Clamp(Highlight.Width, 8, 48);
    }
}

internal sealed record Annotation(AnnotationTool Tool, AnnotationStyle Style, List<PointF> Points, string Text = "")
{
    public RectangleF Bounds
    {
        get
        {
            if (Points.Count == 0) return RectangleF.Empty;
            return RectangleF.FromLTRB(Points.Min(point => point.X), Points.Min(point => point.Y),
                Points.Max(point => point.X), Points.Max(point => point.Y));
        }
    }
}

internal sealed class AnnotationDocument : IDisposable
{
    private readonly Bitmap _source;
    private readonly List<Annotation> _items = new();
    private readonly Stack<List<Annotation>> _undo = new();
    private readonly Stack<List<Annotation>> _redo = new();
    private Bitmap? _cache;
    private Rectangle _cacheBounds;

    public AnnotationDocument(Bitmap source) => _source = source;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int Count => _items.Count;
    public Annotation this[int index] => _items[index];

    public void Add(Annotation annotation)
    {
        if (annotation.Points.Count == 0 || annotation.Tool == AnnotationTool.None) return;
        if (annotation.Tool == AnnotationTool.Text && string.IsNullOrWhiteSpace(annotation.Text)) return;
        Remember();
        _items.Add(annotation with { Style = annotation.Style.Copy(), Points = annotation.Points.ToList() });
        Invalidate();
    }

    public void Replace(int index, Annotation annotation)
    {
        Remember();
        _items[index] = annotation with { Style = annotation.Style.Copy(), Points = annotation.Points.ToList() };
        Invalidate();
    }

    public void Remove(int index)
    {
        Remember(); _items.RemoveAt(index); Invalidate();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(_items.ToList()); Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Push(_items.ToList()); Restore(_redo.Pop());
    }

    private void Remember() { _undo.Push(_items.ToList()); _redo.Clear(); }
    private void Restore(List<Annotation> items) { _items.Clear(); _items.AddRange(items); Invalidate(); }

    public Bitmap Render(Rectangle region, Annotation? preview = null, int excluded = -1)
    {
        region.Intersect(new Rectangle(Point.Empty, _source.Size));
        if (region.Width <= 0 || region.Height <= 0) throw new ArgumentOutOfRangeException(nameof(region));
        if (excluded >= 0)
        {
            var edited = _source.Clone(region, PixelFormat.Format32bppArgb);
            for (int index = 0; index < _items.Count; index++) if (index != excluded) Draw(edited, _items[index], region.Location);
            if (preview is not null) Draw(edited, preview, region.Location);
            return edited;
        }
        if (_cache is null || _cacheBounds != region)
        {
            Invalidate();
            _cache = _source.Clone(region, PixelFormat.Format32bppArgb);
            foreach (var item in _items) Draw(_cache, item, region.Location);
            _cacheBounds = region;
        }
        var result = _cache.Clone(new Rectangle(Point.Empty, _cache.Size), PixelFormat.Format32bppArgb);
        if (preview is not null) Draw(result, preview, region.Location);
        return result;
    }

    public int HitText(PointF point)
    {
        using var graphics = Graphics.FromImage(_source);
        for (int index = _items.Count - 1; index >= 0; index--)
        {
            var item = _items[index];
            if (item.Tool != AnnotationTool.Text) continue;
            using var font = TextFont(item.Style);
            var anchor = item.Points[0];
            var measured = graphics.MeasureString(item.Text, font, Math.Max(1, _source.Width - (int)anchor.X));
            var bounds = new RectangleF(anchor, measured); bounds.Inflate(4, 4);
            if (bounds.Contains(point)) return index;
        }
        return -1;
    }

    public static Font TextFont(AnnotationStyle style, float scale = 1) =>
        new("Segoe UI", Math.Max(1, style.FontSize * scale), style.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    private static GraphicsPath StrokePath(Annotation annotation, Point offset)
    {
        var path = new GraphicsPath(FillMode.Winding);
        var points = annotation.Points.Select(point => new PointF(point.X - offset.X, point.Y - offset.Y)).ToArray();
        if (points.Length == 1 || points.All(point => point == points[0]))
        {
            float radius = annotation.Style.Width / 2f;
            path.AddEllipse(points[0].X - radius, points[0].Y - radius, radius * 2, radius * 2);
        }
        else
        {
            path.AddLines(points);
            using var pen = new Pen(Color.Black, annotation.Style.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            path.Widen(pen);
        }
        return path;
    }

    private static void Draw(Bitmap target, Annotation annotation, Point offset)
    {
        var bounds = annotation.Bounds; bounds.Offset(-offset.X, -offset.Y);
        var style = annotation.Style;
        if (annotation.Tool == AnnotationTool.Mosaic)
        {
            using var mask = style.Brush ? StrokePath(annotation, offset) : new GraphicsPath();
            if (!style.Brush)
            {
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
                mask.AddRectangle(bounds);
            }
            var affected = Rectangle.Intersect(Rectangle.Ceiling(mask.GetBounds()), new Rectangle(Point.Empty, target.Size));
            if (affected.Width <= 0 || affected.Height <= 0) return;
            using var sample = target.Clone(affected, PixelFormat.Format32bppArgb);
            using var pixels = new Bitmap(Math.Max(1, (int)Math.Ceiling(affected.Width / (double)style.Strength)), Math.Max(1, (int)Math.Ceiling(affected.Height / (double)style.Strength)));
            using (var downsample = Graphics.FromImage(pixels))
            {
                downsample.InterpolationMode = InterpolationMode.HighQualityBilinear;
                downsample.PixelOffsetMode = PixelOffsetMode.Half;
                downsample.CompositingMode = CompositingMode.SourceCopy;
                using var edge = new ImageAttributes(); edge.SetWrapMode(WrapMode.TileFlipXY);
                downsample.DrawImage(sample, new Rectangle(Point.Empty, pixels.Size), 0, 0, sample.Width, sample.Height, GraphicsUnit.Pixel, edge);
            }
            using var graphics = Graphics.FromImage(target);
            graphics.SetClip(mask); graphics.InterpolationMode = InterpolationMode.NearestNeighbor; graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using var attributes = new ImageAttributes(); attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(pixels, affected, 0, 0, pixels.Width, pixels.Height, GraphicsUnit.Pixel, attributes);
            return;
        }
        using var canvas = Graphics.FromImage(target);
        canvas.SmoothingMode = SmoothingMode.AntiAlias; canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var brush = new SolidBrush(annotation.Tool == AnnotationTool.Highlight ? Color.FromArgb(115, style.Ink) : style.Ink);
        switch (annotation.Tool)
        {
            case AnnotationTool.Rectangle:
                if (style.Filled) canvas.FillRectangle(brush, bounds);
                else { using var outline = new Pen(style.Ink, style.Width) { LineJoin = LineJoin.Round }; canvas.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height); }
                break;
            case AnnotationTool.Pen:
            case AnnotationTool.Highlight:
                using (var path = StrokePath(annotation, offset)) canvas.FillPath(brush, path);
                break;
            case AnnotationTool.Arrow:
                var start = annotation.Points[0]; var end = annotation.Points[^1];
                using (var arrow = new Pen(style.Ink, style.Width) { StartCap = LineCap.Round })
                using (var cap = new AdjustableArrowCap(4, 5, true)) { arrow.CustomEndCap = cap; canvas.DrawLine(arrow, start.X - offset.X, start.Y - offset.Y, end.X - offset.X, end.Y - offset.Y); }
                break;
            case AnnotationTool.Text:
                using (var font = TextFont(style))
                    canvas.DrawString(annotation.Text, font, brush, new RectangleF(bounds.X, bounds.Y, Math.Max(1, target.Width - bounds.X), Math.Max(1, target.Height - bounds.Y)));
                break;
        }
    }

    private void Invalidate() { _cache?.Dispose(); _cache = null; }
    public void Dispose() => Invalidate();
}