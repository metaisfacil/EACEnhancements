param(
    [Parameter(Mandatory = $true)]
    [string]$Tag
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$sourcePath = Join-Path $repositoryRoot 'src\EACEnhancements\EACEnhancements.cs'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Version source file was not found: $sourcePath"
}

$source = Get-Content -LiteralPath $sourcePath -Raw
function Read-AssemblyVersionAttribute([string]$attributeName) {
    $pattern = '\[assembly:\s*' + [Regex]::Escape($attributeName) +
        '\("(?<version>[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"\)\s*\]'
    $matches = [Regex]::Matches($source, $pattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one $attributeName attribute in $sourcePath; found $($matches.Count)."
    }
    return [Version]$matches[0].Groups['version'].Value
}

$assemblyVersion = Read-AssemblyVersionAttribute 'AssemblyVersion'
$fileVersion = Read-AssemblyVersionAttribute 'AssemblyFileVersion'
if ($assemblyVersion -ne $fileVersion) {
    throw "AssemblyVersion ($assemblyVersion) and AssemblyFileVersion ($fileVersion) do not match."
}

$version = '{0}.{1}.{2}' -f `
    $assemblyVersion.Major, $assemblyVersion.Minor, $assemblyVersion.Build
if ($assemblyVersion.Revision -gt 0) {
    $version += ".$($assemblyVersion.Revision)"
}
$expectedTag = "v$version"

if ($Tag -cne $expectedTag) {
    throw "Release tag '$Tag' does not exactly match source version '$expectedTag'."
}

Write-Host "Release tag $Tag exactly matches source version $assemblyVersion."
