using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FloatShot;

internal enum RegionAction { Cancel, Save, Copy, Pin, SaveAs }
internal enum SelectionDragMode { None, NewSelection, MoveSelection, ResizeSelection }
[Flags]
internal enum ResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }
internal sealed record RegionResult(Bitmap Image, Rectangle ScreenBounds, RegionAction Action);

internal static class RegionCapture
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);

    public static RegionResult? Pick(string? saveFolder = null, Action<string>? saveFolderChanged = null, Settings? settings = null)
    {
        var virtualScreen = SystemInformation.VirtualScreen;
        using var snapshot = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(snapshot))
            graphics.CopyFromScreen(virtualScreen.Location, Point.Empty, virtualScreen.Size, CopyPixelOperation.SourceCopy);
        var foreground = GetForegroundWindow();
        try
        {
            using var overlay = new OverlayForm(snapshot, virtualScreen, saveFolder, saveFolderChanged, settings);
            if (overlay.ShowDialog() != DialogResult.OK || overlay.Action == RegionAction.Cancel) return null;
            return new RegionResult(overlay.RenderSelectedImage(), overlay.SelectedScreenRect, overlay.Action);
        }
        finally { try { SetForegroundWindow(foreground); } catch { } }
    }

    private sealed class OverlayForm : Form
    {
        private readonly Bitmap _snapshot;
        private readonly Rectangle _virt;
        private readonly Settings _settings;
        private readonly Action<string>? _saveFolderChanged;
        private readonly AnnotationEditor _editor;
        private ContextMenuStrip? _saveMenu;
        private string _saveFolder;
        private Rectangle _sel;
        private Rectangle _dragStartSelection;
        private Point _start;
        private bool _selected;
        private SelectionDragMode _dragMode;
        private ResizeEdge _resizeEdge;
        private Rectangle _lastToolbarBounds;

        public Rectangle SelectedScreenRect { get; private set; }
        public RegionAction Action { get; private set; } = RegionAction.Cancel;

        public OverlayForm(Bitmap snapshot, Rectangle virtualScreen, string? saveFolder = null, Action<string>? saveFolderChanged = null, Settings? settings = null)
        {
            _snapshot = snapshot; _virt = virtualScreen; _settings = settings ?? Settings.Load();
            _saveFolder = saveFolder ?? _settings.SaveFolder; _saveFolderChanged = saveFolderChanged;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            Bounds = virtualScreen; ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            KeyPreview = true; Cursor = Cursors.Cross; BackColor = Color.Black; Icon = AppIcon.Get();
            _editor = new AnnotationEditor(this, snapshot, () => _sel, () => _sel, _settings);
            _editor.Toolbar.Visible = false;
            Controls.Add(_editor.Toolbar);
            _editor.Changed += () => { if (_selected) PositionToolbar(); };
            _editor.ActionRequested += action =>
            {
                switch (action)
                {
                    case "pin": Confirm(RegionAction.Pin); break;
                    case "copy": Confirm(RegionAction.Copy); break;
                    case "save": Confirm(RegionAction.Save); break;
                    case "options": ShowSaveMenu(); break;
                    case "close": Confirm(RegionAction.Cancel); break;
                }
            };
            _editor.Toolbar.SetSaveFolder(_saveFolder);
        }

        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x00000008 | 0x00000080; return parameters; }
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var graphics = args.Graphics;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix(new[]
            {
                new float[] { .5f, 0, 0, 0, 0 }, new float[] { 0, .5f, 0, 0, 0 },
                new float[] { 0, 0, .5f, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 }
            }));
            graphics.DrawImage(_snapshot, ClientRectangle, 0, 0, _snapshot.Width, _snapshot.Height, GraphicsUnit.Pixel, attributes);
            if (_sel.Width > 0 && _sel.Height > 0)
            {
                _editor.Draw(graphics);
                using var white = new Pen(Color.White, 4);
                using var blue = new Pen(Fluent.Accent, 2);
                graphics.DrawRectangle(white, _sel); graphics.DrawRectangle(blue, _sel);
                if (_selected)
                {
                    foreach (var point in HandleCenters())
                    {
                        var handle = new Rectangle(point.X - 4, point.Y - 4, 8, 8);
                        graphics.FillRectangle(Brushes.White, handle); graphics.DrawRectangle(blue, handle);
                    }
                }
                string label = $"{_sel.Width} x {_sel.Height}";
                using var font = Fluent.TextFont(9);
                var size = TextRenderer.MeasureText(label, font);
                var labelBounds = new Rectangle(_sel.Left, Math.Max(0, _sel.Top - size.Height - 10), size.Width + 12, size.Height + 6);
                using var background = new SolidBrush(Color.FromArgb(35, 35, 35));
                graphics.FillRectangle(background, labelBounds);
                TextRenderer.DrawText(graphics, label, font, labelBounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                using var font = Fluent.TextFont(11);
                var hint = new Rectangle(0, 25, Width, 40);
                TextRenderer.DrawText(graphics, "Drag to select region", font, hint, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private void PositionToolbar()
        {
            var toolbar = _editor.Toolbar;
            var monitor = RectangleToClient(Screen.FromPoint(PointToScreen(new Point(_sel.Right - 1, _sel.Bottom - 1))).Bounds);
            toolbar.Location = toolbar.Arrange(monitor, _sel.Right, _sel.Bottom + 10, _sel.Top - 10).Location;
            var exposed = _lastToolbarBounds.IsEmpty ? toolbar.Bounds : Rectangle.Union(_lastToolbarBounds, toolbar.Bounds);
            _lastToolbarBounds = toolbar.Bounds;
            Invalidate(exposed);
        }

        protected override void OnMouseDown(MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Right) { Confirm(RegionAction.Cancel); return; }
            if (args.Button != MouseButtons.Left) return;
            if (!_selected)
            {
                _dragMode = SelectionDragMode.NewSelection; _start = args.Location; _sel = new Rectangle(_start, Size.Empty); Capture = true; return;
            }
            if (_editor.MouseDown(args)) return;
            var hit = HitTestResize(args.Location);
            if (_editor.Tool == AnnotationTool.None && (_sel.Contains(args.Location) || hit != ResizeEdge.None))
            {
                _dragMode = hit == ResizeEdge.None ? SelectionDragMode.MoveSelection : SelectionDragMode.ResizeSelection;
                _resizeEdge = hit; _start = args.Location; _dragStartSelection = _sel; Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs args)
        {
            if (_selected && _editor.MouseMove(args)) return;
            switch (_dragMode)
            {
                case SelectionDragMode.NewSelection:
                    var point = new Point(Math.Clamp(args.X, 0, Width), Math.Clamp(args.Y, 0, Height));
                    _sel = Rectangle.FromLTRB(Math.Min(_start.X, point.X), Math.Min(_start.Y, point.Y), Math.Max(_start.X, point.X), Math.Max(_start.Y, point.Y));
                    break;
                case SelectionDragMode.MoveSelection:
                    _sel = new Rectangle(Math.Clamp(_dragStartSelection.X + args.X - _start.X, 0, Width - _sel.Width), Math.Clamp(_dragStartSelection.Y + args.Y - _start.Y, 0, Height - _sel.Height), _sel.Width, _sel.Height);
                    break;
                case SelectionDragMode.ResizeSelection:
                    _sel = ResizeSelection(_dragStartSelection, _resizeEdge, args.Location);
                    break;
                default:
                    if (_selected) Cursor = _editor.Tool == AnnotationTool.None ? CursorForResize(HitTestResize(args.Location), _sel.Contains(args.Location)) : _editor.DrawingCursor;
                    return;
            }
            if (_selected) { SelectedScreenRect = ToScreenRect(_sel); _editor.BoundsChanged(); PositionToolbar(); }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs args)
        {
            if (_selected && _editor.MouseUp(args)) return;
            var previous = _dragMode; _dragMode = SelectionDragMode.None; _resizeEdge = ResizeEdge.None; Capture = false;
            if (previous == SelectionDragMode.NewSelection && _sel.Width > 4 && _sel.Height > 4)
            {
                _selected = true; SelectedScreenRect = ToScreenRect(_sel);
                _editor.Toolbar.Visible = true; PositionToolbar(); Cursor = Cursors.SizeAll;
            }
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs args) { if (_selected) _editor.DoubleClick(args.Location); }
        protected override void OnMouseCaptureChanged(EventArgs args)
        {
            if (!Capture && MouseButtons == MouseButtons.None) _editor.CancelStroke();
            base.OnMouseCaptureChanged(args);
        }

        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (_selected && _editor.HandleKey(args)) return;
            if (args.KeyCode == Keys.Escape) { Confirm(RegionAction.Cancel); return; }
            if (!_selected) return;
            if (args.Control && args.Shift && args.KeyCode == Keys.S) Confirm(RegionAction.SaveAs);
            else if (args.KeyCode is Keys.Enter or Keys.C) Confirm(RegionAction.Copy);
            else if (args.KeyCode == Keys.S) Confirm(RegionAction.Save);
            else if (args.KeyCode == Keys.P) Confirm(RegionAction.Pin);
            else return;
            args.SuppressKeyPress = true;
        }

        private void ShowSaveMenu()
        {
            _saveMenu?.Dispose();
            _saveMenu = new ContextMenuStrip();
            _saveMenu.Items.Add(new ToolStripLabel("Save to") { ForeColor = SystemColors.GrayText });
            _saveMenu.Items.Add(new ToolStripLabel(_saveFolder) { ToolTipText = _saveFolder });
            _saveMenu.Items.Add(new ToolStripSeparator());
            var saveAs = new ToolStripMenuItem("Save as...", FluentIcon.Get(FluentIcon.Save, 16, Color.DimGray)) { ShortcutKeyDisplayString = "Ctrl+Shift+S" };
            saveAs.Click += (_, _) => Confirm(RegionAction.SaveAs); _saveMenu.Items.Add(saveAs);
            var folder = new ToolStripMenuItem("Set save folder...", FluentIcon.Get(FluentIcon.Folder, 16, Color.DimGray));
            folder.Click += (_, _) => SetSaveFolder(); _saveMenu.Items.Add(folder);
            var anchor = _editor.Toolbar.ActionBounds("options");
            _saveMenu.Show(new Point(anchor.Right - _saveMenu.PreferredSize.Width, anchor.Bottom + 4));
        }

        private void SetSaveFolder()
        {
            _saveMenu?.Close();
            using var dialog = new FolderBrowserDialog { Description = "Choose the default screenshot folder", UseDescriptionForTitle = true, SelectedPath = _saveFolder, ShowNewFolderButton = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _saveFolder = dialog.SelectedPath; _settings.SaveFolder = _saveFolder; _settings.Save(); _saveFolderChanged?.Invoke(_saveFolder);
            _editor.Toolbar.SetSaveFolder(_saveFolder);
        }

        public Bitmap RenderSelectedImage() => _editor.Export();
        private Rectangle ToScreenRect(Rectangle rectangle) => new(_virt.X + rectangle.X, _virt.Y + rectangle.Y, rectangle.Width, rectangle.Height);
        private void Confirm(RegionAction action)
        {
            _editor.CommitText(); Action = action; SelectedScreenRect = ToScreenRect(_sel);
            DialogResult = action == RegionAction.Cancel ? DialogResult.Cancel : DialogResult.OK; Close();
        }

        private Rectangle ResizeSelection(Rectangle origin, ResizeEdge edge, Point point)
        {
            int left = origin.Left, top = origin.Top, right = origin.Right, bottom = origin.Bottom;
            if (edge.HasFlag(ResizeEdge.Left)) left = Math.Clamp(point.X, 0, right - 4);
            if (edge.HasFlag(ResizeEdge.Right)) right = Math.Clamp(point.X, left + 4, Width);
            if (edge.HasFlag(ResizeEdge.Top)) top = Math.Clamp(point.Y, 0, bottom - 4);
            if (edge.HasFlag(ResizeEdge.Bottom)) bottom = Math.Clamp(point.Y, top + 4, Height);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private ResizeEdge HitTestResize(Point point)
        {
            var inflated = _sel; inflated.Inflate(8, 8);
            if (!inflated.Contains(point)) return ResizeEdge.None;
            ResizeEdge edge = ResizeEdge.None;
            if (Math.Abs(point.X - _sel.Left) <= 8) edge |= ResizeEdge.Left;
            if (Math.Abs(point.X - _sel.Right) <= 8) edge |= ResizeEdge.Right;
            if (Math.Abs(point.Y - _sel.Top) <= 8) edge |= ResizeEdge.Top;
            if (Math.Abs(point.Y - _sel.Bottom) <= 8) edge |= ResizeEdge.Bottom;
            return edge;
        }

        private static Cursor CursorForResize(ResizeEdge edge, bool inside) => edge switch
        {
            ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
            ResizeEdge.Left | ResizeEdge.Top or ResizeEdge.Right | ResizeEdge.Bottom => Cursors.SizeNWSE,
            ResizeEdge.Right | ResizeEdge.Top or ResizeEdge.Left | ResizeEdge.Bottom => Cursors.SizeNESW,
            _ => inside ? Cursors.SizeAll : Cursors.Default
        };

        private IEnumerable<Point> HandleCenters()
        {
            int centerX = _sel.Left + _sel.Width / 2, centerY = _sel.Top + _sel.Height / 2;
            return new[] { new Point(_sel.Left, _sel.Top), new Point(centerX, _sel.Top), new Point(_sel.Right, _sel.Top), new Point(_sel.Right, centerY), new Point(_sel.Right, _sel.Bottom), new Point(centerX, _sel.Bottom), new Point(_sel.Left, _sel.Bottom), new Point(_sel.Left, centerY) };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _saveMenu?.Dispose(); _editor.Dispose(); }
            base.Dispose(disposing);
        }
    }
}