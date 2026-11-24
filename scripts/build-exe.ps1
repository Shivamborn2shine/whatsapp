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
    Join-Path $repoRoot "native\Core\Models.cs"
    Join-Path $repoRoot "native\Core\SecretProtector.cs"
    Join-Path $repoRoot "native\Core\AppLogger.cs"
    Join-Path $repoRoot "native\Core\ShopPairingClient.cs"
    Join-Path $repoRoot "native\Storage\StoragePaths.cs"
    Join-Path $repoRoot "native\Storage\SqliteDatabase.cs"
    Join-Path $repoRoot "native\Storage\StorageManager.cs"
    Join-Path $repoRoot "native\Transfers\FileValidator.cs"
    Join-Path $repoRoot "native\Transfers\UploadSecurityGuard.cs"
    Join-Path $repoRoot "native\Transfers\TransferService.cs"
    Join-Path $repoRoot "native\Conversion\DocxEditor.cs"
    Join-Path $repoRoot "native\Conversion\ConversionEngine.cs"
    Join-Path $repoRoot "native\Api\HttpHelpers.cs"
    Join-Path $repoRoot "native\Api\PublicUploadServer.cs"
    Join-Path $repoRoot "native\Api\LocalAdminServer.cs"
    Join-Path $repoRoot "native\Ui\FirstRunWizardForm.cs"
    Join-Path $repoRoot "native\AutoUpdater.cs"
    Join-Path $repoRoot "native\TunnelManager.cs"
    Join-Path $repoRoot "native\StationForm.cs"
    Join-Path $repoRoot "native\Program.cs"
)

Write-Host "=== Compiling ScanDrop.exe (PRD v2.0 Local-First Windows Station) ===" -ForegroundColor Cyan
$iconFile = Join-Path $repoRoot "assets\scandrop.ico"
$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/out:$outExe",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.IO.Compression.dll",
    "/r:System.IO.Compression.FileSystem.dll",
    "/r:System.Security.dll",
    "/r:System.Web.Extensions.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Xml.dll",
    "/r:System.Xml.Linq.dll"
)
if (Test-Path $iconFile) {
    $cscArgs += "/win32icon:$iconFile"
}
& $csc @cscArgs $sources

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
