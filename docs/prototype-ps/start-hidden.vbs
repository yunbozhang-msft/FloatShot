' 隐藏窗口启动 screenshot-hotkey.ps1
' 双击此文件即可常驻后台 (无 PowerShell 黑窗)
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
ps1 = scriptDir & "\screenshot-hotkey.ps1"

Set sh = CreateObject("WScript.Shell")
sh.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & ps1 & """", 0, False
