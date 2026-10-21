. "$PSScriptRoot/common.ps1"
Push-Location $ProjectRoot
try {
    Invoke-CadDotnet build CloneCad.sln -c Debug
    $desktop = Join-Path $ProjectRoot 'src/CloneCad.Desktop/bin/Debug/net10.0-windows10.0.19041.0'
    $bridge = Join-Path $ProjectRoot 'src/CloneCad.Mcp/bin/Debug/net10.0'
    New-Item -ItemType Directory -Path (Join-Path $desktop 'mcp') -Force | Out-Null
    Copy-Item -Path "$bridge/*" -Destination (Join-Path $desktop 'mcp') -Recurse -Force
    & (Join-Path $desktop 'CloneCad.Desktop.exe')
}
finally { Pop-Location }
