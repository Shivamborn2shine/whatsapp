[CmdletBinding()]
param([switch]$SkipTests)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$binDir = Join-Path $repoRoot "bin"
if (-not (Test-Path $binDir)) {
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
}

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "64-bit .NET 4.8 C# compiler not found at $csc"
}

Get-Process -Name "ScanDrop" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

$outExe = Join-Path $binDir "ScanDrop.exe"
$sources = @(
    Join-Path $repoRoot "native\TunnelManager.cs"
    Join-Path $repoRoot "native\LocalHttpServer.cs"
    Join-Path $repoRoot "native\StationForm.cs"
    Join-Path $repoRoot "native\Program.cs"
)

Write-Host "=== Compiling ScanDrop.exe (Native Windows Local SSD & Tunnel Station) ===" -ForegroundColor Cyan
& $csc /nologo /target:winexe /platform:x64 /optimize+ /out:$outExe `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    /r:System.Web.Extensions.dll `
    /r:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -Path $outExe -Destination (Join-Path $repoRoot "ScanDrop.exe") -Force
Write-Host "Compiled -> $outExe and root ScanDrop.exe" -ForegroundColor Green

if (-not $SkipTests) {
    Write-Host "=== Running ScanDrop.exe --self-test ===" -ForegroundColor Cyan
    & (Join-Path $repoRoot "ScanDrop.exe") --self-test | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Self-test failed with exit code $LASTEXITCODE"
    }
}
