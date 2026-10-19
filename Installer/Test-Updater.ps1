param([Parameter(Mandatory)][string]$CommandFile)
$ErrorActionPreference = 'Stop'
$command = Get-Content -Raw -LiteralPath $CommandFile
$tokens=$null; $errors=$null
[System.Management.Automation.Language.Parser]::ParseInput($command,[ref]$tokens,[ref]$errors) | Out-Null
if($errors.Count) { throw ($errors | Out-String) }
$resultFile = Join-Path ([IO.Path]::GetTempPath()) ('InerConfig-UpdaterTest-' + [Guid]::NewGuid().ToString('N') + '.txt')
$command=$command.Replace("'C:\a''b\result.txt'",("'"+$resultFile.Replace("'","''")+"'"))
function Wait-Process { param($Id,$Timeout,$ErrorAction) }
function Get-Process { param($Id,$ErrorAction) return $null }
function Test-Path { param($LiteralPath) return $true }
function Start-Process {
    param($FilePath,$ArgumentList,[switch]$PassThru,[switch]$Wait)
    if($FilePath -eq 'msiexec.exe') {
        if($ArgumentList[1] -ne '"C:\a''b\update.msi"') { throw 'MSI argument quoting failed.' }
        $script:installed=$true
        if($script:exitCode -eq -1) { throw 'Simulated installation failure.' }
        return [pscustomobject]@{ ExitCode=$script:exitCode }
    }
    if($FilePath -ne "C:\a'b\InerConfig.exe" -or $ArgumentList -ne '--skip-update-once') { throw 'Restart argument mismatch.' }
    $script:restarted=$true
}
try {
    foreach($code in @(0,3010,1603,-1)) {
        $script:exitCode=$code; $script:installed=$false; $script:restarted=$false
        & ([ScriptBlock]::Create($command))
        if(-not $script:installed -or -not $script:restarted -or [IO.File]::ReadAllText($resultFile) -ne "0.1.2|$code") { throw "Updater branch failed: $code" }
    }
    Write-Output 'PASS: helper syntax, apostrophe paths, success/reboot/failure/exception result and restart (4 mocked cases).'
} finally { if([IO.File]::Exists($resultFile)) { [IO.File]::Delete($resultFile) } }
