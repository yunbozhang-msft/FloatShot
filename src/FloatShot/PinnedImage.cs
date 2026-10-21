using System.Drawing.Imaging;
using System.Windows.Forms;

namespace FloatShot;

internal sealed class PinnedImage : Form
{
    private static readonly List<PinnedImage> _all = new();
    private const int ShadowPad = 4;
    private readonly Bitmap _original;
    private readonly Settings _settings;
    private readonly AnnotationEditor _editor;
    private readonly Form _toolbarWindow;
    private float _zoom = 1;
    private bool _dragging;
    private Point _dragStartScreen;
    private Point _formStart;

    public static void ShowAt(Bitmap bitmap, Point screenLocation, Settings? settings = null)
    {
        var image = new PinnedImage(bitmap, settings) { Location = new Point(screenLocation.X - ShadowPad, screenLocation.Y - ShadowPad) };
        _all.Add(image); image.FormClosed += (_, _) => _all.Remove(image); image.Show();
    }

    private PinnedImage(Bitmap bitmap, Settings? settings = null)
    {
        _original = bitmap; _settings = settings ?? Settings.Load();
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false; TopMost = true; DoubleBuffered = true; KeyPreview = true;
        Cursor = Cursors.SizeAll; BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
        Icon = AppIcon.Get(); Text = "FloatShot Pin";
        Size = new Size(bitmap.Width + ShadowPad * 2, bitmap.Height + ShadowPad * 2);
        _editor = new AnnotationEditor(this, bitmap, () => ImageBounds, () => new Rectangle(Point.Empty, bitmap.Size), _settings);
        _toolbarWindow = new ToolbarWindow { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, TopMost = true, KeyPreview = true, Text = "FloatShot Annotation" };
        _toolbarWindow.Controls.Add(_editor.Toolbar);
        _editor.Toolbar.RegionChanged += (_, _) => UpdateToolbarRegion();
        UpdateToolbarRegion();
        _toolbarWindow.KeyDown += (_, args) => OnKeyDown(args);
        _editor.Changed += PositionToolbar;
        _editor.ActionRequested += action =>
        {
            switch (action)
            {
                case "copy": CopyToClipboard(); break;
                case "save": Save(false); break;
                case "options": ShowSaveMenu(); break;
                case "close": Close(); break;
                case "pin": ToggleToolbar(); break;
            }
        };
        _editor.Toolbar.SetSaveFolder(_settings.SaveFolder);
        ContextMenuStrip = BuildMenu();
        LocationChanged += (_, _) => PositionToolbar();
        SetStyle(ControlStyles.Selectable, true);
    }

    private Rectangle ImageBounds => new(ShadowPad, ShadowPad, Math.Max(1, (int)(_original.Width * _zoom)), Math.Max(1, (int)(_original.Height * _zoom)));
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= 0x00000008 | 0x00000080; return parameters; }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 10), ImageScalingSize = new Size(18, 18) };
        AddItem(menu, "text", "Annotate", "Space", ToggleToolbar);
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "copy", "Copy", "Ctrl+C", CopyToClipboard);
        AddItem(menu, "save", "Save", "Ctrl+S", () => Save(false));
        AddItem(menu, "folder", "Save as...", "Ctrl+Shift+S", () => Save(true));
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "undo", "Undo", "Ctrl+Z", _editor.Undo);
        AddItem(menu, "redo", "Redo", "Ctrl+Y", _editor.Redo);
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "arrow_reset", "Reset zoom", "", () => SetZoom(1));
        AddItem(menu, "zoom_in", "Zoom in", "+", () => SetZoom(_zoom + .1f));
        AddItem(menu, "zoom_out", "Zoom out", "-", () => SetZoom(_zoom - .1f));
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "dismiss", "Close", "Esc", Close);
        return menu;
    }

    private static void AddItem(ContextMenuStrip menu, string icon, string label, string shortcut, Action action)
    {
        var item = new ToolStripMenuItem(label, FluentIcon.Get(icon, 18, Color.FromArgb(50, 50, 50))) { ShortcutKeyDisplayString = shortcut };
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        using var background = new SolidBrush(TransparencyKey); args.Graphics.FillRectangle(background, ClientRectangle);
        for (int index = 0; index < 3; index++)
        {
            using var shadow = new SolidBrush(Color.FromArgb(18 - index * 5, 0, 0, 0));
            var bounds = ImageBounds; bounds.Inflate(ShadowPad - index, ShadowPad - index); bounds.Offset(0, 1);
            args.Graphics.FillRectangle(shadow, bounds);
        }
        _editor.Draw(args.Graphics);
        using var border = new Pen(Color.FromArgb(140, 100, 100, 100)); args.Graphics.DrawRectangle(border, ImageBounds);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        Focus();
        if (_editor.MouseDown(args)) return;
        if (args.Button == MouseButtons.Left)
        {
            _dragging = true; _dragStartScreen = Cursor.Position; _formStart = Location; Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        if (_editor.MouseMove(args)) return;
        if (_dragging && (args.Button & MouseButtons.Left) != 0)
        {
            var current = Cursor.Position;
            Location = new Point(_formStart.X + current.X - _dragStartScreen.X, _formStart.Y + current.Y - _dragStartScreen.Y);
        }
        Cursor = _editor.Tool == AnnotationTool.None ? Cursors.SizeAll : _editor.Tool == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross;
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        if (_editor.MouseUp(args)) return;
        _dragging = false; Capture = false;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return;
        if (_editor.DoubleClick(args.Location)) { ShowToolbar(); return; }
        if (_editor.Tool == AnnotationTool.None && !_editor.EditingText) Close();
    }

    protected override void OnMouseWheel(MouseEventArgs args) => SetZoom(_zoom + (args.Delta > 0 ? .1f : -.1f));
    private void SetZoom(float zoom)
    {
        _editor.CommitText(); _zoom = Math.Clamp(zoom, .5f, 4);
        Size = new Size(ImageBounds.Width + ShadowPad * 2, ImageBounds.Height + ShadowPad * 2);
        _editor.BoundsChanged(); PositionToolbar(); Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        if (_editor.HandleKey(args)) { if (_editor.Tool != AnnotationTool.None && !_toolbarWindow.Visible) ShowToolbar(); return; }
        if (args.KeyCode == Keys.Escape) Close();
        else if (args.KeyCode == Keys.Space) ToggleToolbar();
        else if (args.Control && args.KeyCode == Keys.C) CopyToClipboard();
        else if (args.Control && args.KeyCode == Keys.S) Save(args.Shift);
        else if (args.KeyCode is Keys.Add or Keys.Oemplus) SetZoom(_zoom + .1f);
        else if (args.KeyCode is Keys.Subtract or Keys.OemMinus) SetZoom(_zoom - .1f);
        else return;
        args.SuppressKeyPress = true;
    }

    protected override void OnShown(EventArgs args) { base.OnShown(args); Activate(); Focus(); }
    private void ToggleToolbar()
    {
        _editor.CommitText();
        if (_toolbarWindow.Visible)
        {
            if (_editor.Tool != AnnotationTool.None) _editor.ToggleTool(_editor.Tool);
            _toolbarWindow.Hide();
        }
        else ShowToolbar();
    }

    private void ShowToolbar() { PositionToolbar(); if (!_toolbarWindow.Visible) _toolbarWindow.Show(this); }
    private void UpdateToolbarRegion()
    {
        var previous = _toolbarWindow.Region;
        _toolbarWindow.Region = _editor.Toolbar.Region?.Clone();
        previous?.Dispose();
    }
    private void PositionToolbar()
    {
        var monitor = Screen.FromControl(this).WorkingArea;
        _toolbarWindow.Bounds = _editor.Toolbar.Arrange(monitor, Right, Bottom + 8, Top - 8);
    }

    private void CopyToClipboard()
    {
        try { using var image = _editor.Export(); Clipboard.SetImage(image); }
        catch (Exception exception) { MessageBox.Show(this, "Copy failed: " + exception.Message, "FloatShot", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Save(bool saveAs)
    {
        _editor.CommitText();
        try
        {
            using var image = _editor.Export();
            if (!saveAs) FloatShot.Capture.SaveAndPostProcess(image, _settings, false, false);
            else
            {
                var folder = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                using var dialog = new SaveFileDialog { Title = "Save screenshot as", Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp", InitialDirectory = folder, FileName = FloatShot.Capture.GetAvailableFileName(folder, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png"), OverwritePrompt = true, RestoreDirectory = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch { ".jpg" or ".jpeg" => ImageFormat.Jpeg, ".bmp" => ImageFormat.Bmp, _ => ImageFormat.Png };
                image.Save(dialog.FileName, format); _settings.SaveFolder = Path.GetDirectoryName(dialog.FileName)!;
            }
            _settings.Save(); _editor.Toolbar.SetSaveFolder(_settings.SaveFolder);
        }
        catch (Exception exception) { MessageBox.Show(this, "Save failed: " + exception.Message, "FloatShot", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowSaveMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("Save to") { ForeColor = SystemColors.GrayText });
        menu.Items.Add(new ToolStripLabel(_settings.SaveFolder)); menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "save", "Save as...", "Ctrl+Shift+S", () => Save(true));
        AddItem(menu, "folder", "Set save folder...", "", () =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Choose the default screenshot folder", UseDescriptionForTitle = true, SelectedPath = _settings.SaveFolder };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _settings.SaveFolder = dialog.SelectedPath; _settings.Save(); _editor.Toolbar.SetSaveFolder(_settings.SaveFolder);
        });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        var anchor = _editor.Toolbar.ActionBounds("options"); menu.Show(new Point(anchor.Right - menu.PreferredSize.Width, anchor.Bottom + 4));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _editor.Dispose(); _toolbarWindow.Dispose(); ContextMenuStrip?.Dispose(); _original.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed class ToolbarWindow : Form
    {
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x00000008 | 0x00000080; return parameters; }
        }
    }
}