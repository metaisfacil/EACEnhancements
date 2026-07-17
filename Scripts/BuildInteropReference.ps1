$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$source = Join-Path $repositoryRoot `
    'src\Interop.HelperFunctionsLib.Reference\HelperFunctionsLib.cs'
$outputDirectory = Join-Path $repositoryRoot 'Artifacts\Reference'
$output = Join-Path $outputDirectory 'Interop.HelperFunctionsLib.dll'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'

foreach ($required in @($source, $compiler)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required reference-build input was not found: $required"
    }
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
& $compiler /nologo /target:library /platform:x86 /optimize+ /warn:4 `
    /out:$output $source
if ($LASTEXITCODE -ne 0) {
    throw "Interop reference compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Built reference: $output"
