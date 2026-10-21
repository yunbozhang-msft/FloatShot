using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FloatShot;

internal sealed class AnnotationToolbar : UserControl
{
    private static readonly Color Surface = Color.FromArgb(255, 255, 255);
    private static readonly Color Ink = Color.FromArgb(36, 36, 36);
    private static readonly Color Muted = Color.FromArgb(92, 92, 92);
    private static readonly Color Accent = Color.FromArgb(177, 31, 75);
    private static readonly Color AccentSoft = Color.FromArgb(251, 236, 241);
    private static readonly Color Hairline = Color.FromArgb(229, 229, 229);
    private readonly FlowLayoutPanel _tools = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
    private readonly FlowLayoutPanel _options = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(5, 5, 5, 5) };
    private readonly ToolTip _tips = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 10000 };
    private readonly Dictionary<AnnotationTool, ToolbarButton> _toolButtons = new();
    private readonly Dictionary<string, ToolbarButton> _actions = new();
    private AnnotationTool _tool;
    private AnnotationPreferences _preferences = new();

    public event Action<AnnotationTool>? ToolSelected;
    public event Action? PreferenceChanged;
    public event Action<string>? ActionRequested;

    public AnnotationToolbar()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Surface;
        Padding = new Padding(6);
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Controls.Add(_tools); Controls.Add(_options);
        foreach (var (tool, icon, label) in new[]
        {
            (AnnotationTool.Rectangle, "rectangle", "Rectangle (R)"), (AnnotationTool.Arrow, "arrow", "Arrow (A)"),
            (AnnotationTool.Pen, "pen", "Pen (B)"), (AnnotationTool.Highlight, "highlight", "Highlighter (H)"),
            (AnnotationTool.Mosaic, "mosaic", "Mosaic (M)"), (AnnotationTool.Text, "text", "Text (T)")
        })
        {
            var button = IconButton(icon, label);
            button.Click += (_, _) => ToolSelected?.Invoke(tool);
            _toolButtons.Add(tool, button); _tools.Controls.Add(button);
        }
        Separator(); AddAction("undo", "undo", "Undo (Ctrl+Z)"); AddAction("redo", "redo", "Redo (Ctrl+Y)"); Separator();
        AddAction("pin", "pin", "Pin (P)"); AddAction("copy", "copy", "Copy (Enter / C / Ctrl+C)");
        AddAction("save", "save", "Save (S / Ctrl+S)"); AddAction("options", "chevron", "Save options");
        _actions["options"].Width = 20;
        _actions["options"].IconSize = 12;
        var saveGroup = new SegmentedPanel { Size = new Size(53, 34), Padding = new Padding(2), Margin = new Padding(3, 1, 3, 1), WrapContents = false, BackColor = Surface };
        _actions["save"].Size = new Size(31, 30); _actions["save"].Margin = Padding.Empty;
        _actions["options"].Size = new Size(18, 30); _actions["options"].Margin = Padding.Empty; _actions["options"].SplitRight = true;
        saveGroup.Controls.Add(_actions["save"]); saveGroup.Controls.Add(_actions["options"]); _tools.Controls.Add(saveGroup);
        _actions["copy"].IconColor = Fluent.Accent;
        AddAction("close", "dismiss", "Close (Esc)");
        UpdateState(AnnotationTool.None, _preferences);
    }

    private ToolbarButton IconButton(string icon, string label)
    {
        var button = new ToolbarButton { Size = new Size(34, 34), Margin = new Padding(1), Cursor = Cursors.Hand, AccessibleName = label.Split('(')[0].Trim(), IconName = icon };
        _tips.SetToolTip(button, label);
        return button;
    }

    private void AddAction(string action, string icon, string label)
    {
        var button = IconButton(icon, label);
        button.Click += (_, _) => ActionRequested?.Invoke(action);
        _actions.Add(action, button); _tools.Controls.Add(button);
        if (action is "save" or "options") button.MouseUp += (_, args) => { if (args.Button == MouseButtons.Right) ActionRequested?.Invoke("options"); };
    }

    private void Separator() => _tools.Controls.Add(new Panel { Width = 1, Height = 21, BackColor = Hairline, Margin = new Padding(6, 7, 6, 0) });

    public Rectangle ActionBounds(string action) => _actions[action].RectangleToScreen(_actions[action].ClientRectangle);
    public void SetSaveFolder(string folder) => _tips.SetToolTip(_actions["save"], $"Save to {folder}\nS / Ctrl+S");
    public void SetHistory(bool undo, bool redo) { _actions["undo"].Enabled = undo; _actions["redo"].Enabled = redo; }

    public void UpdateState(AnnotationTool tool, AnnotationPreferences preferences)
    {
        _tool = tool; _preferences = preferences;
        foreach (var entry in _toolButtons)
        {
            entry.Value.Active = entry.Key == tool;
            entry.Value.IconColor = entry.Key == AnnotationTool.Highlight ? Color.FromArgb(245, 158, 11) : Ink;
            entry.Value.Invalidate();
        }
        SuspendLayout();
        foreach (Control control in _options.Controls.Cast<Control>().ToArray()) { _options.Controls.Remove(control); control.Dispose(); }
        _options.Visible = tool != AnnotationTool.None;
        if (tool != AnnotationTool.None)
        {
            var style = preferences.For(tool);
            if (tool != AnnotationTool.Mosaic) AddColors(style);
            if (tool == AnnotationTool.Rectangle)
            {
                AddChoices(("Outline", !style.Filled, () => style.Filled = false), ("Solid", style.Filled, () => style.Filled = true));
            }
            if (tool == AnnotationTool.Mosaic)
            {
                AddChoices(("Brush", style.Brush, () => style.Brush = true), ("Rectangle", !style.Brush, () => style.Brush = false));
                AddSlider(8, 40, style.Strength, "Mosaic strength", value => style.Strength = value);
            }
            else if (tool == AnnotationTool.Text)
            {
                var sizes = new ToolbarButton { Text = style.FontSize.ToString(), Size = new Size(55, 27), Margin = new Padding(10, 1, 3, 0), AccessibleName = "Font size", DropDown = true };
                sizes.Click += (_, _) =>
                {
                    var menu = new ContextMenuStrip { Font = Font, ShowImageMargin = false };
                    foreach (int value in new[] { 12, 14, 18, 22, 24, 32, 40, 48, 60, 72 }.Append(style.FontSize).Distinct().Order())
                    {
                        int size = value;
                        var item = new ToolStripMenuItem(size.ToString()) { Checked = size == style.FontSize };
                        item.Click += (_, _) => { style.FontSize = size; sizes.Text = size.ToString(); PreferenceChanged?.Invoke(); };
                        menu.Items.Add(item);
                    }
                    menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
                    menu.Show(sizes, new Point(0, sizes.Height + 3));
                };
                _options.Controls.Add(sizes);
                var bold = IconButton("bold", "Bold"); bold.Size = new Size(28, 28); bold.Active = style.Bold; bold.Indicator = false;
                bold.Click += (_, _) => { style.Bold = !style.Bold; PreferenceChanged?.Invoke(); UpdateState(_tool, _preferences); };
                _options.Controls.Add(bold);
            }
            else if (tool != AnnotationTool.Rectangle || !style.Filled)
            {
                _options.Controls.Add(new Label { Text = "Width", AutoSize = true, ForeColor = Muted, Margin = new Padding(12, 6, 5, 0) });
                AddSlider(2, tool == AnnotationTool.Highlight ? 48 : 16, style.Width, "Width", value => style.Width = value);
            }
        }
        int padding = Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96f));
        _tools.Location = new Point(padding, padding);
        _tools.Size = _tools.PreferredSize;
        _options.Location = new Point(padding, _tools.Bottom + 5);
        _options.Size = _options.PreferredSize;
        Size = new Size(Math.Max(_tools.PreferredSize.Width, _options.Visible ? _options.PreferredSize.Width : 0) + padding * 2, (_options.Visible ? _options.Bottom : _tools.Bottom) + padding);
        using (var outline = Rounded(new RectangleF(0, 0, Width, Height), 7))
        {
            var previous = Region; Region = new Region(outline); previous?.Dispose();
        }
        ResumeLayout(); Invalidate();
    }

    private void AddChoices(params (string Label, bool Selected, Action Change)[] choices)
    {
        var group = new SegmentedPanel { AutoSize = true, WrapContents = false, Padding = new Padding(2), Margin = new Padding(9, 0, 3, 0), BackColor = Color.FromArgb(245, 245, 245) };
        foreach (var choice in choices)
        {
            var button = new ToolbarButton { Text = choice.Label, Size = new Size(choice.Label.Length > 7 ? 73 : 59, 24), Margin = Padding.Empty, Active = choice.Selected, Segment = true, Indicator = false, AccessibleName = choice.Label };
            button.Click += (_, _) => { choice.Change(); PreferenceChanged?.Invoke(); UpdateState(_tool, _preferences); };
            group.Controls.Add(button);
        }
        _options.Controls.Add(group);
    }

    private void AddSlider(int minimum, int maximum, int value, string label, Action<int> change)
    {
        var slider = new ToolbarSlider { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(value, minimum, maximum), Size = new Size(label == "Mosaic strength" ? 110 : 72, 27), Margin = new Padding(3, 0, 2, 0), AccessibleName = label, Step = label == "Mosaic strength" ? 4 : 1 };
        _tips.SetToolTip(slider, label);
        Label? output = label == "Mosaic strength" ? null : new Label { Text = slider.Value + " px", AutoSize = false, Size = new Size(37, 26), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted, Margin = new Padding(3, 0, 2, 0) };
        slider.ValueChanged += (_, _) => { change(slider.Value); if (output is not null) output.Text = slider.Value + " px"; PreferenceChanged?.Invoke(); };
        _options.Controls.Add(slider);
        if (output is not null) _options.Controls.Add(output);
    }

    private void AddColors(AnnotationStyle style)
    {
        foreach (var color in new[] { Color.FromArgb(230, 45, 45), Color.Yellow, Color.FromArgb(22, 163, 74), Fluent.Accent, Color.Black, Color.White })
        {
            var swatch = new ToolbarButton { Size = new Size(23, 27), Margin = new Padding(1, 0, 1, 0), AccessibleName = color.Name, Swatch = color, IsSwatchSelected = () => color.ToArgb() == style.ColorArgb };
            swatch.Click += (_, _) => { style.ColorArgb = color.ToArgb(); PreferenceChanged?.Invoke(); _options.Invalidate(true); };
            _tips.SetToolTip(swatch, color == Color.Yellow ? "Yellow" : color == Color.White ? "White" : color == Color.Black ? "Black" : color == Fluent.Accent ? "Blue" : color.G > color.R ? "Green" : "Red");
            _options.Controls.Add(swatch);
        }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(220, 220, 220));
        using var shape = Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 7);
        args.Graphics.DrawPath(border, shape);
        if (_options.Visible) args.Graphics.DrawLine(border, 1, _options.Top - 3, Width - 2, _options.Top - 3);
    }

    protected override void Dispose(bool disposing) { if (disposing) _tips.Dispose(); base.Dispose(disposing); }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath(); float diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }

    private sealed class SegmentedPanel : FlowLayoutPanel
    {
        public SegmentedPanel() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        protected override void OnSizeChanged(EventArgs args)
        {
            base.OnSizeChanged(args);
            if (Width < 10 || Height < 10) return;
            using var outline = Rounded(new RectangleF(0, 0, Width, Height), 5);
            var previous = Region; Region = new Region(outline); previous?.Dispose();
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var border = new Pen(Hairline); using var shape = Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5);
            args.Graphics.DrawPath(border, shape);
        }
    }

    private sealed class ToolbarButton : Button
    {
        private bool _hovered;
        private bool _pressed;
        public string? IconName { get; set; }
        public Color IconColor { get; set; } = Ink;
        public int IconSize { get; set; } = 20;
        public bool Active { get; set; }
        public bool Indicator { get; set; } = true;
        public bool Segment { get; set; }
        public bool DropDown { get; set; }
        public bool SplitRight { get; set; }
        public Color? Swatch { get; set; }
        public Func<bool>? IsSwatchSelected { get; set; }

        public ToolbarButton()
        {
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = Surface; ForeColor = Ink; Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs args) { _hovered = true; Invalidate(); base.OnMouseEnter(args); }
        protected override void OnMouseLeave(EventArgs args) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(args); }
        protected override void OnMouseDown(MouseEventArgs args) { _pressed = args.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(args); }
        protected override void OnMouseUp(MouseEventArgs args) { _pressed = false; Invalidate(); base.OnMouseUp(args); }

        protected override void OnPaint(PaintEventArgs args)
        {
            var graphics = args.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Segment ? Parent?.BackColor ?? Surface : Surface);
            if (Swatch is { } color)
            {
                float centerX = Width / 2f, centerY = Height / 2f;
                using var fill = new SolidBrush(color); using var white = new Pen(Surface, 2);
                using var edge = new Pen(IsSwatchSelected?.Invoke() == true ? Ink : Color.FromArgb(200, 200, 200), IsSwatchSelected?.Invoke() == true ? 1.5f : 1);
                graphics.FillEllipse(fill, centerX - 7, centerY - 7, 14, 14);
                graphics.DrawEllipse(white, centerX - 8, centerY - 8, 16, 16);
                graphics.DrawEllipse(edge, centerX - 9, centerY - 9, 18, 18); return;
            }
            var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
            using var background = new SolidBrush(Segment && Active ? Surface : Active ? AccentSoft : _pressed ? Color.FromArgb(232, 232, 232) : _hovered ? Color.FromArgb(245, 245, 245) : Segment ? Parent?.BackColor ?? Surface : Surface);
            using var shape = Rounded(bounds, Segment ? 3 : 4);
            graphics.FillPath(background, shape);
            if (Segment && Active || DropDown)
            {
                using var border = new Pen(Hairline); graphics.DrawPath(border, shape);
            }
            if (SplitRight) { using var divider = new Pen(Hairline); graphics.DrawLine(divider, 0, 3, 0, Height - 3); }
            Color foreground = !Enabled ? Color.FromArgb(185, 185, 185) : Active ? Accent : IconName is null ? Ink : IconColor;
            if (IconName is not null) FluentIcon.Draw(graphics, IconName, ClientRectangle, foreground, IconSize);
            else
            {
                var textBounds = ClientRectangle;
                if (DropDown) { textBounds.Width -= 16; FluentIcon.Draw(graphics, "chevron", new Rectangle(Width - 20, 0, 16, Height), Muted, 12); }
                TextRenderer.DrawText(graphics, Text, Font, textBounds, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (Active && Indicator && !Segment)
            {
                using var marker = new Pen(Accent, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawLine(marker, Width / 2f - 5, Height - 3, Width / 2f + 5, Height - 3);
            }
        }
    }

    private sealed class ToolbarSlider : Control
    {
        private int _value;
        public int Minimum { get; set; }
        public int Maximum { get; set; } = 100;
        public int Step { get; set; } = 1;
        public int Value { get => _value; set { int next = Math.Clamp(value, Minimum, Maximum); if (_value == next) return; _value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler? ValueChanged;

        public ToolbarSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Surface; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider; TabStop = true;
        }

        private void SetFromMouse(int position) => Value = Minimum + (int)Math.Round(Math.Clamp((position - 7f) / Math.Max(1, Width - 14), 0, 1) * (Maximum - Minimum) / Step) * Step;
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
        protected override void OnMouseDown(MouseEventArgs args) { if (args.Button == MouseButtons.Left) { Capture = true; Focus(); SetFromMouse(args.X); } base.OnMouseDown(args); }
        protected override void OnMouseMove(MouseEventArgs args) { if (Capture && args.Button == MouseButtons.Left) SetFromMouse(args.X); base.OnMouseMove(args); }
        protected override void OnMouseUp(MouseEventArgs args) { if (Capture) { SetFromMouse(args.X); Capture = false; } base.OnMouseUp(args); }
        protected override void OnKeyDown(KeyEventArgs args)
        {
            if (args.KeyCode is Keys.Left or Keys.Down) Value -= Step;
            else if (args.KeyCode is Keys.Right or Keys.Up) Value += Step;
            else if (args.KeyCode == Keys.Home) Value = Minimum;
            else if (args.KeyCode == Keys.End) Value = Maximum;
            else { base.OnKeyDown(args); return; }
            args.SuppressKeyPress = true;
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var graphics = args.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float centerY = Height / 2f, thumb = 7 + (Width - 14) * (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
            using var track = new Pen(Color.FromArgb(228, 228, 228), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var fill = new Pen(Accent, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(track, 7, centerY, Width - 7, centerY); graphics.DrawLine(fill, 7, centerY, thumb, centerY);
            using var halo = new SolidBrush(Color.FromArgb(238, 238, 238)); using var thumbBrush = new SolidBrush(Accent);
            graphics.FillEllipse(halo, thumb - 7, centerY - 6, 14, 14);
            graphics.FillEllipse(Brushes.White, thumb - 6, centerY - 6, 12, 12);
            graphics.FillEllipse(thumbBrush, thumb - 4, centerY - 4, 8, 8);
        }
    }
}

internal sealed class AnnotationEditor : IDisposable
{
    private readonly Control _host;
    private readonly Func<Rectangle> _displayBounds;
    private readonly Func<Rectangle> _sourceBounds;
    private readonly Settings _settings;
    private Annotation? _preview;
    private TextBox? _textBox;
    private Font? _textFont;
    private PointF _textAnchor;
    private int _editingIndex = -1;
    private int _movingIndex = -1;
    private PointF _moveStart;
    private Annotation? _moveOriginal;

    public AnnotationDocument Document { get; }
    public AnnotationToolbar Toolbar { get; } = new();
    public AnnotationTool Tool { get; private set; }
    public bool EditingText => _textBox is not null;
    public bool Drawing => _preview is not null;
    public event Action? Changed;
    public event Action<string>? ActionRequested;

    public AnnotationEditor(Control host, Bitmap source, Func<Rectangle> displayBounds, Func<Rectangle> sourceBounds, Settings settings)
    {
        _host = host; _displayBounds = displayBounds; _sourceBounds = sourceBounds; _settings = settings;
        _settings.Annotation ??= new(); _settings.Annotation.Normalize();
        Document = new AnnotationDocument(source);
        Toolbar.ToolSelected += ToggleTool;
        Toolbar.PreferenceChanged += () => { _settings.Save(); UpdateTextBox(); Notify(); };
        Toolbar.ActionRequested += action =>
        {
            CommitText();
            if (action == "undo") Document.Undo();
            else if (action == "redo") Document.Redo();
            else ActionRequested?.Invoke(action);
            RefreshHistory(); Notify();
        };
        Toolbar.UpdateState(Tool, _settings.Annotation); RefreshHistory();
    }

    private float Zoom => _displayBounds().Width / (float)Math.Max(1, _sourceBounds().Width);
    private PointF ToImage(Point point)
    {
        var display = _displayBounds(); var source = _sourceBounds();
        return new PointF(Math.Clamp(source.X + (point.X - display.X) / Zoom, source.Left, source.Right - 1), Math.Clamp(source.Y + (point.Y - display.Y) / Zoom, source.Top, source.Bottom - 1));
    }

    public void ToggleTool(AnnotationTool tool)
    {
        CommitText(); CancelStroke(); Tool = Tool == tool ? AnnotationTool.None : tool;
        Toolbar.UpdateState(Tool, _settings.Annotation); Notify();
    }

    public bool MouseDown(MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left) return false;
        bool wasEditing = EditingText;
        CommitText();
        if (!_displayBounds().Contains(args.Location)) return false;
        var point = ToImage(args.Location);
        if (Tool == AnnotationTool.Text)
        {
            int hit = Document.HitText(point);
            if (!wasEditing || hit >= 0) BeginText(point, hit);
            return true;
        }
        if (Tool == AnnotationTool.None)
        {
            int hit = Document.HitText(point);
            if (hit < 0) return false;
            _movingIndex = hit; _moveStart = point; _moveOriginal = Document[hit]; _preview = _moveOriginal;
        }
        else _preview = new Annotation(Tool, _settings.Annotation.For(Tool).Copy(), new List<PointF> { point });
        _host.Capture = true; Notify(); return true;
    }

    public bool MouseMove(MouseEventArgs args)
    {
        if (_preview is null) return false;
        var point = ToImage(args.Location);
        if (_movingIndex >= 0 && _moveOriginal is not null)
        {
            var anchor = _moveOriginal.Points[0]; var bounds = _sourceBounds();
            var moved = new PointF(Math.Clamp(anchor.X + point.X - _moveStart.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - 32)), Math.Clamp(anchor.Y + point.Y - _moveStart.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - _preview.Style.FontSize * 1.5f)));
            _preview = _moveOriginal with { Points = new List<PointF> { moved } };
        }
        else
        {
            bool straight = (Control.ModifierKeys & Keys.Shift) != 0;
            if (straight)
            {
                var start = _preview.Points[0];
                point = Math.Abs(point.X - start.X) > Math.Abs(point.Y - start.Y) ? new PointF(point.X, start.Y) : new PointF(start.X, point.Y);
            }
            if (_preview.Tool is AnnotationTool.Rectangle or AnnotationTool.Arrow || _preview.Tool == AnnotationTool.Mosaic && !_preview.Style.Brush || straight)
                _preview = _preview with { Points = new List<PointF> { _preview.Points[0], point } };
            else if (_preview.Points[^1] != point) _preview.Points.Add(point);
        }
        Notify(); return true;
    }

    public bool MouseUp(MouseEventArgs args)
    {
        if (_preview is null) return false;
        MouseMove(args);
        var item = _preview; _preview = null;
        if (_movingIndex >= 0)
        {
            if (_moveOriginal!.Points[0] != item.Points[0]) Document.Replace(_movingIndex, item);
            _movingIndex = -1; _moveOriginal = null;
        }
        else if (item.Tool is AnnotationTool.Rectangle || item.Tool == AnnotationTool.Mosaic && !item.Style.Brush)
        {
            if (item.Bounds.Width >= 2 && item.Bounds.Height >= 2) Document.Add(item);
        }
        else Document.Add(item);
        _host.Capture = false; RefreshHistory(); Notify(); return true;
    }

    public bool DoubleClick(Point location)
    {
        if (Tool != AnnotationTool.None || !_displayBounds().Contains(location)) return false;
        var point = ToImage(location); int hit = Document.HitText(point);
        if (hit < 0) return false;
        Tool = AnnotationTool.Text; BeginText(point, hit); Toolbar.UpdateState(Tool, _settings.Annotation); Notify(); return true;
    }

    private void BeginText(PointF anchor, int index)
    {
        _editingIndex = index; _textAnchor = anchor;
        if (index >= 0) { _textAnchor = Document[index].Points[0]; _settings.Annotation.Text = Document[index].Style.Copy(); }
        _textBox = new TextBox { Multiline = true, AcceptsReturn = true, BorderStyle = BorderStyle.FixedSingle, Text = index >= 0 ? Document[index].Text : "", AccessibleName = "Annotation text" };
        _textBox.TextChanged += (_, _) => UpdateTextBox();
        _host.Controls.Add(_textBox); UpdateTextBox(); Toolbar.UpdateState(Tool, _settings.Annotation);
        _textBox.BringToFront(); _textBox.Focus(); _textBox.Select(_textBox.TextLength, 0); Notify();
    }

    private void UpdateTextBox()
    {
        if (_textBox is null) return;
        var bounds = _sourceBounds(); var display = _displayBounds(); var style = _settings.Annotation.Text;
        if (_textFont is null || Math.Abs(_textFont.Size - style.FontSize * Zoom) > .01f || _textFont.Bold != style.Bold)
        {
            var oldFont = _textFont; _textFont = AnnotationDocument.TextFont(style, Zoom); _textBox.Font = _textFont; oldFont?.Dispose();
        }
        _textBox.ForeColor = style.Ink;
        int left = display.Left + (int)((_textAnchor.X - bounds.Left) * Zoom);
        int top = display.Top + (int)((_textAnchor.Y - bounds.Top) * Zoom);
        int width = Math.Max(1, display.Right - left);
        var size = TextRenderer.MeasureText(_textBox.Text + " ", _textBox.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        int height = Math.Min(display.Height, Math.Max(_textBox.Font.Height + 10, size.Height + 10));
        top = Math.Min(top, display.Bottom - height); _textAnchor.Y = bounds.Top + (top - display.Top) / Zoom;
        _textBox.Bounds = new Rectangle(left, top, width, height);
    }

    public void CommitText()
    {
        if (_textBox is null) return;
        var text = _textBox.Text.TrimEnd(); var index = _editingIndex;
        CloseTextBox();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var item = new Annotation(AnnotationTool.Text, _settings.Annotation.Text.Copy(), new List<PointF> { _textAnchor }, text);
            if (index >= 0) Document.Replace(index, item); else Document.Add(item);
        }
        else if (index >= 0) Document.Remove(index);
        RefreshHistory(); Notify();
    }

    private void CloseTextBox()
    {
        if (_textBox is null) return;
        var box = _textBox; _textBox = null; _editingIndex = -1;
        _host.Controls.Remove(box); box.Dispose(); _textFont?.Dispose(); _textFont = null;
        _host.Focus();
    }

    public bool HandleKey(KeyEventArgs args)
    {
        if (_textBox is not null && _textBox.ContainsFocus)
        {
            if (args.KeyCode == Keys.Escape) { CloseTextBox(); Notify(); args.SuppressKeyPress = true; }
            else if (args.Control && args.KeyCode == Keys.Enter) { CommitText(); args.SuppressKeyPress = true; }
            else if (args.Control && args.KeyCode == Keys.S) { CommitText(); return false; }
            return true;
        }
        if (args.Control && args.KeyCode is Keys.Z or Keys.Y)
        {
            if (args.KeyCode == Keys.Y || args.Shift) Document.Redo(); else Document.Undo();
            args.SuppressKeyPress = true; RefreshHistory(); Notify(); return true;
        }
        if (args.Control || args.Alt) return false;
        var tool = args.KeyCode switch { Keys.R => AnnotationTool.Rectangle, Keys.A => AnnotationTool.Arrow, Keys.B => AnnotationTool.Pen, Keys.H => AnnotationTool.Highlight, Keys.M => AnnotationTool.Mosaic, Keys.T => AnnotationTool.Text, _ => AnnotationTool.None };
        if (tool == AnnotationTool.None) return false;
        ToggleTool(tool); args.SuppressKeyPress = true; return true;
    }

    public void Draw(Graphics graphics)
    {
        using var image = Document.Render(_sourceBounds(), _preview, _editingIndex >= 0 ? _editingIndex : _movingIndex);
        graphics.DrawImage(image, _displayBounds());
    }

    public Bitmap Export() { CommitText(); return Document.Render(_sourceBounds()); }
    public void Undo() { CommitText(); Document.Undo(); RefreshHistory(); Notify(); }
    public void Redo() { CommitText(); Document.Redo(); RefreshHistory(); Notify(); }
    public void BoundsChanged() { UpdateTextBox(); Notify(); }
    public void CancelStroke() { _preview = null; _movingIndex = -1; _moveOriginal = null; _host.Capture = false; }
    private void RefreshHistory() => Toolbar.SetHistory(Document.CanUndo, Document.CanRedo);
    private void Notify() { _host.Invalidate(); Changed?.Invoke(); }
    public void Dispose() { CloseTextBox(); Document.Dispose(); Toolbar.Dispose(); }
}