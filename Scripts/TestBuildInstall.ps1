param(
    [string]$EacDirectory,
    [string]$InteropPath
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'EacInstallDiscovery.ps1')

function Get-RunningEacProcesses {
    return @(Get-CimInstance Win32_Process |
        Where-Object { $_.Name -ieq 'EAC.exe' })
}

function Stop-RunningEacProcesses {
    $running = Get-RunningEacProcesses
    foreach ($process in $running) {
        Write-Host "Stopping $($process.Name) (PID $($process.ProcessId))..."
        Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
    }

    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        if ((Get-RunningEacProcesses).Count -eq 0) {
            return
        }
        Start-Sleep -Milliseconds 100
    }

    $remaining = Get-RunningEacProcesses
    $processList = ($remaining | ForEach-Object {
        '{0} (PID {1})' -f $_.Name, $_.ProcessId
    }) -join ', '
    throw "EAC did not close in time: $processList"
}

$eacRoot = Resolve-EacDirectory $EacDirectory
$eacExecutable = Join-Path $eacRoot 'EAC.exe'
$failure = $null

try {
    Stop-RunningEacProcesses

    Write-Host 'Testing EAC Enhancements...'
    $testParameters = @{ EacDirectory = $eacRoot }
    if (-not [string]::IsNullOrWhiteSpace($InteropPath)) {
        $testParameters.InteropPath = $InteropPath
    }
    & (Join-Path $PSScriptRoot 'Test.ps1') @testParameters

    Write-Host 'Building EAC Enhancements...'
    $buildParameters = @{ EacDirectory = $eacRoot }
    if (-not [string]::IsNullOrWhiteSpace($InteropPath)) {
        $buildParameters.InteropPath = $InteropPath
    }
    & (Join-Path $PSScriptRoot 'Build.ps1') @buildParameters

    Write-Host 'Installing EAC Enhancements...'
    & powershell.exe `
        -NoLogo `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $PSScriptRoot 'Install.ps1') `
        -EacDirectory $eacRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Install.ps1 failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Test, build, and installation completed successfully.' -ForegroundColor Green
}
catch {
    $failure = $_
    Write-Host ''
    Write-Host 'Test/build/install failed.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    try {
        if ((Get-Process -Name EAC -ErrorAction SilentlyContinue).Count -eq 0) {
            Write-Host "Starting: $eacExecutable"
            Start-Process `
                -FilePath $eacExecutable `
                -WorkingDirectory $eacRoot
        }
        else {
            Write-Host 'EAC is already running; no second instance was started.'
        }
    }
    catch {
        if ($null -eq $failure) {
            $failure = $_
        }
        else {
            Write-Warning "EAC could not be restarted: $($_.Exception.Message)"
        }
    }
}

if ($null -ne $failure) {
    exit 1
}
exit 0
