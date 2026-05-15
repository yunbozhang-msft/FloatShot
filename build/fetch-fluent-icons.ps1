# Download Microsoft Fluent UI System Icons (MIT) for FloatShot
# https://github.com/microsoft/fluentui-system-icons
[CmdletBinding()]
param(
    [string]$DestDir = (Join-Path $PSScriptRoot '..\src\FloatShot\Resources\fluent')
)

$base = 'https://raw.githubusercontent.com/microsoft/fluentui-system-icons/main/assets'

# 短名 -> @(folder, filename)
$assets = [ordered]@{
    'camera'      = @('Camera',                'ic_fluent_camera_28_filled.svg')
    'pen'         = @('Pen',                   'ic_fluent_pen_24_filled.svg')
    'pin'         = @('Pin',                   'ic_fluent_pin_24_filled.svg')
    'copy'        = @('Copy',                  'ic_fluent_copy_24_filled.svg')
    'save'        = @('Save',                  'ic_fluent_save_24_filled.svg')
    'dismiss'     = @('Dismiss',               'ic_fluent_dismiss_24_filled.svg')
    'settings'    = @('Settings',              'ic_fluent_settings_24_filled.svg')
    'folder'      = @('Folder Open',           'ic_fluent_folder_open_24_filled.svg')
    'fullscreen'  = @('Full Screen Maximize',  'ic_fluent_full_screen_maximize_24_filled.svg')
    'window'      = @('Window',                'ic_fluent_window_24_filled.svg')
    'zoom_in'     = @('Zoom In',               'ic_fluent_zoom_in_24_filled.svg')
    'zoom_out'    = @('Zoom Out',              'ic_fluent_zoom_out_24_filled.svg')
    'arrow_reset' = @('Arrow Reset',           'ic_fluent_arrow_reset_24_filled.svg')
    'info'        = @('Info',                  'ic_fluent_info_24_filled.svg')
    'sign_out'    = @('Sign Out',              'ic_fluent_sign_out_24_filled.svg')
}

if (-not (Test-Path $DestDir)) { New-Item -ItemType Directory -Path $DestDir -Force | Out-Null }

foreach ($k in $assets.Keys) {
    $folder = [System.Uri]::EscapeDataString($assets[$k][0]).Replace('%20', '%20')
    # EscapeDataString 已编码空格, 但 GitHub raw 接受 %20
    $file   = $assets[$k][1]
    $url    = "$base/$folder/SVG/$file"
    $out    = Join-Path $DestDir "$k.svg"
    try {
        Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing -ErrorAction Stop
        Write-Host "OK  $k <- $file"
    } catch {
        Write-Host "ERR $k <- $url" -ForegroundColor Red
        Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Get-ChildItem $DestDir | Sort-Object Name | Format-Table Name, Length
