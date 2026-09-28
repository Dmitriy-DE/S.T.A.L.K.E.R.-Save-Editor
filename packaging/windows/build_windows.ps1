# Windows packages: portable zip (the application folder) and the Inno Setup installer.
# Needs bash (Git for Windows), Python 3 and the MSVC environment for the Kraken library.
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path "$PSScriptRoot\..\..").Path
$Dist = "$Root\dist"
$App = "$PSScriptRoot\dist"

New-Item -ItemType Directory -Force -Path $Dist | Out-Null
& bash "$Root/packaging/publish_app.sh" win-x64 ($App -replace '\\', '/') $Version
if ($LASTEXITCODE -ne 0) { throw "publish_app.sh failed" }

Compress-Archive -Path "$App\*" -DestinationPath "$Dist\StalkerSaveEditor-v$Version-windows-x64.zip" -Force
Write-Host "Portable archive: $Dist\StalkerSaveEditor-v$Version-windows-x64.zip"

$InnoCompiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (!(Test-Path $InnoCompiler)) { throw "Inno Setup 6 (ISCC.exe) is required for the installer" }
& $InnoCompiler "/DMyAppVersion=$Version" "$PSScriptRoot\installer.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
