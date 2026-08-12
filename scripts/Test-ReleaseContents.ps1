[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path,

    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$temporaryDirectory = $null

function Get-RelativeReleaseFiles {
    param([Parameter(Mandatory)][string]$Root)

    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
        $resolvedFile = [IO.Path]::GetFullPath($_.FullName)
        if (-not $resolvedFile.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Release entry resolved outside the audit root: '$resolvedFile'."
        }

        [PSCustomObject]@{
            FullName = $_.FullName
            RelativeName = $resolvedFile.Substring($resolvedRoot.Length).Replace('\', '/')
        }
    }
}

try {
    if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
        if ([IO.Path]::GetExtension($resolvedPath) -ine '.zip') {
            throw "Release audit accepts a publish directory or ZIP archive, not '$resolvedPath'."
        }

        $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "VSModifierReleaseAudit_$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
        Expand-Archive -LiteralPath $resolvedPath -DestinationPath $temporaryDirectory
        $auditRoot = $temporaryDirectory
    }
    else {
        $auditRoot = $resolvedPath
    }

    $files = @(Get-RelativeReleaseFiles -Root $auditRoot)
    if ($files.Count -eq 0) {
        throw 'Release content is empty.'
    }

    $blacklistPatterns = @(
        '(?i)(^|/)(artifacts?|dumps?|backups?|logs?|captures?|process[-_ ]?(?:captures?|dumps?))(/|$)',
        '(?i)(^|/)(?:SaveData[^/]*|GameAssembly\.dll|UnityPlayer\.dll|global-metadata\.dat|Assembly-CSharp(?:-firstpass)?\.dll|dump\.cs|il2cpp\.h|script\.json|createdump\.exe)$',
        '(?i)\.(?:pdb|mdb|dbg|dmp|mdmp|gcdump|nettrace|etl|trace|log|map|ilk|exp|lib|user|suo|pcap|pcapng|har)$',
        '(?i)\.(?:assets|resS|unity3d|bundle)$',
        '(?i)(^|/)(?:sharedassets\d+|level\d+)(?:\.|/|$)'
    )

    $blocked = @(
        foreach ($file in $files) {
            foreach ($pattern in $blacklistPatterns) {
                if ($file.RelativeName -match $pattern) {
                    $file.RelativeName
                    break
                }
            }
        }
    ) | Sort-Object -Unique

    if ($blocked.Count -gt 0) {
        throw "Release blacklist rejected: $($blocked -join ', ')"
    }

    $requiredFiles = @(
        'VSModifier.App.exe',
        'README.md',
        'LICENSE',
        'data/offsets.json',
        'data/ids/unlocks.json',
        'data/ids/forced-level-up-items.json'
    )
    $relativeNames = @($files.RelativeName)
    $missing = @($requiredFiles | Where-Object { $_ -notin $relativeNames })
    if ($missing.Count -gt 0) {
        throw "Release is missing required content: $($missing -join ', ')"
    }

    $offsetsPath = Join-Path $auditRoot 'data\offsets.json'
    $offsets = Get-Content -LiteralPath $offsetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $profiles = @($offsets.profiles)
    if ($profiles.Count -eq 0) {
        throw 'Release offsets catalog contains no profiles.'
    }

    $enabledProfiles = @($profiles | Where-Object { $_.verified -ne $false })
    if ($enabledProfiles.Count -gt 0) {
        $profileIds = @($enabledProfiles | ForEach-Object { $_.profileId }) -join ', '
        throw "Release requires every Trainer profile to remain verified:false; rejected: $profileIds"
    }

    $exePath = Join-Path $auditRoot 'VSModifier.App.exe'
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).FileVersion
    $expectedFileVersion = "$Version.0"
    if ($fileVersion -cne $expectedFileVersion) {
        throw "VSModifier.App.exe file version '$fileVersion' does not match '$expectedFileVersion'."
    }

    Write-Output "Release content audit passed: $($files.Count) files; file version $fileVersion; $($profiles.Count) Trainer profiles verified:false."
}
finally {
    if ($temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        $resolvedTemporaryDirectory = [IO.Path]::GetFullPath($temporaryDirectory)
        if (-not $resolvedTemporaryDirectory.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected audit directory '$resolvedTemporaryDirectory'."
        }

        Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force
    }
}
