using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FloatShot;

internal enum RegionAction { Cancel, Save, Copy, Pin }

internal enum RegionTool { RectangleMark, Pen, Pin, Copy, Save, Cancel }

internal enum EditMode { None, RectangleMark, Pen }

internal enum SelectionDragMode { None, NewSelection, MoveSelection, ResizeSelection }

[Flags]
internal enum ResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

/// <summary>Result of a region capture; <see cref="Image"/> is owned by the caller and must be disposed.</summary>
internal sealed record RegionResult(Bitmap Image, Rectangle ScreenBounds, RegionAction Action);

/// <summary>
/// PixPin-style region capture:
/// - Snapshot the whole virtual screen first.
/// - Show a fullscreen overlay that paints the snapshot dimmed; the selection
///   rectangle re-paints the original snapshot (so selected area looks bright).
/// - Floating Fluent toolbar (Pin / Copy / Save / Cancel) above the selection.
/// </summary>
internal static class RegionCapture
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    public static RegionResult? Pick()
    {
        // Snapshot the whole virtual screen BEFORE showing the overlay so we
        // get the real underlying content (devbox / w365 projection too).
        var virt = SystemInformation.VirtualScreen;
        var snapshot = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(snapshot))
            g.CopyFromScreen(virt.Location, Point.Empty, virt.Size, CopyPixelOperation.SourceCopy);

        var prev = GetForegroundWindow();

        Bitmap? cropped = null;
        Rectangle screenRect = Rectangle.Empty;
        RegionAction action = RegionAction.Cancel;

        try
        {
            using var f = new OverlayForm(snapshot, virt);
            var dr = f.ShowDialog();
            if (dr != DialogResult.OK || f.Action == RegionAction.Cancel) return null;

            action = f.Action;
            screenRect = f.SelectedScreenRect;

            // Export the crop from the overlay so annotation marks are included.
            cropped = f.RenderSelectedImage();
            return new RegionResult(cropped, screenRect, action);
        }
        finally
        {
            snapshot.Dispose();
            try { SetForegroundWindow(prev); } catch { }
        }
    }

    private sealed class OverlayForm : Form
    {
        private readonly Bitmap _snapshot;
        private readonly Rectangle _virt;
        private Point _start;
        private Rectangle _sel;            // overlay-local coordinates
        private SelectionDragMode _dragMode;
        private ResizeEdge _resizeEdge;
        private Rectangle _dragStartSelection;
        private bool _selected;
        private EditMode _editMode;
        private bool _marking;
        private Point _markStart;
        private RectangleF? _previewMark;
        private List<PointF>? _previewStroke;
        private readonly List<RectangleF> _marks = new();        // overlay-local coordinates
        private readonly List<List<PointF>> _strokes = new();    // overlay-local coordinates
        private List<ToolButton> _buttons = new();

        public Rectangle SelectedScreenRect { get; private set; }
        public RegionAction Action { get; private set; } = RegionAction.Cancel;

        public OverlayForm(Bitmap snapshot, Rectangle virt)
        {
            _snapshot = snapshot;
            _virt = virt;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.Manual;
            Bounds          = virt;
            ShowInTaskbar   = false;
            TopMost         = true;
            DoubleBuffered  = true;
            Cursor          = Cursors.Cross;
            KeyPreview      = true;
            BackColor       = Color.Black;
            Icon            = AppIcon.Get();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000008 | 0x00000080; // TOPMOST | TOOLWINDOW
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.PixelOffsetMode   = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.CompositingMode   = CompositingMode.SourceOver;
            g.SmoothingMode     = SmoothingMode.None;

            // 1. Draw the dimmed snapshot as base layer
            var attr = new ImageAttributes();
            var matrix = new ColorMatrix(new[]
            {
                new float[] { 0.50f, 0,     0,     0, 0 },
                new float[] { 0,     0.50f, 0,     0, 0 },
                new float[] { 0,     0,     0.50f, 0, 0 },
                new float[] { 0,     0,     0,     1, 0 },
                new float[] { 0,     0,     0,     0, 1 }
            });
            attr.SetColorMatrix(matrix);
            g.DrawImage(_snapshot,
                new Rectangle(0, 0, _virt.Width, _virt.Height),
                0, 0, _virt.Width, _virt.Height, GraphicsUnit.Pixel, attr);
            attr.Dispose();

            // 2. Selection: redraw original (bright) inside the rectangle
            if (_sel.Width > 0 && _sel.Height > 0)
            {
                g.DrawImage(_snapshot,
                    _sel,
                    _sel.X, _sel.Y, _sel.Width, _sel.Height,
                    GraphicsUnit.Pixel);

                DrawAnnotations(g);

                // 3. Bold outline + corner accents
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var outer = new Pen(Color.FromArgb(235, 255, 255, 255), 7.0f))
                    g.DrawRectangle(outer, _sel);
                using (var pen = new Pen(Fluent.Accent, 4.5f))
                    g.DrawRectangle(pen, _sel);

                int corner = 24;
                using (var glow = new Pen(Color.FromArgb(230, 255, 255, 255), 8.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(glow, _sel.Left, _sel.Top, _sel.Left + corner, _sel.Top);
                    g.DrawLine(glow, _sel.Left, _sel.Top, _sel.Left, _sel.Top + corner);
                    g.DrawLine(glow, _sel.Right, _sel.Top, _sel.Right - corner, _sel.Top);
                    g.DrawLine(glow, _sel.Right, _sel.Top, _sel.Right, _sel.Top + corner);
                    g.DrawLine(glow, _sel.Left, _sel.Bottom, _sel.Left + corner, _sel.Bottom);
                    g.DrawLine(glow, _sel.Left, _sel.Bottom, _sel.Left, _sel.Bottom - corner);
                    g.DrawLine(glow, _sel.Right, _sel.Bottom, _sel.Right - corner, _sel.Bottom);
                    g.DrawLine(glow, _sel.Right, _sel.Bottom, _sel.Right, _sel.Bottom - corner);
                }
                using (var pen = new Pen(Fluent.AccentHover, 6.0f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, _sel.Left, _sel.Top, _sel.Left + corner, _sel.Top);
                    g.DrawLine(pen, _sel.Left, _sel.Top, _sel.Left, _sel.Top + corner);
                    g.DrawLine(pen, _sel.Right, _sel.Top, _sel.Right - corner, _sel.Top);
                    g.DrawLine(pen, _sel.Right, _sel.Top, _sel.Right, _sel.Top + corner);
                    g.DrawLine(pen, _sel.Left, _sel.Bottom, _sel.Left + corner, _sel.Bottom);
                    g.DrawLine(pen, _sel.Left, _sel.Bottom, _sel.Left, _sel.Bottom - corner);
                    g.DrawLine(pen, _sel.Right, _sel.Bottom, _sel.Right - corner, _sel.Bottom);
                    g.DrawLine(pen, _sel.Right, _sel.Bottom, _sel.Right, _sel.Bottom - corner);
                }

                if (_selected)
                    DrawResizeHandles(g);

                // 4. Size pill (top-left of selection)
                var label = $"{_sel.Width} × {_sel.Height}";
                using var fnt = Fluent.TextFont(9.5f, FontStyle.Bold);
                var sz = g.MeasureString(label, fnt);
                int padX = 8, padY = 3;
                int lblW = (int)sz.Width + padX * 2;
                int lblH = (int)sz.Height + padY * 2;
                int lx = _sel.X;
                int ly = _sel.Y - lblH - 6;
                if (ly < 0) ly = _sel.Y + 6;
                using (var bgB = new SolidBrush(Color.FromArgb(220, 32, 32, 32)))
                using (var path = RoundedRect(new Rectangle(lx, ly, lblW, lblH), 4))
                    g.FillPath(bgB, path);
                g.DrawString(label, fnt, Brushes.White, lx + padX, ly + padY);
            }

            if (_selected) DrawToolbar(g);
            else if (_dragMode == SelectionDragMode.None && _sel.Width == 0) DrawCenterHint(g);
        }

        private void DrawCenterHint(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var hint = "Drag to select region   ·   Esc / Right-click to cancel";
            using var fnt = Fluent.TextFont(11.5f);
            var sz = g.MeasureString(hint, fnt);
            int padX = 18, padY = 10;
            int hW = (int)sz.Width + padX * 2;
            int hH = (int)sz.Height + padY * 2;
            int lx = (_virt.Width - hW) / 2;
            int ly = 32;
            using (var bgB = new SolidBrush(Color.FromArgb(220, 32, 32, 32)))
            using (var path = RoundedRect(new Rectangle(lx, ly, hW, hH), Fluent.Radius))
                g.FillPath(bgB, path);
            using (var pen = new Pen(Fluent.Border, 1f))
            using (var path = RoundedRect(new Rectangle(lx, ly, hW, hH), Fluent.Radius))
                g.DrawPath(pen, path);
            g.DrawString(hint, fnt, Brushes.White, lx + padX, ly + padY);
        }

        private void DrawToolbar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            const int btnSize = 48, gap = 4, pad = 8;
            int barW = (btnSize * _buttons.Count) + (gap * (_buttons.Count - 1)) + pad * 2;
            int barH = btnSize + pad * 2;

            // Default: right-bottom of selection, with auto-fallback when off-screen.
            int bx = _sel.Right - barW;            // align right edge
            int by = _sel.Bottom + 8;              // below selection
            // Fallback chain when no room below or off the right edge
            if (by + barH > _virt.Height) by = _sel.Top - barH - 8;
            if (by < 0)                   by = Math.Min(_sel.Bottom - barH - 8, _virt.Height - barH - 8);  // inside selection bottom
            if (by < 0)                   by = 8;
            if (bx + barW > _virt.Width)  bx = _virt.Width - barW - 8;
            if (bx < 0)                   bx = 8;

            var barRect = new Rectangle(bx, by, barW, barH);

            // Soft drop shadow
            for (int i = 0; i < 4; i++)
            {
                using var shadow = new SolidBrush(Color.FromArgb(48 - i * 10, 0, 0, 0));
                using var sp = RoundedRect(new Rectangle(bx - i, by + 2 + i, barW + i * 2, barH + i), Fluent.Radius);
                g.FillPath(shadow, sp);
            }

            // White Mica-like body + 1px subtle border
            using (var bg = new SolidBrush(Color.FromArgb(245, 250, 250, 250)))
            using (var path = RoundedRect(barRect, Fluent.Radius))
                g.FillPath(bg, path);
            using (var pen = new Pen(Color.FromArgb(60, 0, 0, 0), 1f))
            using (var path = RoundedRect(barRect, Fluent.Radius))
                g.DrawPath(pen, path);

            // Buttons
            int x = bx + pad;
            int y = by + pad;
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                b.Bounds = new Rectangle(x, y, btnSize, btnSize);

                bool activeTool = (b.Tool == RegionTool.RectangleMark && _editMode == EditMode.RectangleMark)
                    || (b.Tool == RegionTool.Pen && _editMode == EditMode.Pen);

                // Hover / active background (subtle grey, accent-tinted for cancel)
                if (b.Hovered || activeTool)
                {
                    Color hoverBg = b.Tool == RegionTool.Cancel
                        ? Color.FromArgb(40, 196, 43, 28)
                        : activeTool
                            ? Color.FromArgb(46, b.AccentColor.R, b.AccentColor.G, b.AccentColor.B)
                            : Color.FromArgb(40, 0, 0, 0);
                    using var hb = new SolidBrush(hoverBg);
                    using var hp = RoundedRect(b.Bounds, Fluent.RadiusSmall);
                    g.FillPath(hb, hp);
                }

                // Icon colour: dark grey on white; accent on hover
                Color iconColor = b.Action switch
                {
                    RegionAction.Cancel when b.Hovered || activeTool => Fluent.AccentRed,
                    RegionAction.Cancel                              => Color.FromArgb(255, 90, 90, 90),
                    _ when b.Hovered || activeTool                   => b.AccentColor,
                    _                                                => Color.FromArgb(255, 60, 60, 60)
                };

                DrawToolIcon(g, b, iconColor);

                x += btnSize + gap;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_selected)
            {
                if (e.Button == MouseButtons.Left)
                {
                    foreach (var b in _buttons)
                    {
                        if (b.Bounds.Contains(e.Location))
                        {
                            HandleToolClick(b.Tool);
                            return;
                        }
                    }

                    var hit = HitTestResize(e.Location);
                    if (_sel.Contains(e.Location) || hit != ResizeEdge.None)
                    {
                        if (_editMode == EditMode.RectangleMark)
                        {
                            _marking = true;
                            _markStart = e.Location;
                            _previewMark = null;
                            Capture = true;
                            Cursor = Cursors.Cross;
                            return;
                        }

                        if (_editMode == EditMode.Pen)
                        {
                            _marking = true;
                            _previewStroke = new List<PointF> { e.Location };
                            Capture = true;
                            Cursor = Cursors.Cross;
                            return;
                        }

                        _dragMode = hit == ResizeEdge.None ? SelectionDragMode.MoveSelection : SelectionDragMode.ResizeSelection;
                        _resizeEdge = hit;
                        _start = e.Location;
                        _dragStartSelection = _sel;
                        Capture = true;
                        return;
                    }
                }
                else if (e.Button == MouseButtons.Right)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                _dragMode = SelectionDragMode.NewSelection;
                _start = e.Location;
                _sel = new Rectangle(e.X, e.Y, 0, 0);
                Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_selected)
            {
                if (_marking)
                {
                    if (_editMode == EditMode.RectangleMark)
                        _previewMark = NormalizeRect(_markStart, ClampToSelection(e.Location));
                    else if (_editMode == EditMode.Pen && _previewStroke is not null)
                        _previewStroke.Add(ClampToSelection(e.Location));
                    Invalidate();
                    return;
                }

                if (_dragMode == SelectionDragMode.MoveSelection)
                {
                    var dx = e.X - _start.X;
                    var dy = e.Y - _start.Y;
                    _sel = ClampSelection(new Rectangle(_dragStartSelection.X + dx, _dragStartSelection.Y + dy, _dragStartSelection.Width, _dragStartSelection.Height));
                    SelectedScreenRect = ToScreenRect(_sel);
                    Invalidate();
                    return;
                }

                if (_dragMode == SelectionDragMode.ResizeSelection)
                {
                    _sel = ResizeSelection(_dragStartSelection, _resizeEdge, e.Location);
                    SelectedScreenRect = ToScreenRect(_sel);
                    Invalidate();
                    return;
                }

                bool changed = false;
                foreach (var b in _buttons)
                {
                    var hover = b.Bounds.Contains(e.Location);
                    if (hover != b.Hovered) { b.Hovered = hover; changed = true; }
                }
                Cursor = _buttons.Any(b => b.Hovered)
                    ? Cursors.Hand
                    : _editMode == EditMode.None
                        ? CursorForResize(HitTestResize(e.Location), _sel.Contains(e.Location))
                        : _sel.Contains(e.Location) ? Cursors.Cross : Cursors.Default;
                if (changed) Invalidate();
                return;
            }

            if (_dragMode != SelectionDragMode.NewSelection) return;
            var x = Math.Min(_start.X, e.X);
            var y = Math.Min(_start.Y, e.Y);
            var w = Math.Abs(e.X - _start.X);
            var h = Math.Abs(e.Y - _start.Y);
            _sel = new Rectangle(x, y, w, h);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_selected)
            {
                if (_marking)
                {
                    _marking = false;
                    Capture = false;
                    if (_editMode == EditMode.RectangleMark)
                    {
                        var rect = NormalizeRect(_markStart, ClampToSelection(e.Location));
                        _previewMark = null;
                        if (rect.Width >= 6 && rect.Height >= 6)
                            _marks.Add(rect);
                    }
                    else if (_editMode == EditMode.Pen && _previewStroke is not null)
                    {
                        if (_previewStroke.Count >= 2)
                            _strokes.Add(_previewStroke.ToList());
                        _previewStroke = null;
                    }
                    Invalidate();
                    return;
                }

                _dragMode = SelectionDragMode.None;
                _resizeEdge = ResizeEdge.None;
                Capture = false;
                return;
            }

            if (_dragMode != SelectionDragMode.NewSelection) return;
            _dragMode = SelectionDragMode.None;
            if (_sel.Width > 4 && _sel.Height > 4)
                EnterToolbarMode();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (_selected)
            {
                if (e.Control && e.KeyCode == Keys.Z)
                {
                    UndoAnnotation();
                    e.Handled = true;
                    return;
                }

                switch (e.KeyCode)
                {
                    case Keys.M: ToggleEditMode(EditMode.RectangleMark); break;
                    case Keys.B: ToggleEditMode(EditMode.Pen); break;
                    case Keys.P: Confirm(RegionAction.Pin);  break;
                    case Keys.C: Confirm(RegionAction.Copy); break;
                    case Keys.S:
                    case Keys.Enter: Confirm(RegionAction.Save); break;
                }
            }
            else if (e.KeyCode == Keys.Enter && _sel.Width > 0 && _sel.Height > 0)
            {
                EnterToolbarMode();
            }
        }

        private void EnterToolbarMode()
        {
            SelectedScreenRect = ToScreenRect(_sel);
            _selected = true;
            _buttons = new List<ToolButton>
            {
                new() { Tool = RegionTool.RectangleMark, AccentColor = Fluent.AccentRed    },
                new() { Tool = RegionTool.Pen,  IconName = FluentIcon.Pen,     AccentColor = Fluent.AccentBlue   },
                new() { Tool = RegionTool.Pin,  IconName = FluentIcon.Pin,     Action = RegionAction.Pin,    AccentColor = Fluent.AccentPurple },
                new() { Tool = RegionTool.Copy, IconName = FluentIcon.Copy,    Action = RegionAction.Copy,   AccentColor = Fluent.AccentBlue   },
                new() { Tool = RegionTool.Save, IconName = FluentIcon.Save,    Action = RegionAction.Save,   AccentColor = Fluent.AccentGreen  },
                new() { Tool = RegionTool.Cancel, IconName = FluentIcon.Dismiss, Action = RegionAction.Cancel, AccentColor = Fluent.AccentRed  }
            };
            Cursor = Cursors.Default;
            Invalidate();
        }

        public Bitmap RenderSelectedImage()
        {
            var localRect = new Rectangle(_sel.X, _sel.Y, _sel.Width, _sel.Height);
            var rendered = _snapshot.Clone(localRect, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(rendered);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SetClip(new Rectangle(0, 0, rendered.Width, rendered.Height));
            foreach (var mark in _marks)
            {
                var shifted = new RectangleF(mark.X - _sel.X, mark.Y - _sel.Y, mark.Width, mark.Height);
                DrawMark(g, shifted, preview: false);
            }
            foreach (var stroke in _strokes)
            {
                var shifted = stroke.Select(p => new PointF(p.X - _sel.X, p.Y - _sel.Y)).ToList();
                DrawStroke(g, shifted, preview: false);
            }
            return rendered;
        }

        private void HandleToolClick(RegionTool tool)
        {
            switch (tool)
            {
                case RegionTool.RectangleMark:
                    ToggleEditMode(EditMode.RectangleMark);
                    break;
                case RegionTool.Pen:
                    ToggleEditMode(EditMode.Pen);
                    break;
                case RegionTool.Pin:
                    Confirm(RegionAction.Pin);
                    break;
                case RegionTool.Copy:
                    Confirm(RegionAction.Copy);
                    break;
                case RegionTool.Save:
                    Confirm(RegionAction.Save);
                    break;
                case RegionTool.Cancel:
                    Confirm(RegionAction.Cancel);
                    break;
            }
        }

        private void ToggleEditMode(EditMode mode)
        {
            _editMode = _editMode == mode ? EditMode.None : mode;
            Cursor = _editMode == EditMode.None ? Cursors.Default : Cursors.Cross;
            Invalidate();
        }

        private void UndoAnnotation()
        {
            if (_strokes.Count > 0)
            {
                _strokes.RemoveAt(_strokes.Count - 1);
                Invalidate();
                return;
            }

            if (_marks.Count > 0)
            {
                _marks.RemoveAt(_marks.Count - 1);
                Invalidate();
            }
        }

        private Rectangle ToScreenRect(Rectangle r) => new(_virt.X + r.X, _virt.Y + r.Y, r.Width, r.Height);

        private Rectangle ClampSelection(Rectangle r)
        {
            int w = Math.Max(12, Math.Min(r.Width, _virt.Width));
            int h = Math.Max(12, Math.Min(r.Height, _virt.Height));
            int x = Math.Clamp(r.X, 0, Math.Max(0, _virt.Width - w));
            int y = Math.Clamp(r.Y, 0, Math.Max(0, _virt.Height - h));
            return new Rectangle(x, y, w, h);
        }

        private Rectangle ResizeSelection(Rectangle origin, ResizeEdge edge, Point p)
        {
            const int minSize = 20;
            int left = origin.Left, top = origin.Top, right = origin.Right, bottom = origin.Bottom;

            if (edge.HasFlag(ResizeEdge.Left)) left = Math.Clamp(p.X, 0, right - minSize);
            if (edge.HasFlag(ResizeEdge.Right)) right = Math.Clamp(p.X, left + minSize, _virt.Width);
            if (edge.HasFlag(ResizeEdge.Top)) top = Math.Clamp(p.Y, 0, bottom - minSize);
            if (edge.HasFlag(ResizeEdge.Bottom)) bottom = Math.Clamp(p.Y, top + minSize, _virt.Height);

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private ResizeEdge HitTestResize(Point p)
        {
            const int grip = 9;
            if (!_sel.Contains(p) && !Inflate(_sel, grip).Contains(p)) return ResizeEdge.None;

            ResizeEdge edge = ResizeEdge.None;
            if (Math.Abs(p.X - _sel.Left) <= grip) edge |= ResizeEdge.Left;
            if (Math.Abs(p.X - _sel.Right) <= grip) edge |= ResizeEdge.Right;
            if (Math.Abs(p.Y - _sel.Top) <= grip) edge |= ResizeEdge.Top;
            if (Math.Abs(p.Y - _sel.Bottom) <= grip) edge |= ResizeEdge.Bottom;
            return edge;
        }

        private static Rectangle Inflate(Rectangle r, int amount)
        {
            r.Inflate(amount, amount);
            return r;
        }

        private static Cursor CursorForResize(ResizeEdge edge, bool inside)
        {
            return edge switch
            {
                ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
                ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
                ResizeEdge.Left | ResizeEdge.Top or ResizeEdge.Right | ResizeEdge.Bottom => Cursors.SizeNWSE,
                ResizeEdge.Right | ResizeEdge.Top or ResizeEdge.Left | ResizeEdge.Bottom => Cursors.SizeNESW,
                _ when inside => Cursors.SizeAll,
                _ => Cursors.Default
            };
        }

        private Point ClampToSelection(Point p) => new(
            Math.Clamp(p.X, _sel.Left, _sel.Right),
            Math.Clamp(p.Y, _sel.Top, _sel.Bottom));

        private static RectangleF NormalizeRect(Point a, Point b) => RectangleF.FromLTRB(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        private void DrawAnnotations(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var clip = new Region(_sel);
            var oldClip = g.Clip;
            g.Clip = clip;

            foreach (var mark in _marks)
                DrawMark(g, mark, preview: false);
            if (_previewMark is { } preview)
                DrawMark(g, preview, preview: true);

            foreach (var stroke in _strokes)
                DrawStroke(g, stroke, preview: false);
            if (_previewStroke is { Count: > 1 } previewStroke)
                DrawStroke(g, previewStroke, preview: true);

            g.Clip = oldClip;
        }

        private static void DrawMark(Graphics g, RectangleF rect, bool preview)
        {
            if (rect.Width < 2 || rect.Height < 2) return;
            using var glow = new Pen(Color.FromArgb(190, 255, 255, 255), preview ? 6f : 7f) { LineJoin = LineJoin.Round };
            using var outline = new Pen(Color.FromArgb(245, 255, 42, 42), preview ? 3.5f : 5f) { LineJoin = LineJoin.Round };
            g.DrawRectangle(glow, rect.X, rect.Y, rect.Width, rect.Height);
            g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static void DrawStroke(Graphics g, IReadOnlyList<PointF> points, bool preview)
        {
            if (points.Count < 2) return;
            using var glow = new Pen(Color.FromArgb(190, 255, 255, 255), preview ? 8f : 9f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var pen = new Pen(Color.FromArgb(245, 255, 42, 42), preview ? 4f : 5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            var arr = points.ToArray();
            g.DrawLines(glow, arr);
            g.DrawLines(pen, arr);
        }

        private void DrawResizeHandles(Graphics g)
        {
            const int size = 15;
            foreach (var c in HandleCenters())
            {
                var rect = new Rectangle(c.X - size / 2, c.Y - size / 2, size, size);
                var shadow = rect;
                shadow.Offset(1, 2);
                using var shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
                using var bg = new SolidBrush(Color.White);
                using var pen = new Pen(Fluent.AccentHover, 3f);
                g.FillEllipse(shadowBrush, shadow);
                g.FillEllipse(bg, rect);
                g.DrawEllipse(pen, rect);
            }
        }

        private IEnumerable<Point> HandleCenters()
        {
            int cx = _sel.Left + _sel.Width / 2;
            int cy = _sel.Top + _sel.Height / 2;
            yield return new Point(_sel.Left, _sel.Top);
            yield return new Point(cx, _sel.Top);
            yield return new Point(_sel.Right, _sel.Top);
            yield return new Point(_sel.Right, cy);
            yield return new Point(_sel.Right, _sel.Bottom);
            yield return new Point(cx, _sel.Bottom);
            yield return new Point(_sel.Left, _sel.Bottom);
            yield return new Point(_sel.Left, cy);
        }

        private static void DrawToolIcon(Graphics g, ToolButton b, Color color)
        {
            if (b.Tool == RegionTool.RectangleMark)
            {
                var r = Centered(b.Bounds, 24, 18);
                using var pen = new Pen(color, 3f) { LineJoin = LineJoin.Round };
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                return;
            }

            FluentIcon.Draw(g, b.IconName, b.Bounds, color, sizeOverride: 24);
        }

        private static Rectangle Centered(Rectangle bounds, int width, int height) => new(
            bounds.Left + (bounds.Width - width) / 2,
            bounds.Top + (bounds.Height - height) / 2,
            width,
            height);

        private void Confirm(RegionAction a)
        {
            Action = a;
            DialogResult = a == RegionAction.Cancel ? DialogResult.Cancel : DialogResult.OK;
            Close();
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

        private sealed class ToolButton
        {
            public RegionTool Tool;
            public string IconName = "";
            public RegionAction Action;
            public Rectangle Bounds;
            public bool Hovered;
            public Color AccentColor = Color.Gray;
        }
    }
}
