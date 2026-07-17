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

$sourceCandidates = @(
    (Join-Path $PSScriptRoot 'EACEnhancements.dll'),
    (Join-Path $repositoryRoot 'Artifacts\EACEnhancements.dll')
)
$source = $sourceCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
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

if ([string]::IsNullOrWhiteSpace($source)) {
    throw "EACEnhancements.dll was not found beside the installer or in the repository's Artifacts folder."
}

try {
    Unblock-File -LiteralPath $source -ErrorAction Stop
}
catch {
    throw "Windows could not unblock the downloaded DLL at '$source'. $($_.Exception.Message)"
}
Copy-Item -LiteralPath $source -Destination $destination -Force
try {
    Unblock-File -LiteralPath $destination -ErrorAction Stop
}
catch {
    throw "The DLL was copied, but Windows could not unblock '$destination'. $($_.Exception.Message)"
}
Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue

$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$installedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
if ($sourceHash -ne $installedHash) {
    throw 'Installed DLL hash does not match the build output.'
}

Write-Host "Installed: $destination"
Write-Host "SHA256:   $installedHash"
Write-Host 'Windows download blocking has been removed from the installed DLL.'
Write-Host 'You may now start EAC normally.'
Read-Host 'Press Enter to close'
