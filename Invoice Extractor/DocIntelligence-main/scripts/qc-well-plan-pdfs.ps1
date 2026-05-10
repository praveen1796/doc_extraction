# QC: POST each SmartPlan PDF to extraction API, save JSON, validate shape.
# Requires: curl.exe, backend https://localhost:61181 (dev cert), Auth disabled or pass -ApiKey.
param(
    [string]$ApiBase = "https://localhost:61181",
    [string]$DocsDir = "$PSScriptRoot\..\Frontend\SmartPlanDocuments",
    [string]$OutDir = "$PSScriptRoot\..\qc-reports\well-plan",
    [int]$MaxTimeSec = 900,
    [string]$ApiKey = "",
    [switch]$Resume
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$pdfs = Get-ChildItem -LiteralPath $DocsDir -File -Filter "*.pdf" | Sort-Object Length
if ($pdfs.Count -eq 0) {
    Write-Error "No PDFs in $DocsDir"
}

$summary = @()
$hdr = @()
if ($ApiKey) { $hdr = @("-H", "X-Api-Key: $ApiKey") }

foreach ($pdf in $pdfs) {
    $safeName = [regex]::Replace($pdf.BaseName, '[^\w\-\.]+', '_')
    if ($safeName.Length -gt 120) { $safeName = $safeName.Substring(0, 120) }
    $jsonPath = Join-Path $OutDir "$safeName.json"
    $errPath = Join-Path $OutDir "$safeName.error.txt"

    Write-Host "`n=== $($pdf.Name) ($([math]::Round($pdf.Length/1MB, 2)) MB) ===" -ForegroundColor Cyan

    if ($Resume -and (Test-Path -LiteralPath $jsonPath)) {
        try {
            $existing = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($existing.request_id -and $existing.document_type -eq "well_plan" -and $existing.status) {
                $wc = 0
                if ($existing.data -and $existing.data.wells) { $wc = @($existing.data.wells).Count }
                $okS = ($existing.status -eq "Success" -or $existing.status -eq "PartialSuccess")
                $okR = $okS -and ($wc -gt 0)
                $summary += [pscustomobject]@{
                    File = $pdf.Name; Ok = $okR; Status = "(skipped) $($existing.status) wells=$wc"; Wells = $wc; Sec = 0; RequestId = $existing.request_id
                }
                Write-Host "SKIP resume: existing JSON" -ForegroundColor DarkGray
                continue
            }
        } catch { /* re-extract */ }
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $code = & curl.exe @hdr -sk --max-time $MaxTimeSec -X POST `
        "$ApiBase/api/v1/extraction/extract" `
        -F "file=@$($pdf.FullName);type=application/pdf" `
        -F "documentType=well_plan" `
        -o $jsonPath `
        -w "%{http_code}"
    $sw.Stop()
    $code = "$code".Trim()

    if ($code -ne "200") {
        $errBody = Get-Content -LiteralPath $jsonPath -Raw -ErrorAction SilentlyContinue
        if ($errBody) { Set-Content -LiteralPath $errPath -Value $errBody -Encoding UTF8 }
        $summary += [pscustomobject]@{ File = $pdf.Name; Ok = $false; Status = "HTTP $code"; Wells = $null; Sec = [math]::Round($sw.Elapsed.TotalSeconds, 1); RequestId = $null }
        Write-Host "HTTP $code ($([math]::Round($sw.Elapsed.TotalSeconds,1))s)" -ForegroundColor Red
        continue
    }

    try {
        $j = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        $summary += [pscustomobject]@{ File = $pdf.Name; Ok = $false; Status = "invalid JSON"; Wells = $null; Sec = [math]::Round($sw.Elapsed.TotalSeconds, 1); RequestId = $null }
        Write-Host "Invalid JSON" -ForegroundColor Red
        continue
    }

    $wellCount = 0
    if ($j.data -and $j.data.wells) { $wellCount = @($j.data.wells).Count }

    $okStatus = ($j.status -eq "Success" -or $j.status -eq "PartialSuccess")
    $ok = $okStatus -and ($wellCount -gt 0)
    $status = "$($j.status) wells=$wellCount"
    if ($j.error) {
        $status += " err=$($j.error.code)"
        Set-Content -LiteralPath $errPath -Value ($j.error | ConvertTo-Json -Depth 6) -Encoding UTF8
    }

    $summary += [pscustomobject]@{
        File = $pdf.Name
        Ok = $ok
        Status = $status
        Wells = $wellCount
        Sec = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        RequestId = $j.request_id
    }
    Write-Host "Result: $status ($($summary[-1].Sec)s)" -ForegroundColor $(if ($ok) { "Green" } else { "Yellow" })
    # Checkpoint so interruption still leaves partial QC
    $partialPath = Join-Path $OutDir "_summary.partial.json"
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $partialPath -Encoding UTF8
}

$summaryPath = Join-Path $OutDir "_summary.json"
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host "`nSummary -> $summaryPath" -ForegroundColor Green
$summary | Format-Table -AutoSize
