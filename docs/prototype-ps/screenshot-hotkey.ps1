<#
.SYNOPSIS
    本地全局热键截图工具 (常驻系统托盘)。
.DESCRIPTION
    在本地电脑注册全局热键 (默认 Ctrl+Alt+Shift+S)，按下后立即截取本地全部屏幕
    并保存为 PNG。即使焦点在 Windows 365 / AVD / RDP 的全屏窗口，只要热键不
    包含 Win 键，本地 Windows 通常会先捕获 WM_HOTKEY，从而触发本地截图。
.PARAMETER SaveFolder
    截图保存目录，默认 %USERPROFILE%\Pictures\Screenshots
.PARAMETER PrimaryOnly
    指定后只截取主屏，否则截取所有显示器组成的虚拟屏幕。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\screenshot-hotkey.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\screenshot-hotkey.ps1 -SaveFolder D:\Shots -PrimaryOnly
#>
[CmdletBinding()]
param(
    [string]$SaveFolder = (Join-Path $env:USERPROFILE 'Pictures\Screenshots'),
    [switch]$PrimaryOnly
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path -LiteralPath $SaveFolder)) {
    New-Item -ItemType Directory -Path $SaveFolder -Force | Out-Null
}

# --- C# 辅助类: 注册全局热键的隐藏窗口 ---
$cs = @"
using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public class HotkeyWindow : NativeWindow, IDisposable {
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private int _id;
    public event EventHandler HotkeyPressed;

    public HotkeyWindow(uint modifiers, uint key, int id) {
        _id = id;
        CreateHandle(new CreateParams());
        if (!RegisterHotKey(Handle, _id, modifiers, key)) {
            throw new Exception("RegisterHotKey failed (error " + Marshal.GetLastWin32Error() + "). The hotkey may already be in use.");
        }
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == _id) {
            EventHandler h = HotkeyPressed;
            if (h != null) h(this, EventArgs.Empty);
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

# --- 热键定义: Ctrl + Alt + Shift + S ---
$MOD_ALT      = 0x0001
$MOD_CONTROL  = 0x0002
$MOD_SHIFT    = 0x0004
$MOD_NOREPEAT = 0x4000
$VK_S         = 0x53

$modifiers = $MOD_CONTROL -bor $MOD_ALT -bor $MOD_SHIFT -bor $MOD_NOREPEAT
$hotkeyDisplay = 'Ctrl+Alt+Shift+S'

# --- 截图函数 ---
function Invoke-Capture {
    if ($PrimaryOnly) {
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
    }
    finally {
        $g.Dispose()
        $bmp.Dispose()
    }
}

# --- 系统托盘图标 ---
$tray = New-Object System.Windows.Forms.NotifyIcon
$tray.Icon    = [System.Drawing.SystemIcons]::Application
$tray.Visible = $true
$tray.Text    = "Screenshot Hotkey [$hotkeyDisplay] -> $SaveFolder"

$menu       = New-Object System.Windows.Forms.ContextMenuStrip
$miCapture  = $menu.Items.Add('Capture now')
$miOpen     = $menu.Items.Add('Open folder')
[void]$menu.Items.Add('-')
$miExit     = $menu.Items.Add('Exit')
$tray.ContextMenuStrip = $menu

$captureAction = {
    try {
        $f = Invoke-Capture
        $tray.BalloonTipTitle = 'Screenshot saved'
        $tray.BalloonTipText  = $f
        $tray.BalloonTipIcon  = [System.Windows.Forms.ToolTipIcon]::Info
        $tray.ShowBalloonTip(1500)
    } catch {
        $tray.BalloonTipTitle = 'Screenshot failed'
        $tray.BalloonTipText  = $_.Exception.Message
        $tray.BalloonTipIcon  = [System.Windows.Forms.ToolTipIcon]::Error
        $tray.ShowBalloonTip(3000)
    }
}

$miCapture.add_Click($captureAction)
$miOpen.add_Click({ Start-Process explorer.exe $SaveFolder })

# --- 注册热键 ---
try {
    $hotkey = [HotkeyWindow]::new([uint32]$modifiers, [uint32]$VK_S, 1)
} catch {
    [System.Windows.Forms.MessageBox]::Show(
        "Failed to register hotkey $hotkeyDisplay`n`n$($_.Exception.Message)",
        'Screenshot Hotkey', 'OK', 'Error') | Out-Null
    $tray.Visible = $false
    $tray.Dispose()
    return
}
$hotkey.add_HotkeyPressed($captureAction)

$miExit.add_Click({
    try { $hotkey.Dispose() } catch { }
    $tray.Visible = $false
    $tray.Dispose()
    [System.Windows.Forms.Application]::Exit()
})

# 启动后弹一次提示，确认已运行
$tray.BalloonTipTitle = 'Screenshot Hotkey running'
$tray.BalloonTipText  = "Press $hotkeyDisplay to capture. Saving to: $SaveFolder"
$tray.BalloonTipIcon  = [System.Windows.Forms.ToolTipIcon]::Info
$tray.ShowBalloonTip(2000)

[System.Windows.Forms.Application]::Run()
