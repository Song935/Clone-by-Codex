param([string]$Version = '0.1.0-preview')
. "$PSScriptRoot/common.ps1"
Push-Location $ProjectRoot
try {
    $bundle = Join-Path $ProjectRoot "artifacts/CloneCad-$Version-win-x64"
    if (Test-Path -LiteralPath $bundle) { throw "Package directory already exists: $bundle. Use a new version or inspect it before replacing." }
    $app = Join-Path $bundle 'app'
    Invoke-CadDotnet publish src/CloneCad.Desktop -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $app
    Invoke-CadDotnet publish src/CloneCad.Mcp -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o (Join-Path $app 'mcp')
    Copy-Item -LiteralPath LICENSE,THIRD_PARTY_NOTICES.md,README.md -Destination $bundle
    Copy-Item -LiteralPath docs -Destination $bundle -Recurse
    Copy-Item -LiteralPath tools-v1.json -Destination $bundle
    Copy-Item -LiteralPath scripts/Install-CloneCad.ps1,scripts/Install.cmd -Destination $bundle
    $licenseRoot = Join-Path $bundle 'licenses'
    New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
    $assetsFiles = 'src/CloneCad.Desktop/obj/project.assets.json','src/CloneCad.Mcp/obj/project.assets.json'
    $packageNames = @{}
    foreach ($assetsFile in $assetsFiles) {
        $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
        foreach ($library in $assets.libraries.PSObject.Properties) { if ($library.Value.type -eq 'package') { $packageNames[$library.Name.ToLowerInvariant()] = $true } }
    }
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    foreach ($packageName in $packageNames.Keys) {
        $packagePath = Join-Path $nugetRoot $packageName
        $target = Join-Path $licenseRoot $packageName
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($notice in (Get-ChildItem -LiteralPath $packagePath -Recurse -File | Where-Object { $_.Name -match '^(license|licence|copying|notice|third.?party)' -or $_.Extension -eq '.nuspec' })) {
            $relative = $notice.FullName.Substring($packagePath.Length).TrimStart('\','/')
            $destination = Join-Path $target $relative
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $notice.FullName -Destination $destination
        }
    }
    Compress-Archive -Path "$bundle/*" -DestinationPath "$bundle.zip" -CompressionLevel Optimal
    Get-Item -LiteralPath "$bundle.zip" | Select-Object FullName,Length
}
finally { Pop-Location }
