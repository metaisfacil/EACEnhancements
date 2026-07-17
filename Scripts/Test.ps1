param(
    [string]$EacDirectory,
    [string]$InteropPath
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'EacInstallDiscovery.ps1')
$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$sourceDirectory = Join-Path $repositoryRoot 'src\EACEnhancements'
$testDirectory = Join-Path $repositoryRoot 'Tests'
$testOutputDirectory = Join-Path $repositoryRoot 'Artifacts\Tests'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$interop = if ([string]::IsNullOrWhiteSpace($InteropPath)) {
    Join-Path (Resolve-EacDirectory $EacDirectory) 'Interop.HelperFunctionsLib.dll'
}
elseif ([IO.Path]::IsPathRooted($InteropPath)) {
    [IO.Path]::GetFullPath($InteropPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $InteropPath))
}
$sources = @(Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.cs' -File |
    Sort-Object Name |
    ForEach-Object { $_.FullName })
$tests = @(Get-ChildItem -LiteralPath $testDirectory -Filter '*.cs' -File |
    Sort-Object Name)

foreach ($required in @($compiler, $interop)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required test input was not found: $required"
    }
}
if ($sources.Count -eq 0) {
    throw "No C# source files were found in $sourceDirectory"
}
if ($tests.Count -eq 0) {
    throw "No C# test files were found in $testDirectory"
}

New-Item -ItemType Directory -Path $testOutputDirectory -Force | Out-Null
Copy-Item -LiteralPath $interop -Destination $testOutputDirectory -Force

$executables = @()
foreach ($test in $tests) {
    $output = Join-Path $testOutputDirectory ($test.BaseName + '.exe')
    & $compiler /nologo /target:exe /platform:x86 /optimize+ /warn:4 `
        /reference:$interop /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
        /out:$output $sources $test.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Compiling $($test.Name) failed with exit code $LASTEXITCODE."
    }
    $executables += $output
}

foreach ($executable in $executables) {
    Write-Host "Running: $(Split-Path -Path $executable -Leaf)"
    & $executable
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path -Path $executable -Leaf) failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Passed: $($executables.Count) test programs"
