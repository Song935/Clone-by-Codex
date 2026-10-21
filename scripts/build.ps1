param([string]$Configuration = 'Release')
. "$PSScriptRoot/common.ps1"
Push-Location $ProjectRoot
try { Invoke-CadDotnet build CloneCad.sln -c $Configuration }
finally { Pop-Location }
