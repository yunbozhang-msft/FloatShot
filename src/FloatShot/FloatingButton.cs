using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

    private const int Diameter  = 48;
    private const int ShadowPad = 8;
    private const int Total     = Diameter + ShadowPad * 2;
    private const int DragThreshold = 4;

    private bool _mouseDown, _isDragging, _isHovered, _isPressed;
    private Point _dragStartScreen, _formStart;

    public FloatingButton()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        ShowInTaskbar   = false;
        TopMost         = true;
        Size            = new Size(Total, Total);
        Cursor          = Cursors.Hand;
        Text            = "FloatShot";
        Icon            = AppIcon.Get();
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
                       |  0x00080000;   // WS_EX_LAYERED
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Render();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Painting is via UpdateLayeredWindow, not the normal flow.
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    private void Render()
    {
        using var bmp = new Bitmap(Total, Total, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            // Soft drop shadow underneath the icon
            for (int i = 0; i < 5; i++)
            {
                int s = ShadowPad - i;
                if (s < 0) break;
                int alpha = 32 - i * 6;
                using var sb = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), 0, 0, 0));
                g.FillEllipse(sb,
                    ShadowPad - s,
                    ShadowPad - s + 2,
                    Diameter + s * 2,
                    Diameter + s * 2);
            }

            // Render the app icon at the largest size that fits the diameter
            var rect = new Rectangle(ShadowPad, ShadowPad, Diameter, Diameter);
            // AppIcon.Get() returns a cached shared instance — must NOT dispose it.
            var icon = AppIcon.Get();
            using (var iconBmp = icon.ToBitmap())
            {
                if (_isPressed || _isHovered)
                {
                    // Slight scale animation on hover/press so the rim grows softly
                    var pad = _isPressed ? 1 : -1;
                    var hoverRect = new Rectangle(rect.X + pad, rect.Y + pad, rect.Width - pad * 2, rect.Height - pad * 2);
                    g.DrawImage(iconBmp, hoverRect);
                }
                else
                {
                    g.DrawImage(iconBmp, rect);
                }
            }
        }

        SetLayered(bmp);
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
    protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Render(); }
    protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _isPressed = false; Render(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _mouseDown = true; _isPressed = true; _isDragging = false;
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
            if (!_isDragging && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
                _isDragging = true;
            if (_isDragging)
                Location = new Point(_formStart.X + dx, _formStart.Y + dy);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _mouseDown)
        {
            _mouseDown = false; _isPressed = false; Render();
            if (!_isDragging) CaptureClicked?.Invoke(this, EventArgs.Empty);
            _isDragging = false;
        }
        else if (e.Button == MouseButtons.Right)
        {
            RightClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
