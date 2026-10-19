param([Parameter(Mandatory)][string]$MsiPath, [string]$PreviousMsi)
$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($MsiPath, 0)
function Query([string]$sql) {
    $view = $db.OpenView($sql)
    [void]$view.Execute()
    $records = @()
    while ($record = $view.Fetch()) { $records += $record.StringData(1) }
    [void]$view.Close()
    return $records
}
$product = Query 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductCode'''
if ($installer.ProductState($product) -ne -1) { throw 'Test refuses to change an already registered product.' }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'InerConfig/InerConfig.lnk'
if (Test-Path -LiteralPath $shortcut) { throw 'Test refuses to replace an existing InerConfig shortcut.' }
$options = @(Query 'SELECT `Property` FROM `Control` WHERE `Dialog_` = ''Completed'' AND `Type` = ''CheckBox''')
if ($options.Count -ne 2 -or 'LAUNCHAPP' -notin $options -or 'OPENDEFAULTAPPS' -notin $options) { throw 'Completion options missing.' }
$testDir = Join-Path ([IO.Path]::GetTempPath()) ('InerConfig-InstallerTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
$installDir = Join-Path $testDir 'App'
$installed = $false
try {
    if ($PreviousMsi) {
        $previousPath=(Resolve-Path -LiteralPath $PreviousMsi).Path
        $previousDb=$installer.OpenDatabase($previousPath,0)
        $previousView=$previousDb.OpenView('SELECT `Value` FROM `Property` WHERE `Property` = ''ProductCode''')
        [void]$previousView.Execute();$previousProduct=$previousView.Fetch().StringData(1)
        if ($installer.ProductState($previousProduct) -ne -1) { throw 'Previous product already installed; refusing test.' }
        $process=Start-Process msiexec.exe -ArgumentList @('/i',('"'+$previousPath+'"'),'/qn','/norestart',('INSTALLFOLDER="'+$installDir+'"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin @(0,3010)) { throw 'Previous version install failed.' }
    }
    $process = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + $MsiPath + '"'), '/qn', '/norestart', ('INSTALLFOLDER="' + $installDir + '"'), '/l*v', ('"' + (Join-Path $testDir 'install.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -notin @(0,3010)) { throw "Installation failed: $($process.ExitCode). Logs: $testDir" }
    $installed = $true
    foreach($extension in @('.json','.xml','.csv','.yaml','.yml','.ini')) {
        $capabilities=Get-ItemProperty -LiteralPath 'HKCU:\Software\InerConfig\Capabilities\FileAssociations'
        if($capabilities.$extension -ne 'InerConfig.Document') { throw "Missing association: $extension" }
        $openWith=Get-Item -LiteralPath "HKCU:\Software\Classes\$extension\OpenWithProgids"
        if('InerConfig.Document' -notin $openWith.GetValueNames()) { throw "Missing Open With entry: $extension" }
    }
    $openCommand=(Get-Item -LiteralPath 'HKCU:\Software\Classes\InerConfig.Document\shell\open\command').GetValue('')
    if($openCommand -ne ('"'+(Join-Path $installDir 'InerConfig.exe')+'" "%1"')) { throw 'File open command mismatch.' }
    if ($PreviousMsi -and $installer.ProductState($previousProduct) -ne -1) { throw 'Previous version not removed by upgrade.' }
    $exe = Join-Path $installDir 'InerConfig.exe'
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $shortcut)) { throw 'Installed app or shortcut missing.' }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($shortcut).TargetPath -ne $exe) { throw 'Shortcut target mismatch.' }
    $process = Start-Process -FilePath $exe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Installed executable self-test failed.' }
    $result = Get-Content -LiteralPath (Join-Path $installDir 'self-test-result.txt') -Tail 1
    Copy-Item -LiteralPath (Join-Path $installDir 'self-test-result.txt') -Destination $testDir
    Write-Output "Installed executable: $result"
} finally {
    if ($installed) {
        $process = Start-Process msiexec.exe -ArgumentList @('/x', $product, '/qn', '/norestart', '/l*v', ('"' + (Join-Path $testDir 'uninstall.log') + '"')) -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -notin @(0,3010)) { throw "Test uninstall failed: $($process.ExitCode). Logs: $testDir" }
    }
    if ($PreviousMsi -and $previousProduct -and $installer.ProductState($previousProduct) -ne -1) {
        $cleanup=Start-Process msiexec.exe -ArgumentList @('/x',$previousProduct,'/qn','/norestart') -WindowStyle Hidden -Wait -PassThru
        if($cleanup.ExitCode -notin @(0,3010)) { throw 'Previous test version cleanup failed.' }
    }
}
if (Test-Path -LiteralPath (Join-Path $installDir 'InerConfig.exe')) { throw 'App was not removed.' }
if (Test-Path -LiteralPath $shortcut) { throw 'Shortcut was not removed.' }
if((Get-ItemProperty -LiteralPath 'HKCU:\Software\RegisteredApplications').PSObject.Properties.Name -contains 'InerConfig') { throw 'Default app registration was not removed.' }
Write-Output "PASS: completion options, per-user install, shortcut target, app checks, uninstall. Logs: $testDir"
