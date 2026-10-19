param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipPublish) { & (Join-Path $projectRoot 'build.ps1') -Portable }
$publishDir = Join-Path $projectRoot 'dist/portable-win-x64'
$toolDir = Join-Path $projectRoot 'obj/installer-tools'
$wix = Join-Path $toolDir 'wix.exe'
if (-not (Test-Path -LiteralPath $wix)) {
    dotnet tool install wix --version 5.0.2 --tool-path $toolDir
    if ($LASTEXITCODE -ne 0) { throw 'WiX installation failed.' }
}
& $wix extension add -g WixToolset.UI.wixext/5.0.2
if ($LASTEXITCODE -ne 0) { throw 'WiX UI extension installation failed.' }
$version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'InerConfig.csproj'))).Project.PropertyGroup.Version
$fragment = Join-Path $projectRoot 'obj/Payload.wxs'
$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$xml = [Xml.XmlWriter]::Create($fragment, $settings)
try {
    $xml.WriteStartElement('Wix', 'http://wixtoolset.org/schemas/v4/wxs')
    $xml.WriteStartElement('Fragment')
    $xml.WriteStartElement('ComponentGroup'); $xml.WriteAttributeString('Id', 'Payload'); $xml.WriteAttributeString('Directory', 'INSTALLFOLDER')
    $index = 0
    foreach ($file in Get-ChildItem -LiteralPath $publishDir -Recurse -File | Sort-Object FullName) {
        if ($file.Extension -eq '.pdb' -or $file.Name -like 'self-test-*' -or $file.Name -like '*test-result*' -or $file.Name -like '*test-error*') { continue }
        $relative = [IO.Path]::GetRelativePath($publishDir, $file.FullName)
        $subdir = [IO.Path]::GetDirectoryName($relative)
        $xml.WriteStartElement('Component'); $xml.WriteAttributeString('Id', "Payload$index"); $xml.WriteAttributeString('Guid', '*')
        if ($subdir) { $xml.WriteAttributeString('Subdirectory', $subdir) }
        $xml.WriteStartElement('File'); $xml.WriteAttributeString('Id', $(if ($relative -eq 'InerConfig.exe') { 'AppExe' } else { "File$index" })); $xml.WriteAttributeString('Source', $file.FullName); $xml.WriteAttributeString('KeyPath', 'yes'); $xml.WriteEndElement()
        $xml.WriteEndElement(); $index++
    }
    $xml.WriteEndElement(); $xml.WriteEndElement(); $xml.WriteEndElement()
} finally { $xml.Dispose() }
$output = Join-Path $projectRoot "dist/InerConfig-$version-x64.msi"
& $wix build (Join-Path $PSScriptRoot 'Product.wxs') $fragment -arch x64 -ext WixToolset.UI.wixext -culture ko-KR -d "ProductVersion=$version" -d "ProjectRoot=$projectRoot" -o $output
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
Write-Output $output
