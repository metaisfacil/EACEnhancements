param(
    [string]$EacDirectory,
    [string]$DiagnosticLogPath,
    [switch]$Elevated,
    [switch]$Interactive
)

$ErrorActionPreference = 'Stop'
$installerLoggingEnabled = $env:EACENHANCEMENTS_LOGGING -eq '1'

function Initialize-InstallerLog {
    param(
        [string]$RequestedPath,
        [bool]$Append
    )

    if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        $fileName = 'EACEnhancements-Install-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss')
        $RequestedPath = Join-Path ([IO.Path]::GetTempPath()) $fileName
    }

    try {
        $resolvedPath = [IO.Path]::GetFullPath($RequestedPath)
        $parent = Split-Path -Path $resolvedPath -Parent
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        $header = @(
            ''
            ('=' * 72)
            "EAC Enhancements installer diagnostics"
            "Started:     $([DateTime]::Now.ToString('O'))"
            "PowerShell:  $($PSVersionTable.PSVersion)"
            "Windows:     $([Environment]::OSVersion.VersionString)"
            "User:        $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
            "ElevatedRun: $Elevated"
            "Requested EAC directory: $EacDirectory"
            ('=' * 72)
        ) -join [Environment]::NewLine
        if ($Append -and (Test-Path -LiteralPath $resolvedPath)) {
            Add-Content -LiteralPath $resolvedPath -Value $header -Encoding UTF8
        }
        else {
            Set-Content -LiteralPath $resolvedPath -Value $header -Encoding UTF8
        }
        return $resolvedPath
    }
    catch {
        Write-Warning "Installer diagnostics could not be initialized: $($_.Exception.Message)"
        return $null
    }
}

function Write-InstallerLog {
    param([string]$Message)

    if ([string]::IsNullOrWhiteSpace($script:installerLogPath)) {
        return
    }
    try {
        $line = '{0}  {1}' -f ([DateTime]::Now.ToString('O')), $Message
        Add-Content -LiteralPath $script:installerLogPath -Value $line -Encoding UTF8
    }
    catch {
        # Logging must never conceal the original installation result.
    }
}

function Quote-ProcessArgument {
    param([string]$Value)
    return '"{0}"' -f ($Value -replace '"', '\"')
}

function Wait-ForInstallerAcknowledgement {
    if ($Interactive) {
        Write-Host ''
        Read-Host 'Press Enter to close' | Out-Null
    }
}

$script:installerLogPath = $null
if ($installerLoggingEnabled) {
    $script:installerLogPath = Initialize-InstallerLog `
        -RequestedPath $DiagnosticLogPath `
        -Append:$Elevated
}

try {
    . (Join-Path $PSScriptRoot 'EacInstallDiscovery.ps1')
    $root = Resolve-EacDirectory $EacDirectory
    $repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
    Write-InstallerLog "Resolved EAC directory: $root"

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    $isAdministrator = $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
    Write-InstallerLog "Administrator: $isAdministrator"

    if (-not $isAdministrator) {
        if ($Elevated) {
            throw 'The elevated installer process does not have administrator privileges.'
        }

        $argumentParts = @(
            '-NoLogo'
            '-NoProfile'
            '-ExecutionPolicy Bypass'
            ('-File {0}' -f (Quote-ProcessArgument $PSCommandPath))
            ('-EacDirectory {0}' -f (Quote-ProcessArgument $root))
            '-Elevated'
        )
        if ($installerLoggingEnabled) {
            $argumentParts += '-DiagnosticLogPath {0}' -f `
                (Quote-ProcessArgument $script:installerLogPath)
        }
        if ($Interactive) {
            $argumentParts += '-Interactive'
        }
        $arguments = $argumentParts -join ' '
        Write-InstallerLog 'Requesting administrator privileges.'
        $process = Start-Process powershell.exe `
            -Verb RunAs `
            -ArgumentList $arguments `
            -Wait `
            -PassThru
        Write-InstallerLog "Elevated installer exit code: $($process.ExitCode)"
        if ($process.ExitCode -ne 0) {
            exit $process.ExitCode
        }

        Write-Host 'Installation completed successfully.' -ForegroundColor Green
        if ($installerLoggingEnabled) {
            Write-Host "Diagnostic log: $script:installerLogPath"
        }
        exit 0
    }

    $sourceCandidates = @(
        (Join-Path $PSScriptRoot 'EACEnhancements.dll'),
        (Join-Path $repositoryRoot 'Artifacts\EACEnhancements.dll')
    )
    $source = $sourceCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
    $destination = Join-Path $root 'EACEnhancements.dll'
    $runtimeLog = Join-Path $root 'EACEnhancements.log'

    $existing = Get-CimInstance Win32_Process |
        Where-Object { $_.Name -like 'EAC*.exe' }
    if ($existing) {
        $processList = ($existing | ForEach-Object {
            '{0} (PID {1})' -f $_.Name, $_.ProcessId
        }) -join ', '
        throw "Close every EAC window before installing. Still running: $processList"
    }

    if ([string]::IsNullOrWhiteSpace($source)) {
        throw (Get-MissingEacEnhancementsDllMessage `
            -ScriptDirectory $PSScriptRoot `
            -RepositoryRoot $repositoryRoot)
    }
    Write-InstallerLog "Source DLL: $source"
    Write-InstallerLog "Destination DLL: $destination"

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
    Remove-Item -LiteralPath $runtimeLog -Force -ErrorAction SilentlyContinue

    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $installedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    Write-InstallerLog "Source SHA256: $sourceHash"
    Write-InstallerLog "Installed SHA256: $installedHash"
    if ($sourceHash -ne $installedHash) {
        throw 'Installed DLL hash does not match the build output.'
    }

    Write-InstallerLog 'Installation completed successfully.'
    Write-Host "Installed: $destination"
    Write-Host "SHA256:   $installedHash"
    Write-Host 'Windows download blocking has been removed from the installed DLL.'
    Write-Host 'You may now start EAC normally.'
    if ($installerLoggingEnabled) {
        Write-Host "Diagnostic log: $script:installerLogPath"
    }
    Wait-ForInstallerAcknowledgement
    exit 0
}
catch {
    $message = $_.Exception.Message
    $details = $_ | Format-List * -Force | Out-String
    Write-InstallerLog "INSTALLATION FAILED: $message"
    Write-InstallerLog $details.TrimEnd()
    if (-not [string]::IsNullOrWhiteSpace($_.ScriptStackTrace)) {
        Write-InstallerLog "Script stack trace: $($_.ScriptStackTrace)"
    }

    Write-Host ''
    Write-Host 'EAC Enhancements installation failed.' -ForegroundColor Red
    Write-Host $message -ForegroundColor Red
    if (-not [string]::IsNullOrWhiteSpace($script:installerLogPath)) {
        Write-Host ''
        Write-Host "Diagnostic log: $script:installerLogPath"
        Write-Host 'Please include this log when reporting an installer problem.'
    }
    else {
        Write-Host ''
        Write-Host 'For a detailed diagnostic log, set EACENHANCEMENTS_LOGGING=1 and run the installer again.'
    }
    Wait-ForInstallerAcknowledgement
    exit 1
}
