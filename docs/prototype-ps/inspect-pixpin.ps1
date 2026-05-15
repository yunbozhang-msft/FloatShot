# 抓 PixPin 所有可见窗口的样式 + 类名 + 标题，用于复制其"跨虚拟显示器"诀窍
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class W {
    public delegate bool EnumDelegate(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumDelegate lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
}
"@

$pixpinPids = Get-Process -Name PixPin -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }
if (-not $pixpinPids) {
    Write-Host "PixPin process not running. Open PixPin and pin a screenshot first." -ForegroundColor Red
    return
}
Write-Host "PixPin PIDs: $($pixpinPids -join ', ')" -ForegroundColor Cyan

$results = @()
$cb = [W+EnumDelegate]{
    param($h, $l)
    if ([W]::IsWindowVisible($h)) {
        $procId = 0
        [W]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
        if ($pixpinPids -contains $procId) {
            $sb = New-Object System.Text.StringBuilder 256
            [W]::GetWindowText($h, $sb, 256) | Out-Null
            $title = $sb.ToString()
            $cls = New-Object System.Text.StringBuilder 256
            [W]::GetClassName($h, $cls, 256) | Out-Null
            $style   = [W]::GetWindowLong($h, [W]::GWL_STYLE)
            $exstyle = [W]::GetWindowLong($h, [W]::GWL_EXSTYLE)
            $script:results += [PSCustomObject]@{
                Hwnd    = ('0x{0:X}' -f $h.ToInt64())
                PID     = $procId
                Class   = $cls.ToString()
                Title   = $title
                Style   = ('0x{0:X8}' -f $style)
                ExStyle = ('0x{0:X8}' -f $exstyle)
            }
        }
    }
    return $true
}
[W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

$results | Format-Table -AutoSize

# 解析 ExStyle，标出哪些位被设了 (我们关心的几个)
Write-Host "`n--- ExStyle decoded (relevant flags) ---" -ForegroundColor Cyan
$flags = @{
    'WS_EX_TOPMOST'        = 0x00000008
    'WS_EX_TRANSPARENT'    = 0x00000020
    'WS_EX_TOOLWINDOW'     = 0x00000080
    'WS_EX_LAYERED'        = 0x00080000
    'WS_EX_NOACTIVATE'     = 0x08000000
    'WS_EX_APPWINDOW'      = 0x00040000
    'WS_EX_COMPOSITED'     = 0x02000000
}
foreach ($r in $results) {
    $ex = [Convert]::ToInt32($r.ExStyle, 16)
    $set = @()
    foreach ($k in $flags.Keys) { if ($ex -band $flags[$k]) { $set += $k } }
    Write-Host "$($r.Hwnd) [$($r.Class)] '$($r.Title)' -> $($set -join ', ')"
}
