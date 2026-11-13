[CmdletBinding()]
param([switch]$SkipTests)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$binDir = Join-Path $repoRoot "bin"
$dlDir = Join-Path $repoRoot "downloads"
if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }
if (-not (Test-Path $dlDir)) { New-Item -ItemType Directory -Path $dlDir -Force | Out-Null }

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "64-bit .NET 4.8 C# compiler not found at $csc"
}

Get-Process -Name "ScanDrop" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

$outExe = Join-Path $binDir "ScanDrop.exe"
$sources = @(
    Join-Path $repoRoot "native\AutoUpdater.cs"
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

$rootExe = Join-Path $repoRoot "ScanDrop.exe"
$dlExe = Join-Path $dlDir "ScanDrop.exe"
Copy-Item -Path $outExe -Destination $rootExe -Force
Copy-Item -Path $outExe -Destination $dlExe -Force

# Compute SHA-256 and stamp version.json cleanly
$hash = (Get-FileHash -Path $dlExe -Algorithm SHA256).Hash.ToLowerInvariant()
$versionFile = Join-Path $repoRoot "VERSION"
$manifestFile = Join-Path $repoRoot "version.json"
$verText = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { "3.1.0" }
if (Test-Path $manifestFile) {
    $manifest = Get-Content $manifestFile -Raw | ConvertFrom-Json
    $manifest.version = $verText
    $manifest.sha256 = $hash
    $manifest.releaseDate = (Get-Date).ToString("yyyy-MM-dd")
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestFile -Encoding UTF8
}

Write-Host "Compiled -> $outExe, $rootExe, and $dlExe (SHA256: $hash)" -ForegroundColor Green

if (-not $SkipTests) {
    Write-Host "=== Running ScanDrop.exe --self-test ===" -ForegroundColor Cyan
    & $rootExe --self-test | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Self-test failed with exit code $LASTEXITCODE"
    }
}
