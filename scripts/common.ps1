$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path $PSScriptRoot -Parent
function Get-CadDotnet {
    $localSdk = Join-Path (Split-Path $ProjectRoot -Parent) '.tools\dotnet10\dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { return $localSdk }
    $sdkCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $sdkCommand) { throw 'Install the .NET 10 SDK first.' }
    return $sdkCommand.Source
}
$CadDotnet = Get-CadDotnet
$env:DOTNET_ROOT = Split-Path $CadDotnet -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
function Invoke-CadDotnet {
    & $CadDotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed (exit $LASTEXITCODE)." }
}
