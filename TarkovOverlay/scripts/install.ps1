# Builds TarkovOverlay (Release) and creates a "Tarkov Overlay" shortcut on the Desktop.
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\install.ps1 [-NoShortcut] [-Run]
param([switch]$NoShortcut, [switch]$Run)
$ErrorActionPreference = "Stop"
$proj = Resolve-Path (Join-Path $PSScriptRoot "..")

if (-not (Test-Path (Join-Path $proj "tessdata\rus.traineddata")) -or -not (Test-Path (Join-Path $proj "tessdata_fast\rus.traineddata"))) {
    Write-Host "Downloading OCR language data..."
    & (Join-Path $PSScriptRoot "download-tessdata.ps1")
}

$running = Get-Process TarkovOverlay -ErrorAction SilentlyContinue
if ($running) { Write-Host "Stopping running overlay..."; $running | Stop-Process -Force; Start-Sleep -Milliseconds 500 }

Write-Host "Building Release..."
dotnet build (Join-Path $proj "TarkovOverlay.csproj") -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$exe = Join-Path $proj "bin\Release\net8.0-windows\TarkovOverlay.exe"
if (-not $NoShortcut) {
    $lnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "Tarkov Overlay.lnk"
    $sh = New-Object -ComObject WScript.Shell
    $sc = $sh.CreateShortcut($lnk)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = Split-Path $exe
    $sc.IconLocation = "$exe,0"
    $sc.Description = "Tarkov Overlay - item prices under the game tooltip"
    $sc.Save()
    Write-Host "Shortcut created: $lnk"
}
if ($Run) { Start-Process $exe }
Write-Host "Done. Exe: $exe"
