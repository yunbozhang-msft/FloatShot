[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$InnoSetupCompiler
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\FloatShot\FloatShot.csproj'
$publishDir = Join-Path $root 'publish'
$installerScript = Join-Path $root 'installer\FloatShot.iss'

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "Publishing FloatShot..."
dotnet publish $project -c $Configuration -r $Runtime -o $publishDir

if (-not $InnoSetupCompiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $InnoSetupCompiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $InnoSetupCompiler -or -not (Test-Path $InnoSetupCompiler)) {
    throw "Inno Setup compiler was not found. Install Inno Setup 6 or pass -InnoSetupCompiler <path-to-ISCC.exe>."
}

Write-Host "Building installer..."
& $InnoSetupCompiler $installerScript

Write-Host "Done. Installer output:"
Get-ChildItem (Join-Path $root 'installer\Output') -Filter '*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 5 FullName, Length, LastWriteTime