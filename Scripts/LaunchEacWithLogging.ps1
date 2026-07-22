param(
    [string]$EacDirectory
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'EacInstallDiscovery.ps1')
$eacRoot = Resolve-EacDirectory $EacDirectory
$executable = Join-Path $eacRoot 'EAC.exe'

$existing = Get-CimInstance Win32_Process |
    Where-Object { $_.Name -ieq 'EAC.exe' }
if ($existing) {
    $processList = ($existing | ForEach-Object {
        '{0} (PID {1})' -f $_.Name, $_.ProcessId
    }) -join ', '
    throw "Close every EAC window before starting a logging session. Still running: $processList"
}

$env:EACENHANCEMENTS_LOGGING = '1'
Start-Process -FilePath $executable -WorkingDirectory $eacRoot
Write-Host 'Started EAC with EAC Enhancements diagnostic logging enabled for this session.'
