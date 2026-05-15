using System.Runtime.InteropServices;

namespace FloatShot;

internal static class WindowOps
{
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint SWP_NOMOVE     = 0x0002;
    public const uint SWP_NOSIZE     = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;

    public static void EnsureTopMost(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
