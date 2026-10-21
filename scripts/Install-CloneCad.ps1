param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\CloneCad'),[switch]$NoShortcut)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $source 'CloneCad.Desktop.exe'))) { throw 'Extract the complete release ZIP before installing.' }
$target = [System.IO.Path]::GetFullPath($Destination)
$packagePath = [System.IO.Path]::GetFullPath($PSScriptRoot)
if ($target.StartsWith($packagePath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or $target -eq $packagePath) { throw 'Choose an installation directory outside the extracted package.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path "$source/*" -Destination $target -Recurse -Force
foreach ($notice in 'LICENSE','THIRD_PARTY_NOTICES.md','licenses','docs','tools-v1.json') {
    $path = Join-Path $PSScriptRoot $notice
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $target -Recurse -Force }
}
if(-not $NoShortcut){
    $shell = New-Object -ComObject WScript.Shell
    $menu = [Environment]::GetFolderPath('Programs')
    $shortcut = $shell.CreateShortcut((Join-Path $menu 'Clone by Codex.lnk'))
    $shortcut.TargetPath = Join-Path $target 'CloneCad.Desktop.exe'
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = 'Clone by Codex - Agent CAD'
    $shortcut.Save()
}
Write-Host "Installed: $target"
Write-Host 'Launch Clone by Codex from the Start menu. Existing drawings and credentials were preserved.'
