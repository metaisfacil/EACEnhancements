param(
    [string]$EacDirectory
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'EacInstallDiscovery.ps1')
$root = Resolve-EacDirectory $EacDirectory
$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdministrator) {
	$arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}" -EacDirectory "{1}"' -f `
		$PSCommandPath, $root
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments
    return
}

$source = Join-Path $repositoryRoot 'Artifacts\EACEnhancements.dll'
$destination = Join-Path $root 'EACEnhancements.dll'
$log = Join-Path $root 'EACEnhancements.log'

$existing = Get-CimInstance Win32_Process |
    Where-Object { $_.Name -like 'EAC*.exe' }
if ($existing) {
    $processList = ($existing | ForEach-Object {
        '{0} (PID {1})' -f $_.Name, $_.ProcessId
    }) -join ', '
    throw "Close every EAC window before installing. Still running: $processList"
}

if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Build output not found: $source"
}

Copy-Item -LiteralPath $source -Destination $destination -Force
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
if ($sourceHash -ne $installedHash) {
    throw 'Installed DLL hash does not match the build output.'
}

Write-Host "Installed: $destination"
Write-Host "SHA256:   $installedHash"
Write-Host 'You may now start EAC normally.'
Read-Host 'Press Enter to close'
