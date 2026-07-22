function ConvertTo-EacDirectoryCandidate {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Value.Trim())
    if ($expanded.StartsWith('"')) {
        $closingQuote = $expanded.IndexOf('"', 1)
        if ($closingQuote -gt 1) {
            $expanded = $expanded.Substring(1, $closingQuote - 1)
        }
    }
    elseif ($expanded -match '^(.*?\.exe)(?:,\d+|\s+.*)?$') {
        $expanded = $Matches[1]
    }

    $expanded = $expanded.Trim().Trim('"')
    if ([IO.Path]::GetExtension($expanded).Equals('.exe', [StringComparison]::OrdinalIgnoreCase)) {
        return Split-Path -Path $expanded -Parent
    }
    return $expanded
}

function Resolve-EacDirectory {
    param([string]$RequestedDirectory)

    if (-not [string]::IsNullOrWhiteSpace($RequestedDirectory)) {
        $requested = [IO.Path]::GetFullPath($RequestedDirectory)
        if (-not (Test-Path -LiteralPath (Join-Path $requested 'EAC.exe') -PathType Leaf)) {
            throw "EAC.exe was not found in the requested directory: $requested"
        }
        return $requested
    }

    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($env:EAC_DIRECTORY)) {
        [void]$candidates.Add($env:EAC_DIRECTORY)
    }
    [void]$candidates.Add($PSScriptRoot)
    [void]$candidates.Add((Split-Path -Path $PSScriptRoot -Parent))

    $uninstallRoots = @(
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    )
    foreach ($uninstallRoot in $uninstallRoots) {
        if (-not (Test-Path -LiteralPath $uninstallRoot)) {
            continue
        }
        foreach ($key in Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue) {
            $entry = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if ($null -eq $entry -or $entry.DisplayName -notlike 'Exact Audio Copy*') {
                continue
            }
            foreach ($value in @($entry.InstallLocation, $entry.DisplayIcon, $entry.UninstallString)) {
                $candidate = ConvertTo-EacDirectoryCandidate $value
                if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                    [void]$candidates.Add($candidate)
                }
            }
        }
    }

    foreach ($programFiles in @(
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles))) {
        if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
            [void]$candidates.Add((Join-Path $programFiles 'Exact Audio Copy'))
        }
    }

    $visited = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $candidates) {
        try {
            $fullPath = [IO.Path]::GetFullPath($candidate)
        }
        catch {
            continue
        }
        if ($visited.Add($fullPath) -and
            (Test-Path -LiteralPath (Join-Path $fullPath 'EAC.exe') -PathType Leaf)) {
            return $fullPath
        }
    }

    throw "Exact Audio Copy could not be located. Pass its folder with -EacDirectory or set EAC_DIRECTORY."
}

function Get-MissingEacEnhancementsDllMessage {
    param(
        [string]$ScriptDirectory,
        [string]$RepositoryRoot
    )

    $sourceDirectory = Join-Path $RepositoryRoot 'src\EACEnhancements'
    $buildScript = Join-Path $ScriptDirectory 'Build.ps1'
    if ((Test-Path -LiteralPath $sourceDirectory -PathType Container) -and
        (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
        return @(
            'This appears to be a source checkout or GitHub source-code archive, which does not include a compiled EACEnhancements.dll.'
            ''
            'For a normal installation, download EACEnhancements-Setup.exe from the Releases page:'
            'https://github.com/metaisfacil/EACEnhancements/releases'
            ''
            'Do not download GitHub''s automatically generated "Source code" ZIP. To install from source instead, run Scripts\Build.ps1 first.'
        ) -join [Environment]::NewLine
    }

    return 'EACEnhancements.dll was not found beside the installer or in the repository''s Artifacts folder.'
}
