[CmdletBinding()]
param(
    [string]$AdminBase = "http://127.0.0.1:3838",
    [switch]$KeepTestSubmission
)

$ErrorActionPreference = "Stop"

function Assert-True([bool]$Cond, [string]$Msg) {
    if (-not $Cond) { throw "ASSERTION FAILED: $Msg" }
    Write-Host "  [PASS] $Msg" -ForegroundColor Green
}

function Get-HttpStatus([string]$Url, [hashtable]$Headers = @{}) {
    try {
        $resp = Invoke-WebRequest -Uri $Url -Headers $Headers -UseBasicParsing -TimeoutSec 10
        return [int]$resp.StatusCode
    } catch {
        if ($_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode
        }
        return -1
    }
}

Write-Host "=== Scan Drop PRD v2.0 Live End-to-End Acceptance Test ===" -ForegroundColor Cyan

# 1. Wait for live tunnel on running ScanDrop.exe
$health = $null
for ($i = 0; $i -lt 20; $i++) {
    try {
        $h = Invoke-RestMethod -Uri "$AdminBase/api/health" -TimeoutSec 4
        if ($h.tunnelOnline -and $h.tunnelUrl) { $health = $h; break }
    } catch {}
    Start-Sleep -Seconds 2
}
Assert-True ($null -ne $health) "Local admin /api/health reports live tunnel ($($health.tunnelUrl))"
$tunnelBase = $health.tunnelUrl.TrimEnd('/')
$pubHeaders = @{}
Start-Sleep -Seconds 2

# 2. Public tunnel health & zero info leak
$pubHealthRaw = (Invoke-WebRequest -Uri "$tunnelBase/api/health" -Headers $pubHeaders -UseBasicParsing -TimeoutSec 12).Content
$pubHealth = $pubHealthRaw | ConvertFrom-Json
Assert-True ($pubHealth.ok -eq $true) "Public tunnel /api/health reachable over internet ($tunnelBase)"
Assert-True ($pubHealthRaw -notmatch "ProgramData|AppData|Business 2027") "Public /api/health leaks zero local filesystem paths"

# 3. Strict public/admin isolation over live internet
foreach ($blockedPath in @("/workspace/", "/api/state", "/api/file?id=none", "/api/storage", "/api/settings")) {
    $st = Get-HttpStatus "$tunnelBase$blockedPath" $pubHeaders
    Assert-True ($st -eq 404) "Public tunnel blocks $blockedPath with HTTP 404 (got $st)"
}

# 4. Real PDF + PNG chunked upload over live internet tunnel
$pdfBytes = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n1 0 obj <<>> endobj`ntrailer <<>>`n%%EOF`n")
$pngPath = Join-Path (Split-Path -Parent $PSScriptRoot) "data\test_files\customer_id_card.png"
if (Test-Path $pngPath) {
    $pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
} else {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap(16, 16)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

$idemKey = "e2e_" + [Guid]::NewGuid().ToString("N")
$initBody = @{
    customerName = "PRD E2E Verifier"
    customerNote = "Print 2 copies color"
    idempotencyKey = $idemKey
    files = @(
        @{ clientFileId = "c0"; name = "e2e_invoice.pdf"; size = $pdfBytes.Length; contentType = "application/pdf" },
        @{ clientFileId = "c1"; name = "e2e_id_card.png"; size = $pngBytes.Length; contentType = "image/png" }
    )
} | ConvertTo-Json -Depth 5

$initResp = Invoke-RestMethod -Method Post -Uri "$tunnelBase/api/upload/sessions" -Headers $pubHeaders -ContentType "application/json" -Body $initBody -TimeoutSec 15
Assert-True ($initResp.success -and $initResp.sessionId -and $initResp.uploadToken) "Created live upload session ($($initResp.sessionId))"

$f0Id = $initResp.files[0].fileId
$f1Id = $initResp.files[1].fileId

$c0Hdrs = @{
    "X-Session-Id" = $initResp.sessionId; "X-Upload-Token" = $initResp.uploadToken
    "X-File-Id" = $f0Id; "X-Chunk-Index" = "0"; "X-Byte-Offset" = "0"
}
$null = Invoke-RestMethod -Method Post -Uri "$tunnelBase/api/upload/chunk" -Headers $c0Hdrs -ContentType "application/octet-stream" -Body $pdfBytes -TimeoutSec 20

$c1Hdrs = @{
    "X-Session-Id" = $initResp.sessionId; "X-Upload-Token" = $initResp.uploadToken
    "X-File-Id" = $f1Id; "X-Chunk-Index" = "0"; "X-Byte-Offset" = "0"
}
$null = Invoke-RestMethod -Method Post -Uri "$tunnelBase/api/upload/chunk" -Headers $c1Hdrs -ContentType "application/octet-stream" -Body $pngBytes -TimeoutSec 20

$cmpResp = Invoke-RestMethod -Method Post -Uri "$tunnelBase/api/upload/complete" -Headers $pubHeaders -ContentType "application/json" -Body (@{
    sessionId = $initResp.sessionId; uploadToken = $initResp.uploadToken
} | ConvertTo-Json) -TimeoutSec 20
$receipt = $cmpResp.receiptCode
Assert-True ($cmpResp.success -and $receipt.Length -eq 6) "Committed PDF + PNG over live tunnel -> 6-char receipt: $receipt"

# 5. Idempotency duplicate protection over live tunnel
$dupResp = Invoke-RestMethod -Method Post -Uri "$tunnelBase/api/upload/sessions" -Headers $pubHeaders -ContentType "application/json" -Body $initBody -TimeoutSec 15
Assert-True ($dupResp.alreadyCompleted -eq $true -and $dupResp.receiptCode -eq $receipt) "Duplicate submission idempotency returned existing receipt ($receipt)"

# 6. Magic-byte EXE-as-PDF rejection over live tunnel
$fakeExe = [byte[]](0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00)
$badInit = Invoke-RestMethod -Method Post -Uri "$tunnelBase/api/upload/sessions" -Headers $pubHeaders -ContentType "application/json" -Body (@{
    customerName = "BadActor"
    files = @(@{ clientFileId = "b0"; name = "spoofed.pdf"; size = $fakeExe.Length; contentType = "application/pdf" })
} | ConvertTo-Json -Depth 4) -TimeoutSec 15
$badHdrs = @{
    "X-Session-Id" = $badInit.sessionId; "X-Upload-Token" = $badInit.uploadToken
    "X-File-Id" = $badInit.files[0].fileId; "X-Chunk-Index" = "0"; "X-Byte-Offset" = "0"
}
$null = Invoke-RestMethod -Method Post -Uri "$tunnelBase/api/upload/chunk" -Headers $badHdrs -ContentType "application/octet-stream" -Body $fakeExe -TimeoutSec 15
$badStatus = 0
try {
    $null = Invoke-WebRequest -Method Post -Uri "$tunnelBase/api/upload/complete" -Headers $pubHeaders -ContentType "application/json" -Body (@{
        sessionId = $badInit.sessionId; uploadToken = $badInit.uploadToken
    } | ConvertTo-Json) -UseBasicParsing -TimeoutSec 15
    $badStatus = 200
} catch {
    if ($_.Exception.Response) { $badStatus = [int]$_.Exception.Response.StatusCode }
}
Assert-True ($badStatus -eq 400) "Magic-byte validation rejected PE/MZ EXE disguised as .pdf over live tunnel (HTTP 400)"

# 7. Verify receipt appears in local Admin /api/state + convert PNG -> PDF
$state = Invoke-RestMethod -Uri "$AdminBase/api/state" -TimeoutSec 10
$found = $state.state.submissions | Where-Object { $_.receiptCode -eq $receipt } | Select-Object -First 1
Assert-True ($null -ne $found -and $found.files.Count -eq 2) "Local Admin /api/state displays committed submission $receipt with 2 files"

$pngFileId = ($found.files | Where-Object { $_.originalName -eq "e2e_id_card.png" } | Select-Object -First 1).id
$conv = Invoke-RestMethod -Method Post -Uri "$AdminBase/api/file/convert-pdf" -ContentType "application/json" -Body (@{ fileId = $pngFileId } | ConvertTo-Json) -TimeoutSec 15
Assert-True ($conv.success -and $conv.sizeBytes -gt 100) "Converted uploaded PNG -> PDF locally ($($conv.outputFilename), $($conv.sizeBytes) B)"

# 8. Cleanup test submission unless -KeepTestSubmission
if (-not $KeepTestSubmission) {
    $null = Invoke-RestMethod -Method Post -Uri "$AdminBase/api/submission/delete" -ContentType "application/json" -Body (@{ submissionIds = @($receipt) } | ConvertTo-Json) -TimeoutSec 10
    Write-Host "  [CLEAN] Removed temporary E2E submission $receipt" -ForegroundColor DarkGray
}

Write-Host "`nALL LIVE END-TO-END PRD v2.0 ACCEPTANCE TESTS PASSED!" -ForegroundColor Green
