# Test all KNX Project Parser samples
# Run from repository root

$xknxSamples = "docs\samples\xknxproject"
$ownSamples = "docs\samples\own"
$parserToolPath = "backend\KnxMonitor.ParserTool"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "KNX Project Parser - Sample Test Suite" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

function Test-Project {
    param(
        [string]$Name,
        [string]$FilePath,
        [string]$Password = $null
    )

    Write-Host "`n----------------------------------------" -ForegroundColor Yellow
    Write-Host "Testing: $Name" -ForegroundColor Yellow
    Write-Host "----------------------------------------" -ForegroundColor Yellow

    if (-not (Test-Path $FilePath)) {
        Write-Host "[SKIP] missing fixture: $FilePath" -ForegroundColor DarkGray
        return
    }

    Write-Host "`n[DETECT]" -ForegroundColor Green
    & dotnet run --project $parserToolPath -- detect $FilePath

    Write-Host "`n[PARSE]" -ForegroundColor Green
    if ($Password) {
        & dotnet run --project $parserToolPath -- parse $FilePath --password $Password
    } else {
        & dotnet run --project $parserToolPath -- parse $FilePath
    }
}

# Public xknxproject samples (always tracked)
Test-Project -Name "ETS4 - No Password" `
             -FilePath (Join-Path $xknxSamples "test_project-ets4-no_password.knxproj")

Test-Project -Name "ETS4 - Password Protected (test)" `
             -FilePath (Join-Path $xknxSamples "test_project-ets4.knxproj") `
             -Password "test"

Test-Project -Name "ETS6 - Free Addressing" `
             -FilePath (Join-Path $xknxSamples "ets6_free.knxproj")

Test-Project -Name "ETS6 - Two Level Addressing" `
             -FilePath (Join-Path $xknxSamples "ets6_two_level.knxproj")

Test-Project -Name "ETS6 - Password Protected (test)" `
             -FilePath (Join-Path $xknxSamples "testprojekt-ets6.knxproj") `
             -Password "test"

# Local-only samples (skipped when not present)
Test-Project -Name "ETS5 - Large Project (own)" `
             -FilePath (Join-Path $ownSamples "myProject_ets_v5.7.7.knxproj")

Test-Project -Name "ETS5 - KNX Secure (own, affe)" `
             -FilePath (Join-Path $ownSamples "TestMitSecure_ets_v5.7.7_secure.knxproj") `
             -Password "affe"

Write-Host "`nAll tests completed!" -ForegroundColor Green
