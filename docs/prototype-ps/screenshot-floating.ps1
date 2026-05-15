<#
.SYNOPSIS
    桌面悬浮截图按钮 (TopMost, 可拖动)。
.DESCRIPTION
    在桌面上常驻一个小型悬浮按钮，单击即截屏。可拖动到任意位置；
    右键菜单可切换截图范围 / 隐藏到托盘 / 退出。
    同时注册全局热键 Ctrl+Alt+Shift+S，作为全屏远程桌面下的备用入口。

    ⚠️ 重要限制：
    悬浮按钮无法显示在「全屏 RDP / Windows 365 / AVD」之上 (操作系统层面的限制)。
    全屏远程桌面时请使用全局热键截图，本地桌面或窗口化远程桌面下可点击按钮。
.PARAMETER SaveFolder
    保存目录，默认 %USERPROFILE%\Pictures\Screenshots
#>
[CmdletBinding()]
param(
    [string]$SaveFolder = (Join-Path $env:USERPROFILE 'Pictures\Screenshots')
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $SaveFolder)) {
    New-Item -ItemType Directory -Path $SaveFolder -Force | Out-Null
}

# --- 全局热键 + 持续 TopMost 维持 C# 类 ---
$cs = @"
using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public static class TopMostHelper {
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;

    public static void Reassert(IntPtr hWnd) {
        SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

public class HotkeyWindow : NativeWindow, IDisposable {
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const int WM_HOTKEY = 0x0312;
    private int _id;
    public event EventHandler HotkeyPressed;
    public HotkeyWindow(uint modifiers, uint key, int id) {
        _id = id;
        CreateHandle(new CreateParams());
        if (!RegisterHotKey(Handle, _id, modifiers, key))
            throw new Exception("RegisterHotKey failed (err " + Marshal.GetLastWin32Error() + ")");
    }
    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == _id) {
            EventHandler h = HotkeyPressed; if (h != null) h(this, EventArgs.Empty);
        }
        base.WndProc(ref m);
    }
    public void Dispose() {
        try { UnregisterHotKey(Handle, _id); } catch { }
        try { DestroyHandle(); } catch { }
    }
}
"@
if (-not ('HotkeyWindow' -as [type])) {
    Add-Type -TypeDefinition $cs -ReferencedAssemblies System.Windows.Forms, System.Drawing
}

# --- 状态: 截图范围 ---
$script:PrimaryOnly = $false

function Invoke-Capture {
    # 先把悬浮窗藏起来再截，避免按钮被截进去
    $script:form.Opacity = 0
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 80
    try {
        if ($script:PrimaryOnly) {
            $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        } else {
            $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
        }
        $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $g   = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
            $ts   = Get-Date -Format 'yyyyMMdd_HHmmss_fff'
            $file = Join-Path $SaveFolder "shot_$ts.png"
            $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
            return $file
        } finally {
            $g.Dispose(); $bmp.Dispose()
        }
    } finally {
        $script:form.Opacity = 0.85
    }
}

# --- 悬浮窗体 ---
# 关键: 模仿 PixPin 浮窗 (WS_EX_LAYERED + WS_EX_TOOLWINDOW + WS_EX_TOPMOST)
# WS_EX_LAYERED 让窗口走 DWM 单独 surface, Windows App / RDP 在跨虚拟显示器投影时能正确采集
# 通过 .NET 的 protected CreateParams 注入 ExStyle
if (-not ('FloatForm' -as [type])) {
    Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @"
using System;
using System.Windows.Forms;
public class FloatForm : Form {
    public const int WS_EX_TOPMOST    = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED    = 0x00080000;
    protected override CreateParams CreateParams {
        get {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            return cp;
        }
    }
}
"@
}

$form = New-Object FloatForm
$script:form = $form
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$form.StartPosition   = [System.Windows.Forms.FormStartPosition]::Manual
$form.TopMost         = $true
# 注意: ShowInTaskbar 必须为 false (因为我们用了 WS_EX_TOOLWINDOW, 不会出现在任务栏)
# 但 Win+Tab Task View 仍可识别 layered tool window —— PixPin 就是这种
$form.ShowInTaskbar   = $false
$form.Size            = New-Object System.Drawing.Size 56, 56
$form.BackColor       = [System.Drawing.Color]::FromArgb(30, 30, 30)
$form.Opacity         = 0.85
$form.Text            = 'Screenshot'
# 默认放在主屏右下角上方一些
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.Location = New-Object System.Drawing.Point ($wa.Right - 80), ($wa.Bottom - 200)

# 圆角
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$d    = 24
$path.AddArc(0, 0, $d, $d, 180, 90)
$path.AddArc($form.Width - $d - 1, 0, $d, $d, 270, 90)
$path.AddArc($form.Width - $d - 1, $form.Height - $d - 1, $d, $d, 0, 90)
$path.AddArc(0, $form.Height - $d - 1, $d, $d, 90, 90)
$path.CloseFigure()
$form.Region = New-Object System.Drawing.Region $path

# --- 圆形相机按钮 ---
$btn = New-Object System.Windows.Forms.Button
$btn.Size      = New-Object System.Drawing.Size 48, 48
$btn.Location  = New-Object System.Drawing.Point 4, 4
$btn.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$btn.FlatAppearance.BorderSize = 0
$btn.BackColor = [System.Drawing.Color]::FromArgb(0, 122, 204)
$btn.ForeColor = [System.Drawing.Color]::White
$btn.Font      = New-Object System.Drawing.Font('Segoe UI Emoji', 16, [System.Drawing.FontStyle]::Bold)
$btn.Text      = [char]0xD83D + [char]0xDCF7  # 📷
$btn.TabStop   = $false
$btn.Cursor    = [System.Windows.Forms.Cursors]::Hand
# 按钮圆形
$bp = New-Object System.Drawing.Drawing2D.GraphicsPath
$bp.AddEllipse(0, 0, 48, 48)
$btn.Region = New-Object System.Drawing.Region $bp
$form.Controls.Add($btn)

# --- 拖动: 在按钮上「按住右键拖」或在窗体边框拖动 ---
# 单击左键 = 截图；按住中键 / 边框 = 拖动
$dragging = $false
$dragOff  = New-Object System.Drawing.Point 0, 0

$startDrag = {
    param($s, $e)
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Middle) {
        $script:dragging = $true
        $script:dragOff  = New-Object System.Drawing.Point $e.X, $e.Y
    }
}
$doDrag = {
    param($s, $e)
    if ($script:dragging) {
        $screenPt = $s.PointToScreen([System.Drawing.Point]::new($e.X, $e.Y))
        $form.Location = New-Object System.Drawing.Point ($screenPt.X - $script:dragOff.X), ($screenPt.Y - $script:dragOff.Y)
    }
}
$endDrag = { param($s, $e) $script:dragging = $false }

$btn.add_MouseDown($startDrag); $btn.add_MouseMove($doDrag); $btn.add_MouseUp($endDrag)
$form.add_MouseDown($startDrag); $form.add_MouseMove($doDrag); $form.add_MouseUp($endDrag)

# --- 截图动作 ---
$captureAction = {
    try {
        $f = Invoke-Capture
        $script:tray.BalloonTipTitle = 'Screenshot saved'
        $script:tray.BalloonTipText  = $f
        $script:tray.BalloonTipIcon  = [System.Windows.Forms.ToolTipIcon]::Info
        $script:tray.ShowBalloonTip(1500)
    } catch {
        $script:tray.BalloonTipTitle = 'Screenshot failed'
        $script:tray.BalloonTipText  = $_.Exception.Message
        $script:tray.BalloonTipIcon  = [System.Windows.Forms.ToolTipIcon]::Error
        $script:tray.ShowBalloonTip(3000)
    }
}
$btn.add_Click($captureAction)

# --- 右键菜单 ---
$menu     = New-Object System.Windows.Forms.ContextMenuStrip
$miCap    = $menu.Items.Add('Capture now')
$miMode   = $menu.Items.Add('Mode: All screens')   # 切换主屏 / 全部
[void]$menu.Items.Add('-')
$miOpen   = $menu.Items.Add('Open folder')
$miHide   = $menu.Items.Add('Hide button (use tray / hotkey)')
[void]$menu.Items.Add('-')
$miExit   = $menu.Items.Add('Exit')

$miCap.add_Click($captureAction)
$miOpen.add_Click({ Start-Process explorer.exe $SaveFolder })
$miMode.add_Click({
    $script:PrimaryOnly = -not $script:PrimaryOnly
    $miMode.Text = if ($script:PrimaryOnly) { 'Mode: Primary screen only' } else { 'Mode: All screens' }
})
$miHide.add_Click({ $form.Visible = $false })

# 添加: 隐藏任务栏图标 (Pin 到所有桌面后用)
[void]$menu.Items.Add('-')
$miHideTaskbar = $menu.Items.Add('Hide taskbar icon (after pinning)')
$miHideTaskbar.add_Click({
    # 切换 ShowInTaskbar 需要重建窗口，用 Tool Window ExStyle 隐藏更稳
    $WS_EX_TOOLWINDOW = 0x00000080
    $GWL_EXSTYLE      = -20
    $hwnd = $form.Handle
    Add-Type -Name UserHide -Namespace Win32 -MemberDefinition '
        [DllImport("user32.dll")] public static extern int GetWindowLong(System.IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(System.IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int c);
    ' -ErrorAction SilentlyContinue
    [Win32.UserHide]::ShowWindow($hwnd, 0) | Out-Null  # SW_HIDE
    $cur = [Win32.UserHide]::GetWindowLong($hwnd, $GWL_EXSTYLE)
    [Win32.UserHide]::SetWindowLong($hwnd, $GWL_EXSTYLE, $cur -bor $WS_EX_TOOLWINDOW) | Out-Null
    [Win32.UserHide]::ShowWindow($hwnd, 8) | Out-Null  # SW_SHOWNA
    [TopMostHelper]::Reassert($hwnd)
})

# 把右键菜单挂到窗体和按钮（按钮的左键已用于截图，右键弹菜单）
$form.ContextMenuStrip = $menu
$btn.ContextMenuStrip  = $menu

# --- 系统托盘 (隐藏按钮后仍可操作) ---
$tray = New-Object System.Windows.Forms.NotifyIcon
$script:tray = $tray
$tray.Icon    = [System.Drawing.SystemIcons]::Application
$tray.Visible = $true
$tray.Text    = 'Screenshot floating button (Ctrl+Alt+Shift+S)'

$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
$tmShow   = $trayMenu.Items.Add('Show button')
$tmCap    = $trayMenu.Items.Add('Capture now')
$tmOpen   = $trayMenu.Items.Add('Open folder')
[void]$trayMenu.Items.Add('-')
$tmExit   = $trayMenu.Items.Add('Exit')
$tray.ContextMenuStrip = $trayMenu

$tmShow.add_Click({ $form.Visible = $true; $form.TopMost = $true })
$tmCap.add_Click($captureAction)
$tmOpen.add_Click({ Start-Process explorer.exe $SaveFolder })
$tray.add_DoubleClick({ $form.Visible = $true; $form.TopMost = $true })

# --- 注册全局热键 ---
$MOD_CONTROL = 0x0002; $MOD_ALT = 0x0001; $MOD_SHIFT = 0x0004; $MOD_NOREPEAT = 0x4000
$VK_S = 0x53
$modifiers = $MOD_CONTROL -bor $MOD_ALT -bor $MOD_SHIFT -bor $MOD_NOREPEAT
try {
    $hotkey = [HotkeyWindow]::new([uint32]$modifiers, [uint32]$VK_S, 1)
    $hotkey.add_HotkeyPressed($captureAction)
} catch {
    $tray.BalloonTipTitle = 'Hotkey registration failed'
    $tray.BalloonTipText  = $_.Exception.Message
    $tray.BalloonTipIcon  = [System.Windows.Forms.ToolTipIcon]::Warning
    $tray.ShowBalloonTip(3000)
    $hotkey = $null
}

$tmExit.add_Click({
    if ($hotkey) { try { $hotkey.Dispose() } catch { } }
    $tray.Visible = $false; $tray.Dispose()
    $form.Close()
})
$miExit.add_Click({
    if ($hotkey) { try { $hotkey.Dispose() } catch { } }
    $tray.Visible = $false; $tray.Dispose()
    $form.Close()
})

# 提示一下
$tray.BalloonTipTitle = 'Screenshot floating button running'
$tray.BalloonTipText  = "Click camera to capture | Middle-drag to move | Right-click for menu | Hotkey: Ctrl+Alt+Shift+S"
$tray.BalloonTipIcon  = [System.Windows.Forms.ToolTipIcon]::Info
$tray.ShowBalloonTip(2500)

# --- 自动 Pin 到所有虚拟桌面 (跨 W365 Switch / 普通虚拟桌面) ---
# 依赖 MScholtes 的 VirtualDesktop 模块 (Install-Module VirtualDesktop -Scope CurrentUser)
# 注意：必须等窗口被 shell 注册为 ApplicationView 才能 pin (否则 0x8002802B)，
# 所以挂在 Shown 事件 + 延迟 + 重试。
$logFile = Join-Path $SaveFolder 'screenshot-tool.log'
$script:pinDone = $false

$pinAction = {
    if ($script:pinDone) { return }
    try {
        Import-Module VirtualDesktop -ErrorAction Stop
    } catch {
        "[$(Get-Date -Format 'HH:mm:ss')] Import-Module failed: $($_.Exception.Message)" | Out-File $logFile -Append
        return
    }
    $hwnd = $form.Handle
    "[$(Get-Date -Format 'HH:mm:ss')] pin attempt, hwnd=$hwnd" | Out-File $logFile -Append

    # 先尝试 Pin-Window，失败则 fallback 到 Pin-Application
    $ok = $false
    for ($i = 1; $i -le 5; $i++) {
        try {
            Pin-Window -Hwnd $hwnd -ErrorAction Stop
            $pinned = Test-WindowPinned -Hwnd $hwnd
            "[$(Get-Date -Format 'HH:mm:ss')] try $i Pin-Window OK pinned=$pinned" | Out-File $logFile -Append
            if ($pinned) { $ok = $true; break }
        } catch {
            "[$(Get-Date -Format 'HH:mm:ss')] try $i Pin-Window err: $($_.Exception.Message)" | Out-File $logFile -Append
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ok) {
        try {
            Pin-Application -Hwnd $hwnd -ErrorAction Stop
            "[$(Get-Date -Format 'HH:mm:ss')] Pin-Application OK" | Out-File $logFile -Append
            $ok = $true
        } catch {
            "[$(Get-Date -Format 'HH:mm:ss')] Pin-Application err: $($_.Exception.Message)" | Out-File $logFile -Append
        }
    }

    $script:pinDone = $true
    $msg = if ($ok) { 'pinned to all desktops' } else { 'pin failed - see log' }
    $tray.BalloonTipTitle = 'Virtual desktop pin'
    $tray.BalloonTipText  = $msg
    $tray.BalloonTipIcon  = if ($ok) { [System.Windows.Forms.ToolTipIcon]::Info } else { [System.Windows.Forms.ToolTipIcon]::Warning }
    $tray.ShowBalloonTip(3500)
}

# 用 Timer 延迟 1.5 秒触发 (Shown 事件不一定足够晚)
$pinTimer = New-Object System.Windows.Forms.Timer
$pinTimer.Interval = 1500
$pinTimer.add_Tick({
    $pinTimer.Stop()
    & $pinAction
})
$form.add_Shown({ $pinTimer.Start() })

# --- 持续重置 TopMost (每 500ms 把窗口挤回最顶层) ---
# 这是关键：全屏 RDP/W365 客户端会反复抢顶，必须周期性 SetWindowPos(HWND_TOPMOST)
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 500
$timer.add_Tick({
    if ($form.Visible -and $form.Handle -ne [IntPtr]::Zero) {
        try { [TopMostHelper]::Reassert($form.Handle) } catch { }
    }
})
$timer.Start()

[System.Windows.Forms.Application]::Run($form)
