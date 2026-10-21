using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FloatShot;

/// <summary>
/// Per-pixel alpha layered floating button. No magenta color-key — uses
/// UpdateLayeredWindow so anti-aliased edges and shadows blend cleanly with
/// the screen behind. This is what PixPin / Snipping Tool / Stickies use.
/// </summary>
internal sealed class FloatingButton : Form
{
    public event EventHandler? CaptureClicked;
    public event EventHandler? RightClicked;
    public event EventHandler? PositionCommitted;

    private const int Diameter  = 40;
    private const int ShadowPad = 6;
    private const int Total     = Diameter + ShadowPad * 2;
    private const int DragThreshold = 4;

    private bool _mouseDown, _isDragging, _isHovered, _isPressed;
    private Point _dragStartScreen, _formStart;
    private FloatingDockSide _dockSide;
    private string? _dockScreenName;
    private bool _collapsed;
    private bool _menuOpen;
    private readonly System.Windows.Forms.Timer _collapseTimer = new() { Interval = 1800 };

    public FloatingDockSide DockSide => _dockSide;
    public bool IsCollapsed => _collapsed;
    public string? DockScreenName => _dockScreenName;
    public Point SavedLocation
    {
        get
        {
            if (_dockSide == FloatingDockSide.None) return Location;
            var area = DockScreen().WorkingArea;
            return new Point(_dockSide == FloatingDockSide.Left ? area.Left : area.Right - Pixels(Total), Top);
        }
    }

    public FloatingButton()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        TopMost         = true;
        Size            = new Size(Pixels(Total), Pixels(Total));
        Cursor          = Cursors.Hand;
        Text            = "FloatShot";
        Icon            = AppIcon.Get();
        AutoScaleMode   = AutoScaleMode.None;
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!Visible || _mouseDown || _menuOpen || Bounds.Contains(Cursor.Position)) return;
            Collapse();
        };
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
    }

    // WS_EX_LAYERED is required for UpdateLayeredWindow.
    // WS_EX_TOOLWINDOW + TOPMOST to behave like PixPin pin.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000008    // WS_EX_TOPMOST
                       |  0x00000080    // WS_EX_TOOLWINDOW
                       |  0x00080000
                       |  0x08000000;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateFrameSize();
        if (_dockSide != FloatingDockSide.None) PlaceAtDock();
        Render();
    }

    private int Pixels(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateFrameSize();
        if (!_isDragging && _dockSide != FloatingDockSide.None) PlaceAtDock();
        Render();
    }

    protected override bool ShowWithoutActivation => true;

    private Screen DockScreen() => Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == _dockScreenName) ?? Screen.FromRectangle(Bounds);
    private void UpdateFrameSize()
    {
        Size = new Size(Pixels(_collapsed ? 24 : Total), Pixels(Total));
        AccessibleName = _collapsed ? "Capture screenshot (docked)" : "Capture screenshot";
    }

    internal static FloatingDockSide FindDockSide(Rectangle bounds, Rectangle workArea, int threshold)
    {
        bool left = bounds.Left <= workArea.Left + threshold;
        bool right = bounds.Right >= workArea.Right - threshold;
        if (left && right) return Math.Abs(bounds.Left - workArea.Left) <= Math.Abs(bounds.Right - workArea.Right) ? FloatingDockSide.Left : FloatingDockSide.Right;
        return left ? FloatingDockSide.Left : right ? FloatingDockSide.Right : FloatingDockSide.None;
    }

    internal static Point ConstrainPosition(Point position, Size size, Rectangle workArea) => new(
        Math.Clamp(position.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
        Math.Clamp(position.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));

    public void RestorePosition(Point position, FloatingDockSide side, string? screenName)
    {
        var screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == screenName)
            ?? Screen.AllScreens.FirstOrDefault(candidate => candidate.Bounds.Contains(position))
            ?? Screen.PrimaryScreen!;
        _dockSide = Enum.IsDefined(side) ? side : FloatingDockSide.None;
        _dockScreenName = _dockSide == FloatingDockSide.None ? null : screen.DeviceName;
        _collapsed = _dockSide != FloatingDockSide.None;
        UpdateFrameSize();
        Location = ConstrainPosition(position, Size, screen.WorkingArea);
        if (_dockSide != FloatingDockSide.None) PlaceAtDock();
        Render();
    }

    public void DockTo(FloatingDockSide side)
    {
        if (!Enum.IsDefined(side)) return;
        var screen = Screen.FromRectangle(Bounds);
        _dockSide = side; _dockScreenName = side == FloatingDockSide.None ? null : screen.DeviceName;
        _collapsed = side != FloatingDockSide.None;
        UpdateFrameSize();
        if (_collapsed) PlaceAtDock();
        else Location = ConstrainPosition(Location, Size, screen.WorkingArea);
        Render(); PositionCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void PlaceAtDock()
    {
        var screen = DockScreen(); _dockScreenName = screen.DeviceName;
        var area = screen.WorkingArea;
        Location = ConstrainPosition(new Point(_dockSide == FloatingDockSide.Left ? area.Left : area.Right - Width, Top), Size, area);
    }

    public void Reveal()
    {
        _collapseTimer.Stop();
        if (!_collapsed) return;
        _collapsed = false; UpdateFrameSize(); PlaceAtDock(); Render(); ArmCollapse();
    }

    private void Collapse()
    {
        if (_dockSide == FloatingDockSide.None || _collapsed || _mouseDown || _menuOpen) return;
        _collapsed = true; UpdateFrameSize(); PlaceAtDock(); Render();
    }

    public void HoldOpen(bool hold)
    {
        if (IsDisposed) return;
        _menuOpen = hold; _collapseTimer.Stop();
        if (!hold) ArmCollapse();
    }

    private void ArmCollapse()
    {
        if (_dockSide != FloatingDockSide.None && !_collapsed && Visible && !_mouseDown && !_menuOpen)
        {
            _collapseTimer.Stop(); _collapseTimer.Start();
        }
    }

    private void DisplaySettingsChanged(object? sender, EventArgs args)
    {
        if (!IsHandleCreated || IsDisposed) return;
        try { BeginInvoke((Action)EnsureOnScreen); } catch (InvalidOperationException) { }
    }

    private void EnsureOnScreen()
    {
        if (IsDisposed || _mouseDown) return;
        if (_dockSide != FloatingDockSide.None) PlaceAtDock();
        else Location = ConstrainPosition(Location, Size, Screen.FromRectangle(Bounds).WorkingArea);
        Render(); PositionCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_collapseTimer is null) return;
        if (Visible) { Render(); ArmCollapse(); } else _collapseTimer.Stop();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Painting is via UpdateLayeredWindow, not the normal flow.
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    private void Render()
    {
        if (!IsHandleCreated || IsDisposed) return;
        using var bmp = CreateFrame();
        SetLayered(bmp);
    }

    private Bitmap CreateFrame()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            if (_collapsed)
            {
                var tab = new RectangleF(_dockSide == FloatingDockSide.Left ? -Pixels(8) : Pixels(2), Pixels(7), Width + Pixels(6), Height - Pixels(14));
                using var shape = Rounded(tab, Pixels(10));
                using var fill = new SolidBrush(Color.FromArgb(250, 255, 255, 255));
                using var border = new Pen(_isHovered ? Fluent.AccentHover : Fluent.Accent, Pixels(1));
                g.FillPath(fill, shape); g.DrawPath(border, shape);
                var brand = AppIcon.GetBitmap(Pixels(20));
                g.DrawImageUnscaled(brand, (Width - brand.Width) / 2, (Height - brand.Height) / 2);
                return bmp;
            }

            // Soft drop shadow underneath the icon
            for (int i = 0; i < 5; i++)
            {
                int s = Pixels(ShadowPad) - Pixels(i);
                if (s < 0) break;
                int alpha = 14 - i * 3;
                using var sb = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), 0, 0, 0));
                g.FillEllipse(sb,
                    Pixels(ShadowPad) - s,
                    Pixels(ShadowPad) - s + Pixels(2),
                    Pixels(Diameter) + s * 2,
                    Pixels(Diameter) + s * 2);
            }

            // Render the app icon at the largest size that fits the diameter
            var rect = new Rectangle(Pixels(ShadowPad), Pixels(ShadowPad), Pixels(Diameter), Pixels(Diameter));
            var iconBmp = AppIcon.GetBitmap(rect.Width);
            g.DrawImageUnscaled(iconBmp, rect.Location);
            if (_isPressed || _isHovered)
            {
                using var rim = new Pen(Color.FromArgb(180, Fluent.Accent), Math.Max(1, Pixels(1)));
                g.DrawEllipse(rim, rect);
            }
        }

        return bmp;
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath(); float diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }

    // ===== UpdateLayeredWindow plumbing =====
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE  { public int W, H; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private void SetLayered(Bitmap bmp)
    {
        if (!IsHandleCreated) return;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr oldBitmap = SelectObject(memDc, hBitmap);

        var size = new SIZE { W = bmp.Width, H = bmp.Height };
        var src  = new POINT { X = 0, Y = 0 };
        var dst  = new POINT { X = Left, Y = Top };
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1   // AC_SRC_ALPHA
        };
        UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);

        SelectObject(memDc, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    // Mouse handling — drag with threshold, click triggers capture
    protected override void OnMouseEnter(EventArgs e) { _collapseTimer.Stop(); _isHovered = true; Render(); }
    protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _isPressed = false; Render(); ArmCollapse(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _mouseDown = true; _isPressed = true; _isDragging = false;
            _collapseTimer.Stop(); Capture = true;
            _dragStartScreen = Cursor.Position;
            _formStart = Location;
            Render();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mouseDown && (e.Button & MouseButtons.Left) != 0)
        {
            var cur = Cursor.Position;
            var dx = cur.X - _dragStartScreen.X;
            var dy = cur.Y - _dragStartScreen.Y;
            if (!_isDragging && (Math.Abs(dx) > Pixels(DragThreshold) || Math.Abs(dy) > Pixels(DragThreshold)))
            {
                _isDragging = true;
                bool wasCollapsed = _collapsed;
                _dockSide = FloatingDockSide.None; _dockScreenName = null; _collapsed = false;
                if (wasCollapsed)
                {
                    UpdateFrameSize(); _dragStartScreen = cur;
                    _formStart = new Point(cur.X - Width / 2, cur.Y - Height / 2); dx = 0; dy = 0;
                    Render();
                }
            }
            if (_isDragging)
                Location = new Point(_formStart.X + dx, _formStart.Y + dy);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _mouseDown)
        {
            bool dragged = _isDragging;
            _mouseDown = false; _isPressed = false; _isDragging = false; Capture = false;
            if (dragged)
            {
                var screen = Screen.FromRectangle(Bounds);
                var side = FindDockSide(Bounds, screen.WorkingArea, Pixels(20));
                if (side != FloatingDockSide.None) DockTo(side);
                else { Location = ConstrainPosition(Location, Size, screen.WorkingArea); Render(); PositionCommitted?.Invoke(this, EventArgs.Empty); }
            }
            else { Render(); CaptureClicked?.Invoke(this, EventArgs.Empty); ArmCollapse(); }
        }
        else if (e.Button == MouseButtons.Right)
        {
            HoldOpen(true);
            RightClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture && _mouseDown) { _mouseDown = false; _isDragging = false; _isPressed = false; Render(); ArmCollapse(); }
        base.OnMouseCaptureChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged; _collapseTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
