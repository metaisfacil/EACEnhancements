$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$artifactDirectory = Join-Path $repositoryRoot 'Artifacts'
$stagingDirectory = Join-Path $artifactDirectory 'Package\EACEnhancements'
$packagePath = Join-Path $artifactDirectory 'EACEnhancements.zip'
$files = @(
    @{ Source = (Join-Path $artifactDirectory 'EACEnhancements.dll'); Name = 'EACEnhancements.dll' },
    @{ Source = (Join-Path $PSScriptRoot 'EacInstallDiscovery.ps1'); Name = 'EacInstallDiscovery.ps1' },
    @{ Source = (Join-Path $PSScriptRoot 'Install.ps1'); Name = 'Install.ps1' },
    @{ Source = (Join-Path $PSScriptRoot 'Install EAC Enhancements.cmd'); Name = 'Install EAC Enhancements.cmd' },
    @{ Source = (Join-Path $PSScriptRoot 'LaunchEacWithLogging.ps1'); Name = 'LaunchEacWithLogging.ps1' },
    @{ Source = (Join-Path $PSScriptRoot 'Launch EAC with Logging.cmd'); Name = 'Launch EAC with Logging.cmd' },
    @{ Source = (Join-Path $repositoryRoot 'README.md'); Name = 'README.md' }
)

foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath $file.Source -PathType Leaf)) {
        throw "Required package input was not found: $($file.Source)"
    }
}

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
foreach ($file in $files) {
    Copy-Item -LiteralPath $file.Source `
        -Destination (Join-Path $stagingDirectory $file.Name) -Force
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}
Compress-Archive -Path (Join-Path $stagingDirectory '*') `
    -DestinationPath $packagePath -CompressionLevel Optimal

Write-Host "Packaged: $packagePath"
