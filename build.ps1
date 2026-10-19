param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$projectFile = Join-Path $PSScriptRoot 'InerConfig.csproj'
$targetDir = Join-Path $PSScriptRoot $(if ($Portable) { 'dist/portable-win-x64' } else { 'dist/win-x64' })
$runtimeOption = if ($Portable) { 'true' } else { 'false' }
dotnet publish $projectFile -c Release -r win-x64 --self-contained $runtimeOption -o $targetDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Get-ChildItem -LiteralPath $targetDir -File | Where-Object { $_.Name -in @('SoleStore.dll','SoleStore.pdb','SoleStore.deps.json') } | Remove-Item
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $targetDir
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Samples') -Destination $targetDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Licenses') -Destination $targetDir -Recurse -Force
Write-Output (Join-Path $targetDir 'InerConfig.exe')
