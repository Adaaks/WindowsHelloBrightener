# Builds HelloBrightener.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# Usage:
#   .\build.ps1            -> builds HelloBrightener.exe next to this script
#   .\build.ps1 -Install   -> also runs it, which installs it to %LOCALAPPDATA%\HelloBrightener and adds it to startup
param([switch]$Install)
$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

$exe = Join-Path $PSScriptRoot 'HelloBrightener.exe'
& $csc /nologo /target:winexe /optimize "/out:$exe" (Join-Path $PSScriptRoot 'src\HelloBrightener.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built $exe"

if ($Install) {
    Start-Process $exe
    Write-Host 'Installing... look for the shield icon in the system tray.'
}
