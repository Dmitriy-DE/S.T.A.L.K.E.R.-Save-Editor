# PowerShell Windows Packaging Script
param(
    [string]$Version = "1.4.0"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path "$PSScriptRoot\..\..").Path
$Dist = "$Root\dist"
$WinDist = "$PSScriptRoot\dist"

Write-Host "=== Building S.T.A.L.K.E.R. Save Editor for Windows v$Version (win-x64) ===" -ForegroundColor Green

# 1. Clean directories
if (Test-Path $WinDist) { Remove-Item -Recurse -Force $WinDist }
New-Item -ItemType Directory -Force -Path $WinDist | Out-Null
if (!(Test-Path $Dist)) { New-Item -ItemType Directory -Force -Path $Dist | Out-Null }

# 2. Publish single-file executable
# Suppress IL3000 (Assembly.Location warning in SteamWorkerProcessRunner) for single-file publish while keeping TreatWarningsAsErrors=true
dotnet publish "$Root\src\StalkerSaveEditor.Desktop\StalkerSaveEditor.Desktop.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:NoWarn="IL3000" `
    -o $WinDist

Rename-Item -Path "$WinDist\StalkerSaveEditor.Desktop.exe" -NewName "StalkerSaveEditor.exe"

# Copy standalone .exe to dist
Copy-Item "$WinDist\StalkerSaveEditor.exe" -Destination "$Dist\StalkerSaveEditor-v$Version-win-x64.exe"
Write-Host "Standalone Windows binary created: $Dist\StalkerSaveEditor-v$Version-win-x64.exe" -ForegroundColor Cyan

# Bundle companion mod next to binary
Write-Host "Bundling companion mod..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path "$WinDist\mods" | Out-Null
Copy-Item -Recurse -Force "$Root\mods\companion" -Destination "$WinDist\mods\companion"

# 3. Compile Inno Setup installer if available
$InnoCompiler = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (Test-Path $InnoCompiler) {
    Write-Host "Compiling Inno Setup installer..."
    & $InnoCompiler "/DMyAppVersion=$Version" "$PSScriptRoot\installer.iss"
    Write-Host "Setup installer created in $Dist" -ForegroundColor Green
} else {
    Write-Host "Inno Setup compiler (ISCC.exe) not found; skipping installer generation." -ForegroundColor Yellow
}
