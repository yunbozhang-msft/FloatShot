using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FloatShot;

/// <summary>
/// 屏幕贴图便签 (PixPin 同款) — Win11 Fluent 风格。
/// - layered + toolwindow + topmost (跨虚拟桌面 / W365 投影可见)
/// - 1px 灰描边 + 淡阴影
/// - 左键拖动, 滚轮缩放 (50%-400%), 双击/Esc 关闭
/// - Mark mode: draw red rectangles to highlight important areas
/// - 右键菜单: Copy / Save as / Reset zoom / Close
/// </summary>
internal sealed class PinnedImage : Form
{
    private static readonly List<PinnedImage> _all = new();

    private const int ShadowPad = 4;   // 透明区给阴影留位

    private readonly Bitmap _original;
    private readonly List<RectangleF> _marks = new();
    private float _zoom = 1.0f;
    private bool _dragging;
    private bool _markMode;
    private bool _marking;
    private Point _dragStartScreen;
    private Point _formStart;
    private Point _markStart;
    private RectangleF? _previewMark;

    public static void ShowAt(Bitmap bmp, Point screenLocation)
    {
        var p = new PinnedImage(bmp);
        // screenLocation 是图像左上角的屏幕坐标; 减掉 shadowPad 让图像本身贴在原位
        p.Location = new Point(screenLocation.X - ShadowPad, screenLocation.Y - ShadowPad);
        p.Show();
        _all.Add(p);
        p.FormClosed += (_, _) => _all.Remove(p);
    }

    private PinnedImage(Bitmap bmp)
    {
        _original = bmp;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        TopMost         = true;
        DoubleBuffered  = true;
        Cursor          = Cursors.SizeAll;
        BackColor       = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Icon            = AppIcon.Get();
        Text            = "FloatShot Pin";
        Size            = new Size(bmp.Width + ShadowPad * 2, bmp.Height + ShadowPad * 2);
        ContextMenuStrip = BuildMenu();
        KeyPreview      = true;
        TabStop         = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000008 | 0x00000080;
            return cp;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        // Win11 light theme menu — generous padding, real icons, no cramped look
        var menu = new ContextMenuStrip
        {
            Renderer = new LightMenuRenderer(),
            BackColor = Color.White,
            ShowImageMargin = true,
            ImageScalingSize = new Size(18, 18),
            Font = new Font("Segoe UI Variable", 10f),
            Padding = new Padding(4)
        };

        AddItem(menu, FluentIcon.Copy,       "Copy",        Keys.Control | Keys.C, (_, _) => CopyToClipboard());
        AddItem(menu, FluentIcon.Save,       "Save as...",  Keys.Control | Keys.S, (_, _) => SaveAs());
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, FluentIcon.Info,       "Mark rectangle", Keys.M,             (_, _) => ToggleMarkMode());
        AddItem(menu, FluentIcon.ArrowReset, "Undo mark",      Keys.Control | Keys.Z, (_, _) => UndoMark());
        AddItem(menu, FluentIcon.Dismiss,    "Clear marks",    Keys.None,          (_, _) => ClearMarks());
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, FluentIcon.ArrowReset, "Reset zoom",  Keys.None,             (_, _) => { _zoom = 1.0f; ApplyZoom(); });
        AddItem(menu, FluentIcon.ZoomIn,     "Zoom in",     Keys.None,             (_, _) => { _zoom = Math.Min(4.0f, _zoom + 0.1f); ApplyZoom(); });
        AddItem(menu, FluentIcon.ZoomOut,    "Zoom out",    Keys.None,             (_, _) => { _zoom = Math.Max(0.5f, _zoom - 0.1f); ApplyZoom(); });
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, FluentIcon.Dismiss,    "Close",       Keys.Escape,           (_, _) => Close());
        return menu;
    }

    private static void AddItem(ContextMenuStrip menu, string iconName, string text, Keys shortcut, EventHandler onClick)
    {
        var it = new ToolStripMenuItem(text)
        {
            ForeColor = Color.FromArgb(28, 28, 28),
            Font = new Font("Segoe UI Variable", 10f),
            Padding = new Padding(2, 4, 8, 4),
            Image = FluentIcon.Get(iconName, 18, Color.FromArgb(70, 70, 70))
        };
        if (shortcut != Keys.None) it.ShortcutKeyDisplayString = ShortcutLabel(shortcut);
        it.Click += onClick;
        menu.Items.Add(it);
    }

    private static string ShortcutLabel(Keys k)
    {
        var parts = new List<string>();
        if ((k & Keys.Control) != 0) parts.Add("Ctrl");
        if ((k & Keys.Alt) != 0) parts.Add("Alt");
        if ((k & Keys.Shift) != 0) parts.Add("Shift");
        var key = k & ~(Keys.Control | Keys.Alt | Keys.Shift);
        if (key != Keys.None) parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
        g.SmoothingMode     = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(TransparencyKey))
            g.FillRectangle(bg, ClientRectangle);

        var dispW = (int)(_original.Width * _zoom);
        var dispH = (int)(_original.Height * _zoom);
        var imgRect = new Rectangle(ShadowPad, ShadowPad, dispW, dispH);

        // 阴影 (3 层)
        for (int i = 0; i < 3; i++)
        {
            int s = ShadowPad - i;
            int alpha = 18 - i * 5;
            using var shadow = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            g.FillRectangle(shadow,
                ShadowPad - s,
                ShadowPad - s + 1,
                dispW + s * 2,
                dispH + s * 2);
        }

        // 图像
        g.DrawImage(_original, imgRect);

        DrawMarks(g);

        // 1px 灰描边 (Fluent 风格, 不抢眼)
        using var pen = new Pen(Color.FromArgb(180, 90, 90, 90), 1f);
        g.DrawRectangle(pen, imgRect);

        if (_markMode)
            DrawMarkBadge(g);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();

        if (_markMode && e.Button == MouseButtons.Left && IsInImage(e.Location))
        {
            _marking = true;
            _markStart = e.Location;
            _previewMark = null;
            Capture = true;
            Cursor = Cursors.Cross;
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStartScreen = Cursor.Position;
            _formStart = Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_marking)
        {
            _previewMark = ClientToImageRect(_markStart, e.Location);
            Invalidate();
            return;
        }

        if (_dragging && (e.Button & MouseButtons.Left) != 0)
        {
            var cur = Cursor.Position;
            Location = new Point(_formStart.X + cur.X - _dragStartScreen.X,
                                 _formStart.Y + cur.Y - _dragStartScreen.Y);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_marking)
        {
            _marking = false;
            Capture = false;
            var rect = ClientToImageRect(_markStart, e.Location);
            _previewMark = null;
            if (rect.Width >= 6 && rect.Height >= 6)
                _marks.Add(rect);
            Invalidate();
            return;
        }

        _dragging = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Close();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var delta = e.Delta > 0 ? 0.1f : -0.1f;
        _zoom = Math.Clamp(_zoom + delta, 0.5f, 4.0f);
        ApplyZoom();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        else if (e.Control && e.KeyCode == Keys.C) CopyToClipboard();
        else if (e.Control && e.KeyCode == Keys.S) SaveAs();
        else if (e.Control && e.KeyCode == Keys.Z) UndoMark();
        else if (e.KeyCode == Keys.M) ToggleMarkMode();
        else if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
        {
            _zoom = Math.Min(4.0f, _zoom + 0.1f); ApplyZoom();
        }
        else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
        {
            _zoom = Math.Max(0.5f, _zoom - 0.1f); ApplyZoom();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    private void ApplyZoom()
    {
        Size = new Size((int)(_original.Width * _zoom) + ShadowPad * 2, (int)(_original.Height * _zoom) + ShadowPad * 2);
        Invalidate();
    }

    private void CopyToClipboard()
    {
        try
        {
            using var rendered = RenderAnnotatedImage();
            Clipboard.SetImage(rendered);
        }
        catch { }
    }

    private void SaveAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
            FileName = $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                ImageFormat fmt = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                    ".bmp"            => ImageFormat.Bmp,
                    _                 => ImageFormat.Png
                };
                using var rendered = RenderAnnotatedImage();
                rendered.Save(dlg.FileName, fmt);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message, "FloatShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ToggleMarkMode()
    {
        _markMode = !_markMode;
        Cursor = _markMode ? Cursors.Cross : Cursors.SizeAll;
        Invalidate();
    }

    private void UndoMark()
    {
        if (_marks.Count == 0) return;
        _marks.RemoveAt(_marks.Count - 1);
        Invalidate();
    }

    private void ClearMarks()
    {
        if (_marks.Count == 0) return;
        _marks.Clear();
        Invalidate();
    }

    private bool IsInImage(Point p)
    {
        var dispW = (int)(_original.Width * _zoom);
        var dispH = (int)(_original.Height * _zoom);
        return new Rectangle(ShadowPad, ShadowPad, dispW, dispH).Contains(p);
    }

    private RectangleF ClientToImageRect(Point a, Point b)
    {
        var x1 = Math.Clamp((Math.Min(a.X, b.X) - ShadowPad) / _zoom, 0, _original.Width);
        var y1 = Math.Clamp((Math.Min(a.Y, b.Y) - ShadowPad) / _zoom, 0, _original.Height);
        var x2 = Math.Clamp((Math.Max(a.X, b.X) - ShadowPad) / _zoom, 0, _original.Width);
        var y2 = Math.Clamp((Math.Max(a.Y, b.Y) - ShadowPad) / _zoom, 0, _original.Height);
        return RectangleF.FromLTRB(x1, y1, x2, y2);
    }

    private RectangleF ImageToClientRect(RectangleF r) => new(
        ShadowPad + r.X * _zoom,
        ShadowPad + r.Y * _zoom,
        r.Width * _zoom,
        r.Height * _zoom);

    private void DrawMarks(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var mark in _marks)
            DrawMark(g, ImageToClientRect(mark), preview: false);
        if (_previewMark is { } preview)
            DrawMark(g, ImageToClientRect(preview), preview: true);
    }

    private static void DrawMark(Graphics g, RectangleF rect, bool preview)
    {
        if (rect.Width < 2 || rect.Height < 2) return;

        using var fill = new SolidBrush(Color.FromArgb(preview ? 30 : 22, 255, 45, 45));
        using var outline = new Pen(Color.FromArgb(245, 255, 42, 42), preview ? 3.5f : 4.0f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var glow = new Pen(Color.FromArgb(180, 255, 255, 255), outline.Width + 2.0f)
        {
            LineJoin = LineJoin.Round
        };

        g.FillRectangle(fill, rect);
        g.DrawRectangle(glow, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void DrawMarkBadge(Graphics g)
    {
        const string text = "Mark mode";
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        var size = g.MeasureString(text, font);
        var rect = new Rectangle(ShadowPad + 10, ShadowPad + 10, (int)size.Width + 22, (int)size.Height + 12);
        using var bg = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
        using var border = new Pen(Color.FromArgb(210, 255, 42, 42), 1.5f);
        using var path = RoundedRect(rect, 8);
        g.FillPath(bg, path);
        g.DrawPath(border, path);
        using var brush = new SolidBrush(Color.FromArgb(190, 24, 24));
        g.DrawString(text, font, brush, rect.Left + 11, rect.Top + 6);
    }

    private Bitmap RenderAnnotatedImage()
    {
        var rendered = new Bitmap(_original.Width, _original.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(rendered);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(_original, 0, 0, _original.Width, _original.Height);
        foreach (var mark in _marks)
            DrawExportMark(g, mark);
        return rendered;
    }

    private static void DrawExportMark(Graphics g, RectangleF rect)
    {
        if (rect.Width < 2 || rect.Height < 2) return;
        using var fill = new SolidBrush(Color.FromArgb(22, 255, 45, 45));
        using var glow = new Pen(Color.FromArgb(190, 255, 255, 255), 7f) { LineJoin = LineJoin.Round };
        using var outline = new Pen(Color.FromArgb(245, 255, 42, 42), 5f) { LineJoin = LineJoin.Round };
        g.FillRectangle(fill, rect);
        g.DrawRectangle(glow, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _original?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Win11 light theme renderer for ContextMenuStrip</summary>
    private sealed class LightMenuRenderer : ToolStripProfessionalRenderer
    {
        public LightMenuRenderer() : base(new LightColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.FromArgb(28, 28, 28);
            base.OnRenderItemText(e);
        }
    }

    private sealed class LightColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected             => Color.FromArgb(243, 243, 243);
        public override Color MenuItemSelectedGradientBegin=> Color.FromArgb(243, 243, 243);
        public override Color MenuItemSelectedGradientEnd  => Color.FromArgb(243, 243, 243);
        public override Color MenuItemBorder               => Color.Transparent;
        public override Color MenuBorder                   => Color.FromArgb(229, 229, 229);
        public override Color ToolStripDropDownBackground  => Color.White;
        public override Color ImageMarginGradientBegin     => Color.White;
        public override Color ImageMarginGradientMiddle    => Color.White;
        public override Color ImageMarginGradientEnd       => Color.White;
        public override Color SeparatorDark                => Color.FromArgb(232, 232, 232);
        public override Color SeparatorLight               => Color.FromArgb(232, 232, 232);
    }
}
