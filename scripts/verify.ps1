param([string]$Configuration = 'Release', [switch]$PdfCheck)
. "$PSScriptRoot/common.ps1"
Push-Location $ProjectRoot
try {
    Invoke-CadDotnet build CloneCad.sln -c $Configuration
    Invoke-CadDotnet test tests/CloneCad.Tests -c $Configuration --no-build --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/test-results
    $app = Join-Path $ProjectRoot "src/CloneCad.Desktop/bin/$Configuration/net10.0-windows10.0.19041.0/CloneCad.Desktop.exe"
    $process = Start-Process -FilePath $app -ArgumentList '--smoke-test artifacts/verification' -WorkingDirectory $ProjectRoot -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { Stop-Process -Id $process.Id -Force; throw 'WPF smoke test timed out.' }
    if ($process.ExitCode -ne 0) { throw "WPF smoke test failed. See artifacts/verification/smoke-error.txt (exit $($process.ExitCode))." }
    $smoke = Get-Content -LiteralPath 'artifacts/verification/smoke-result.json' -Raw | ConvertFrom-Json
    if (-not $smoke.passed) { throw 'WPF smoke test did not report success.' }
    Invoke-CadDotnet run --project src/CloneCad.Mcp -c $Configuration --no-build -- --export-tools artifacts/tools-v1.json
    if ((Test-Path -LiteralPath 'tools-v1.json') -and [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'tools-v1.json')) -ne [System.IO.File]::ReadAllText((Join-Path $ProjectRoot 'artifacts/tools-v1.json'))) {
        throw 'Tool contract changed. Review and regenerate tools-v1.json.'
    }
    if ($PdfCheck) {
        python scripts/verify_pdf.py artifacts/verification
        if ($LASTEXITCODE -ne 0) { throw 'Independent PDF verification failed.' }
    }
    $smoke | ConvertTo-Json -Depth 5
}
finally { Pop-Location }
