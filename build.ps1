# Builds HelloBrightener.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# Usage:
#   .\build.ps1            -> builds HelloBrightener.exe next to this script
#   .\build.ps1 -Install   -> also installs it to %LOCALAPPDATA%\HelloBrightener, adds it to startup and runs it
param([switch]$Install)
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

$exe = Join-Path $PSScriptRoot 'HelloBrightener.exe'
& $csc /nologo /target:winexe /optimize "/out:$exe" (Join-Path $PSScriptRoot 'src\HelloBrightener.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built $exe"

if ($Install) {
    $dir = Join-Path $env:LOCALAPPDATA 'HelloBrightener'
    New-Item -ItemType Directory -Force $dir | Out-Null
    Get-Process HelloBrightener -ErrorAction SilentlyContinue | Stop-Process
    Start-Sleep -Milliseconds 300
    Copy-Item $exe $dir -Force

    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Startup')) 'HelloBrightener.lnk'))
    $lnk.TargetPath = Join-Path $dir 'HelloBrightener.exe'
    $lnk.Save()

    Start-Process (Join-Path $dir 'HelloBrightener.exe')
    Write-Host "Installed to $dir and added to startup. Look for the shield icon in the system tray."
}
