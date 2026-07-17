param(
    [string]$EacDirectory,
    [string]$InteropPath
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'EacInstallDiscovery.ps1')
$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$sourceDirectory = Join-Path $repositoryRoot 'src\EACEnhancements'
$artifactDirectory = Join-Path $repositoryRoot 'Artifacts'
$sources = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File |
    Sort-Object Name |
    ForEach-Object { $_.FullName })
$output = Join-Path $artifactDirectory 'EACEnhancements.dll'
$interop = if ([string]::IsNullOrWhiteSpace($InteropPath)) {
    Join-Path (Resolve-EacDirectory $EacDirectory) 'Interop.HelperFunctionsLib.dll'
}
elseif ([IO.Path]::IsPathRooted($InteropPath)) {
    [IO.Path]::GetFullPath($InteropPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $InteropPath))
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'

foreach ($required in @($interop, $compiler)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required build input was not found: $required"
    }
}

if ($sources.Count -eq 0) {
    throw "No C# source files were found in $sourceDirectory"
}

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

& $compiler /nologo /target:library /platform:x86 /optimize+ `
    /warn:4 /reference:$interop `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    /out:$output $sources
if ($LASTEXITCODE -ne 0) {
    throw "C# compiler failed with exit code $LASTEXITCODE."
}

Write-Host "Built: $output"
