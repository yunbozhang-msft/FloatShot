using System.Runtime.InteropServices;

namespace FloatShot;

/// <summary>
/// 跨进程消息: 已运行实例被再次启动时, 让旧实例"显示自己"。
/// 简单实现: 注册一个全局 Windows 消息 ID, 用 PostMessage 广播给 HWND_BROADCAST。
/// </summary>
internal static class AppMessages
{
    private const string MsgName = "FloatShot.Show";
    public static readonly uint ShowMessage = RegisterWindowMessage(MsgName);

    private const int HWND_BROADCAST = 0xFFFF;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public static void NotifyExisting()
    {
        PostMessage(new IntPtr(HWND_BROADCAST), ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }
}
