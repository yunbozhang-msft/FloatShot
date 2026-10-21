using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
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
    private readonly FlowLayoutPanel _tools = new BufferedFlowPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
    private readonly FlowLayoutPanel _options = new BufferedFlowPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(5, 3, 5, 3) };
    private readonly Dictionary<(AnnotationTool Tool, bool Variant), (AnnotationStyle Style, FlowLayoutPanel Panel)> _optionPages = new();
    private FlowLayoutPanel _optionPage = null!;
    private bool _updatingOptions;
    private readonly ToolTip _tips = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 10000 };
    private readonly Dictionary<AnnotationTool, ToolbarButton> _toolButtons = new();
    private readonly Dictionary<string, ToolbarButton> _actions = new();
    private AnnotationTool _tool;
    private AnnotationPreferences _preferences = new();
    private Rectangle _toolsSurface;
    private Rectangle _optionsSurface;

    public event Action<AnnotationTool>? ToolSelected;
    public event Action? PreferenceChanged;
    public event Action<string>? ActionRequested;

    public AnnotationToolbar()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Surface;
        Padding = new Padding(6);
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _options.Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
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
        var saveGroup = new SegmentedPanel { Size = new Size(63, 40), Padding = new Padding(2), Margin = new Padding(3, 1, 3, 1), WrapContents = false, BackColor = Surface };
        _actions["save"].Size = new Size(37, 36); _actions["save"].Margin = Padding.Empty;
        _actions["options"].Size = new Size(22, 36); _actions["options"].Margin = Padding.Empty; _actions["options"].SplitRight = true;
        saveGroup.Controls.Add(_actions["save"]); saveGroup.Controls.Add(_actions["options"]); _tools.Controls.Add(saveGroup);
        _actions["copy"].IconColor = Fluent.Accent;
        AddAction("close", "dismiss", "Close (Esc)");
        UpdateState(AnnotationTool.None, _preferences);
    }

    private ToolbarButton IconButton(string icon, string label)
    {
        var button = new ToolbarButton { Size = new Size(40, 40), Margin = new Padding(1), Cursor = Cursors.Hand, AccessibleName = label.Split('(')[0].Trim(), IconName = icon };
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

    private void Separator() => _tools.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = Hairline, Margin = new Padding(7, 9, 7, 0) });

    private int Pixels(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96f));
    private Size ControlSize(int width, int height) => new(Pixels(width), Pixels(height));
    private Padding ControlPadding(int left, int top, int right, int bottom) => new(left == 0 ? 0 : Pixels(left), top == 0 ? 0 : Pixels(top), right == 0 ? 0 : Pixels(right), bottom == 0 ? 0 : Pixels(bottom));

    public Rectangle ActionBounds(string action) => _actions[action].RectangleToScreen(_actions[action].ClientRectangle);
    public void SetSaveFolder(string folder) => _tips.SetToolTip(_actions["save"], $"Save to {folder}\nS / Ctrl+S");
    public void SetHistory(bool undo, bool redo) { _actions["undo"].Enabled = undo; _actions["redo"].Enabled = redo; }

    public void UpdateState(AnnotationTool tool, AnnotationPreferences preferences)
    {
        using var redraw = new RedrawBatch(this);
        _tool = tool; _preferences = preferences;
        foreach (var entry in _toolButtons)
        {
            entry.Value.Active = entry.Key == tool;
            entry.Value.IconColor = entry.Key == AnnotationTool.Highlight ? Color.FromArgb(245, 158, 11) : Ink;
            entry.Value.Invalidate();
        }
        SuspendLayout();
        _options.SuspendLayout();
        _options.Visible = false;
        if (tool != AnnotationTool.None)
        {
            var style = preferences.For(tool);
            var key = (tool, tool == AnnotationTool.Rectangle ? style.Filled : tool == AnnotationTool.Mosaic && style.Brush);
            bool createPage = !_optionPages.TryGetValue(key, out var cached) || !ReferenceEquals(cached.Style, style);
            if (createPage)
            {
                if (cached.Panel is not null) { _options.Controls.Remove(cached.Panel); cached.Panel.Dispose(); }
                _optionPage = new BufferedFlowPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, Visible = false };
                _optionPages[key] = (style, _optionPage);
                _options.Controls.Add(_optionPage);
            }
            else _optionPage = cached.Panel;
            foreach (Control page in _options.Controls) page.Visible = page == _optionPage;
            if (createPage)
            {
            if (tool != AnnotationTool.Mosaic) AddColors(style);
            if (tool == AnnotationTool.Rectangle)
            {
                AddChoices(("Outline", () => !style.Filled, () => style.Filled = false), ("Solid", () => style.Filled, () => style.Filled = true));
            }
            if (tool == AnnotationTool.Mosaic)
            {
                AddChoices(("Brush", () => style.Brush, () => style.Brush = true), ("Rectangle", () => !style.Brush, () => style.Brush = false));
                _optionPage.Controls.Add(new Label { Text = "Strength", AutoSize = true, UseCompatibleTextRendering = false, UseMnemonic = false, ForeColor = Muted, Margin = ControlPadding(12, 6, 5, 0) });
                AddSlider(2, 40, style.Strength, "Mosaic strength", value => style.Strength = value);
                if (style.Brush)
                {
                    _optionPage.Controls.Add(new Label { Text = "Size", AutoSize = true, UseCompatibleTextRendering = false, UseMnemonic = false, ForeColor = Muted, Margin = ControlPadding(12, 6, 5, 0) });
                    AddSlider(2, 80, style.Width, "Mosaic brush size", value => style.Width = value);
                }
            }
            else if (tool == AnnotationTool.Text)
            {
                var sizes = new ToolbarButton { Text = style.FontSize.ToString(), Size = ControlSize(60, 30), Margin = ControlPadding(10, 1, 4, 0), AccessibleName = "Font size", DropDown = true, ValueText = () => style.FontSize.ToString() };
                sizes.Click += (_, _) =>
                {
                    var menu = new ContextMenuStrip { Font = _options.Font, ShowImageMargin = false };
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
                _optionPage.Controls.Add(sizes);
                var bold = IconButton("bold", "Bold"); bold.Size = ControlSize(30, 30); bold.IconSize = 22; bold.ActiveWhen = () => style.Bold; bold.Indicator = false;
                bold.Click += (_, _) => { style.Bold = !style.Bold; PreferenceChanged?.Invoke(); UpdateState(_tool, _preferences); };
                _optionPage.Controls.Add(bold);
            }
            else if (tool != AnnotationTool.Rectangle || !style.Filled)
            {
                _optionPage.Controls.Add(new Label { Text = "Width", AutoSize = true, UseCompatibleTextRendering = false, UseMnemonic = false, ForeColor = Muted, Margin = ControlPadding(12, 6, 5, 0) });
                AddSlider(2, tool == AnnotationTool.Highlight ? 48 : 16, style.Width, "Width", value => style.Width = value);
            }
            }
            _updatingOptions = true;
            try
            {
                foreach (var slider in _optionPage.Controls.OfType<ToolbarSlider>()) slider.Value = slider.AccessibleName == "Mosaic strength" ? style.Strength : style.Width;
            }
            finally { _updatingOptions = false; }
            _optionPage.Visible = true;
            _optionPage.Invalidate(true);
        }
        _options.ResumeLayout(true);
        bool hasOptions = tool != AnnotationTool.None;
        _options.Visible = hasOptions;
        int padding = Pixels(8);
        _tools.Location = new Point(padding, padding);
        _tools.Size = _tools.PreferredSize;
        _toolsSurface = new Rectangle(0, 0, _tools.Width + padding * 2, _tools.Bottom + padding);
        int optionsTop = _toolsSurface.Bottom + Pixels(6);
        _options.Location = new Point(padding, optionsTop + Pixels(4));
        _options.Size = _options.PreferredSize;
        _optionsSurface = hasOptions ? new Rectangle(0, optionsTop, _options.Width + padding * 2, _options.Height + Pixels(8)) : Rectangle.Empty;
        Size = new Size(Math.Max(_toolsSurface.Width, _optionsSurface.Width), hasOptions ? _optionsSurface.Bottom : _toolsSurface.Bottom);
        UpdateOutline();
        ResumeLayout(); Invalidate();
    }

    public Rectangle Arrange(Rectangle available, int anchorRight, int below, int above)
    {
        int padding = Pixels(8);
        int headerLeft = Math.Clamp(anchorRight - _toolsSurface.Width, available.Left + padding, Math.Max(available.Left + padding, available.Right - _toolsSurface.Width - padding));
        int optionsLeft = _optionsSurface.IsEmpty ? headerLeft : Math.Clamp(headerLeft, available.Left + padding, Math.Max(available.Left + padding, available.Right - _optionsSurface.Width - padding));
        int left = Math.Min(headerLeft, optionsLeft);
        int right = Math.Max(headerLeft + _toolsSurface.Width, _optionsSurface.IsEmpty ? headerLeft : optionsLeft + _optionsSurface.Width);
        int reservedHeight = Math.Max(Height, _toolsSurface.Height + Pixels(64));
        int top = below;
        if (top + reservedHeight > available.Bottom - padding) top = above - reservedHeight;
        top = Math.Clamp(top, available.Top + padding, Math.Max(available.Top + padding, available.Bottom - reservedHeight - padding));
        SuspendLayout();
        _toolsSurface.X = headerLeft - left;
        _tools.Location = new Point(_toolsSurface.Left + padding, _tools.Location.Y);
        if (!_optionsSurface.IsEmpty)
        {
            _optionsSurface.X = optionsLeft - left;
            _options.Location = new Point(_optionsSurface.Left + padding, _options.Location.Y);
        }
        Size = new Size(right - left, Height);
        UpdateOutline(); ResumeLayout(); Invalidate();
        return new Rectangle(left, top, Width, Height);
    }

    private void UpdateOutline()
    {
        using (var toolsOutline = Rounded(_toolsSurface, Pixels(7)))
        {
            var shape = new Region(toolsOutline);
            if (!_optionsSurface.IsEmpty)
            {
                using var optionsOutline = Rounded(_optionsSurface, Pixels(7));
                shape.Union(optionsOutline);
            }
            var previous = Region; Region = shape; previous?.Dispose();
        }
    }

    private void AddChoices(params (string Label, Func<bool> Selected, Action Change)[] choices)
    {
        var group = new SegmentedPanel { AutoSize = true, WrapContents = false, Padding = ControlPadding(2, 2, 2, 2), Margin = ControlPadding(12, 0, 4, 0), BackColor = Color.FromArgb(245, 245, 245) };
        foreach (var choice in choices)
        {
            var button = new ToolbarButton { Text = choice.Label, Size = ControlSize(choice.Label.Length > 7 ? 80 : 64, 28), Margin = Padding.Empty, ActiveWhen = choice.Selected, Segment = true, Indicator = false, AccessibleName = choice.Label };
            button.Click += (_, _) => { choice.Change(); PreferenceChanged?.Invoke(); UpdateState(_tool, _preferences); };
            group.Controls.Add(button);
        }
        _optionPage.Controls.Add(group);
    }

    private void AddSlider(int minimum, int maximum, int value, string label, Action<int> change)
    {
        var slider = new ToolbarSlider { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(value, minimum, maximum), Size = ControlSize(label == "Mosaic strength" ? 120 : 84, 30), Margin = ControlPadding(4, 0, 3, 0), AccessibleName = label, Step = 1 };
        _tips.SetToolTip(slider, label);
        Label? output = label == "Mosaic strength" ? null : new Label { Text = slider.Value + " px", AutoSize = false, UseCompatibleTextRendering = false, UseMnemonic = false, Size = ControlSize(44, 30), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted, Margin = ControlPadding(4, 0, 3, 0) };
        slider.ValueChanged += (_, _) => { if (output is not null) output.Text = slider.Value + " px"; if (_updatingOptions) return; change(slider.Value); PreferenceChanged?.Invoke(); };
        _optionPage.Controls.Add(slider);
        if (output is not null) _optionPage.Controls.Add(output);
    }

    private void AddColors(AnnotationStyle style)
    {
        foreach (var color in new[] { Color.FromArgb(230, 45, 45), Color.Yellow, Color.FromArgb(22, 163, 74), Fluent.Accent, Color.Black, Color.White })
        {
            var swatch = new ToolbarButton { Size = ControlSize(28, 30), Margin = ControlPadding(1, 0, 1, 0), AccessibleName = color.Name, Swatch = color, IsSwatchSelected = () => color.ToArgb() == style.ColorArgb };
            swatch.Click += (_, _) => { style.ColorArgb = color.ToArgb(); PreferenceChanged?.Invoke(); _options.Invalidate(true); };
            _tips.SetToolTip(swatch, color == Color.Yellow ? "Yellow" : color == Color.White ? "White" : color == Color.Black ? "Black" : color == Fluent.Accent ? "Blue" : color.G > color.R ? "Green" : "Red");
            _optionPage.Controls.Add(swatch);
        }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Color.FromArgb(220, 220, 220));
        foreach (var surface in new[] { _toolsSurface, _optionsSurface })
        {
            if (surface.IsEmpty) continue;
            using var shape = Rounded(new RectangleF(surface.X + .5f, surface.Y + .5f, surface.Width - 1, surface.Height - 1), Pixels(7));
            args.Graphics.DrawPath(border, shape);
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) { _tips.Dispose(); _options.Font.Dispose(); } base.Dispose(disposing); }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath(); float diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }

    private class BufferedFlowPanel : FlowLayoutPanel
    {
        public BufferedFlowPanel()
        {
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);

    private readonly struct RedrawBatch : IDisposable
    {
        private readonly AnnotationToolbar _owner;
        private readonly bool _active;
        public RedrawBatch(AnnotationToolbar owner)
        {
            _owner = owner; _active = owner.IsHandleCreated && owner.Visible;
            if (_active) SendMessage(owner.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
        }
        public void Dispose()
        {
            if (!_active || _owner.IsDisposed) return;
            SendMessage(_owner.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
            _owner.Invalidate(true);
        }
    }

    private sealed class SegmentedPanel : BufferedFlowPanel
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
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? IconName { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color IconColor { get; set; } = Ink;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int IconSize { get; set; } = 24;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Active { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<bool>? ActiveWhen { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Func<string>? ValueText { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Indicator { get; set; } = true;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Segment { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DropDown { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool SplitRight { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color? Swatch { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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

        private int Pixels(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96f));
        private void DrawIcon(Graphics graphics, string name, Rectangle bounds, Color color, int logicalSize)
        {
            int size = Math.Min(Pixels(logicalSize), Math.Min(bounds.Width, bounds.Height));
            var image = FluentIcon.Get(name, size, color);
            graphics.DrawImageUnscaled(image, bounds.Left + (bounds.Width - size) / 2, bounds.Top + (bounds.Height - size) / 2);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            var graphics = args.Graphics; graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool active = ActiveWhen?.Invoke() ?? Active;
            graphics.Clear(Segment ? Parent?.BackColor ?? Surface : Surface);
            if (Swatch is { } color)
            {
                float centerX = Width / 2f, centerY = Height / 2f;
                using var fill = new SolidBrush(color); using var white = new Pen(Surface, Pixels(2));
                using var edge = new Pen(IsSwatchSelected?.Invoke() == true ? Ink : Color.FromArgb(170, 170, 170), (IsSwatchSelected?.Invoke() == true ? 1.5f : 1) * DeviceDpi / 96f);
                graphics.FillEllipse(fill, centerX - Pixels(9), centerY - Pixels(9), Pixels(18), Pixels(18));
                graphics.DrawEllipse(white, centerX - Pixels(10), centerY - Pixels(10), Pixels(20), Pixels(20));
                graphics.DrawEllipse(edge, centerX - Pixels(11), centerY - Pixels(11), Pixels(22), Pixels(22)); return;
            }
            var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
            using var background = new SolidBrush(Segment && active ? Surface : active ? AccentSoft : _pressed ? Color.FromArgb(232, 232, 232) : _hovered ? Color.FromArgb(245, 245, 245) : Segment ? Parent?.BackColor ?? Surface : Surface);
            using var shape = Rounded(bounds, Segment ? 3 : 4);
            graphics.FillPath(background, shape);
            if (Segment && active || DropDown)
            {
                using var border = new Pen(Hairline); graphics.DrawPath(border, shape);
            }
            if (SplitRight) { using var divider = new Pen(Hairline); graphics.DrawLine(divider, 0, 3, 0, Height - 3); }
            Color foreground = !Enabled ? Color.FromArgb(185, 185, 185) : active ? Accent : IconName is null ? Ink : IconColor;
            if (IconName is not null) DrawIcon(graphics, IconName, ClientRectangle, foreground, IconSize);
            else
            {
                var textBounds = ClientRectangle;
                if (DropDown) { textBounds.Width -= Pixels(20); DrawIcon(graphics, "chevron", new Rectangle(Width - Pixels(24), 0, Pixels(20), Height), Muted, 14); }
                TextRenderer.DrawText(graphics, ValueText?.Invoke() ?? Text, Font, textBounds, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (active && Indicator && !Segment)
            {
                using var marker = new Pen(Accent, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawLine(marker, Width / 2f - 5, Height - 3, Width / 2f + 5, Height - 3);
            }
        }
    }

    private sealed class ToolbarSlider : Control
    {
        private int _value;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minimum { get; set; }
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Maximum { get; set; } = 100;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Step { get; set; } = 1;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Value { get => _value; set { int next = Math.Clamp(value, Minimum, Maximum); if (_value == next) return; _value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
        public event EventHandler? ValueChanged;

        public ToolbarSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Surface; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider; TabStop = true;
        }

        private int Pixels(int logical) => Math.Max(1, (int)Math.Round(logical * DeviceDpi / 96f));
        private void SetFromMouse(int position) => Value = Minimum + (int)Math.Round(Math.Clamp((position - (float)Pixels(8)) / Math.Max(1, Width - Pixels(16)), 0, 1) * (Maximum - Minimum) / Step) * Step;
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
            float centerY = Height / 2f, thumb = Pixels(8) + (Width - Pixels(16)) * (Value - Minimum) / (float)Math.Max(1, Maximum - Minimum);
            using var track = new Pen(Color.FromArgb(210, 210, 210), Pixels(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var fill = new Pen(Accent, Pixels(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(track, Pixels(8), centerY, Width - Pixels(8), centerY); graphics.DrawLine(fill, Pixels(8), centerY, thumb, centerY);
            using var halo = new SolidBrush(Color.FromArgb(238, 238, 238)); using var thumbBrush = new SolidBrush(Accent);
            graphics.FillEllipse(halo, thumb - Pixels(8), centerY - Pixels(7), Pixels(16), Pixels(16));
            graphics.FillEllipse(Brushes.White, thumb - Pixels(7), centerY - Pixels(7), Pixels(14), Pixels(14));
            graphics.FillEllipse(thumbBrush, thumb - Pixels(5), centerY - Pixels(5), Pixels(10), Pixels(10));
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
    private ScreenshotTextBox? _textBox;
    private Font? _textFont;
    private PointF _textAnchor;
    private int _editingIndex = -1;
    private int _movingIndex = -1;
    private PointF _moveStart;
    private Annotation? _moveOriginal;
    private readonly Cursor _selectionCursor;
    private readonly Dictionary<(int Height, int Edge, int Color), (Cursor Cursor, IntPtr Handle)> _highlightCursors = new();
    private readonly Dictionary<(int Diameter, int Edge), (Cursor Cursor, IntPtr Handle)> _mosaicCursors = new();

    public AnnotationDocument Document { get; }
    public AnnotationToolbar Toolbar { get; } = new();
    public AnnotationTool Tool { get; private set; }
    public bool EditingText => _textBox is not null;
    public bool Drawing => _preview is not null;
    public Cursor DrawingCursor => Tool == AnnotationTool.Text ? Cursors.IBeam : Tool == AnnotationTool.Highlight ? HighlightCursor() : Tool == AnnotationTool.Mosaic && _settings.Annotation.Mosaic.Brush ? MosaicCursor() : Cursors.Cross;
    public event Action? Changed;
    public event Action<string>? ActionRequested;

    public AnnotationEditor(Control host, Bitmap source, Func<Rectangle> displayBounds, Func<Rectangle> sourceBounds, Settings settings)
    {
        _host = host; _displayBounds = displayBounds; _sourceBounds = sourceBounds; _settings = settings; _selectionCursor = host.Cursor;
        _settings.Annotation ??= new(); _settings.Annotation.Normalize();
        Document = new AnnotationDocument(source);
        Toolbar.ToolSelected += ToggleTool;
        Toolbar.PreferenceChanged += () => { _settings.Save(); UpdateTextBox(); UpdateDrawingCursor(); Changed?.Invoke(); };
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
    private void UpdateDrawingCursor() => _host.Cursor = Tool == AnnotationTool.None ? _selectionCursor : DrawingCursor;

    private Cursor HighlightCursor()
    {
        var style = _settings.Annotation.Highlight;
        int height = Math.Max(1, (int)Math.Round(style.Width * Zoom));
        int edge = Math.Max(1, (int)Math.Round(_host.DeviceDpi / 96f));
        var key = (height, edge, style.Ink.ToArgb());
        if (_highlightCursors.TryGetValue(key, out var cached)) return cached.Cursor;
        using var image = new Bitmap(edge * 5, height + edge * 2);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(Brushes.Black, 0, 0, image.Width, image.Height);
            graphics.FillRectangle(Brushes.White, edge, edge, edge * 3, height);
            using var ink = new SolidBrush(style.Ink);
            graphics.FillRectangle(ink, edge * 2, edge, edge, height);
        }
        var native = CreateDrawingCursor(image);
        _highlightCursors.Add(key, native);
        return native.Cursor;
    }

    private Cursor MosaicCursor()
    {
        int diameter = Math.Max(1, (int)Math.Round(_settings.Annotation.Mosaic.Width * Zoom));
        int edge = Math.Max(1, (int)Math.Round(_host.DeviceDpi / 96f));
        var key = (diameter, edge);
        if (_mosaicCursors.TryGetValue(key, out var cached)) return cached.Cursor;
        using var image = new Bitmap(diameter + edge * 2, diameter + edge * 2);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(Color.FromArgb(166, Color.White));
            using var dark = new Pen(Color.FromArgb(48, Color.Black), edge * 3);
            using var light = new Pen(Color.FromArgb(220, Color.White), edge * 2);
            graphics.FillEllipse(fill, edge, edge, diameter, diameter);
            graphics.DrawEllipse(dark, edge, edge, diameter, diameter);
            graphics.DrawEllipse(light, edge, edge, diameter, diameter);
        }
        var native = CreateDrawingCursor(image);
        _mosaicCursors.Add(key, native);
        return native.Cursor;
    }

    private static (Cursor Cursor, IntPtr Handle) CreateDrawingCursor(Bitmap image)
    {
        var header = new NativeCursorBitmap { Size = (uint)Marshal.SizeOf<NativeCursorBitmap>(), Width = image.Width, Height = -image.Height, Planes = 1, BitCount = 32 };
        var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var pixels, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[image.Width * 4];
                for (int vertical = 0; vertical < image.Height; vertical++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, vertical * data.Stride), row, 0, row.Length);
                    Marshal.Copy(row, 0, IntPtr.Add(pixels, vertical * row.Length), row.Length);
                }
            }
            finally { image.UnlockBits(data); }
            using var mask = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format1bppIndexed);
            var maskHandle = mask.GetHbitmap();
            try
            {
                var info = new NativeCursorInfo { IsIcon = false, HotspotX = (uint)(image.Width / 2), HotspotY = (uint)(image.Height / 2), Mask = maskHandle, Color = color };
                var handle = CreateIconIndirect(ref info);
                if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                return (new Cursor(handle), handle);
            }
            finally { DeleteObject(maskHandle); }
        }
        finally { DeleteObject(color); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCursorBitmap
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int HorizontalResolution, VerticalResolution;
        public uint UsedColors, ImportantColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCursorInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint HotspotX, HotspotY;
        public IntPtr Mask, Color;
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(IntPtr icon, out NativeCursorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateIconIndirect(ref NativeCursorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyCursor(IntPtr cursor);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr context, ref NativeCursorBitmap info, uint usage, out IntPtr pixels, IntPtr section, uint offset);

    private PointF ToImage(Point point)
    {
        var display = _displayBounds(); var source = _sourceBounds();
        return new PointF(Math.Clamp(source.X + (point.X - display.X) / Zoom, source.Left, source.Right - 1), Math.Clamp(source.Y + (point.Y - display.Y) / Zoom, source.Top, source.Bottom - 1));
    }

    public void ToggleTool(AnnotationTool tool)
    {
        CommitText(); CancelStroke(); Tool = Tool == tool ? AnnotationTool.None : tool;
        Toolbar.UpdateState(Tool, _settings.Annotation); UpdateDrawingCursor(); Changed?.Invoke();
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
        _textBox = new ScreenshotTextBox(Document.Render(_sourceBounds(), excluded: index)) { Multiline = true, AcceptsReturn = true, BorderStyle = BorderStyle.FixedSingle, Text = index >= 0 ? Document[index].Text : "", AccessibleName = "Annotation text" };
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
        _textBox.UpdateBackground(display);
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
        if (_preview is not { Tool: AnnotationTool.Mosaic, Style.Brush: false } preview || preview.Bounds.Width <= 0 || preview.Bounds.Height <= 0) return;
        var source = _sourceBounds(); var display = _displayBounds(); var bounds = preview.Bounds;
        var outline = new RectangleF(display.Left + (bounds.Left - source.Left) * Zoom, display.Top + (bounds.Top - source.Top) * Zoom, bounds.Width * Zoom, bounds.Height * Zoom);
        float width = _host.DeviceDpi / 96f;
        using var clip = graphics.Clip;
        graphics.SetClip(display, CombineMode.Intersect);
        using var halo = new Pen(Color.White, width * 3);
        using var border = new Pen(Color.FromArgb(40, 40, 40), width) { DashStyle = DashStyle.Dash };
        graphics.DrawRectangle(halo, outline.X, outline.Y, outline.Width, outline.Height);
        graphics.DrawRectangle(border, outline.X, outline.Y, outline.Width, outline.Height);
        graphics.Clip = clip;
    }

    public Bitmap Export() { CommitText(); return Document.Render(_sourceBounds()); }
    public void Undo() { CommitText(); Document.Undo(); RefreshHistory(); Notify(); }
    public void Redo() { CommitText(); Document.Redo(); RefreshHistory(); Notify(); }
    public void BoundsChanged() { _textBox?.ReplaceBackground(Document.Render(_sourceBounds(), excluded: _editingIndex)); UpdateTextBox(); UpdateDrawingCursor(); Notify(); }
    public void CancelStroke() { _preview = null; _movingIndex = -1; _moveOriginal = null; _host.Capture = false; }
    private void RefreshHistory() => Toolbar.SetHistory(Document.CanUndo, Document.CanRedo);
    private void Notify() { _host.Invalidate(); Changed?.Invoke(); }
    public void Dispose()
    {
        CloseTextBox(); _host.Cursor = _selectionCursor;
        foreach (var cursor in _highlightCursors.Values.Concat(_mosaicCursors.Values)) { cursor.Cursor.Dispose(); DestroyCursor(cursor.Handle); }
        _highlightCursors.Clear(); _mosaicCursors.Clear(); Document.Dispose(); Toolbar.Dispose();
    }

    private sealed class ScreenshotTextBox(Bitmap background) : TextBox
    {
        private Bitmap _background = background;
        private IntPtr _brush;
        private BackgroundWindow? _backgroundWindow;
        private Rectangle _backgroundBounds;
        private Size _backgroundSize;

        public void ReplaceBackground(Bitmap image)
        {
            _background.Dispose(); _background = image;
            if (_brush != IntPtr.Zero) { DeleteObject(_brush); _brush = IntPtr.Zero; }
        }

        public void UpdateBackground(Rectangle display)
        {
            var origin = Parent!.PointToClient(PointToScreen(Point.Empty));
            var bounds = new Rectangle(display.Left - origin.X, display.Top - origin.Y, display.Width, display.Height);
            if (_brush != IntPtr.Zero && _backgroundBounds == bounds && _backgroundSize == ClientSize) return;
            using var image = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
            using (var graphics = Graphics.FromImage(image)) graphics.DrawImage(_background, bounds);
            var bitmap = image.GetHbitmap();
            IntPtr brush;
            try { brush = CreatePatternBrush(bitmap); }
            finally { DeleteObject(bitmap); }
            if (_brush != IntPtr.Zero) DeleteObject(_brush);
            _brush = brush; _backgroundBounds = bounds; _backgroundSize = ClientSize;
            _backgroundWindow ??= new BackgroundWindow(this);
            Invalidate();
        }

        private sealed class BackgroundWindow : NativeWindow
        {
            private readonly ScreenshotTextBox _editor;
            public BackgroundWindow(ScreenshotTextBox editor) { _editor = editor; AssignHandle(editor.Parent!.Handle); }

            protected override void WndProc(ref Message message)
            {
                if (_editor._brush != IntPtr.Zero && message.LParam == _editor.Handle && message.Msg is 0x0133 or 0x0138)
                {
                    SetBkMode(message.WParam, 1);
                    var color = _editor.ForeColor;
                    SetTextColor(message.WParam, (uint)(color.R | color.G << 8 | color.B << 16));
                    message.Result = _editor._brush;
                    return;
                }
                base.WndProc(ref message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _backgroundWindow?.ReleaseHandle();
            base.Dispose(disposing);
            if (disposing) _background.Dispose();
            if (_brush != IntPtr.Zero) { DeleteObject(_brush); _brush = IntPtr.Zero; }
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreatePatternBrush(IntPtr bitmap);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr deviceContext, int mode);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr deviceContext, uint color);
    }
}