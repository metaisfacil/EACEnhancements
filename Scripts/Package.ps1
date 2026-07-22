param([string]$InnoSetupCompiler)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$artifactDirectory = Join-Path $repositoryRoot 'Artifacts'
$plugin = Join-Path $artifactDirectory 'EACEnhancements.dll'
$installerDefinition = Join-Path $repositoryRoot 'Installer\EACEnhancements.iss'
$installer = Join-Path $artifactDirectory 'EACEnhancements-Setup.exe'

foreach ($required in @($plugin, $installerDefinition)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required installer input was not found: $required"
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $InnoSetupCompiler = $command.Source
    }
}
if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $candidates = @()
    foreach ($programDirectory in @(
        ${env:ProgramFiles(x86)},
        $env:ProgramFiles,
        (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if (-not [string]::IsNullOrWhiteSpace($programDirectory)) {
            $candidates += Join-Path $programDirectory 'Inno Setup 6\ISCC.exe'
        }
    }
    $InnoSetupCompiler = $candidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or
    -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup or pass -InnoSetupCompiler.'
}

$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($plugin).Version
$version = '{0}.{1}.{2}' -f `
    $assemblyVersion.Major, $assemblyVersion.Minor, $assemblyVersion.Build
if ($assemblyVersion.Revision -gt 0) {
    $version += ".$($assemblyVersion.Revision)"
}

if (Test-Path -LiteralPath $installer -PathType Leaf) {
    Remove-Item -LiteralPath $installer -Force
}
& $InnoSetupCompiler "/DAppVersion=$version" $installerDefinition
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Inno Setup did not create the expected installer: $installer"
}

Write-Host "Packaged installer: $installer"
