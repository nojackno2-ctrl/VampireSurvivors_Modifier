[CmdletBinding()]
param(
    [string]$Tag,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$version = (& (Join-Path $PSScriptRoot 'Assert-Version.ps1') -Tag $Tag).Trim()
$runtimeIdentifier = 'win-x64'
$appProject = Join-Path $repoRoot 'VSModifier.App\VSModifier.App.csproj'
$nugetConfig = Join-Path $repoRoot 'NuGet.Config'
$releaseBase = Join-Path $repoRoot 'artifacts\release'
$releaseDirectory = Join-Path $releaseBase $version
$stagingDirectory = Join-Path $releaseBase ".staging-$version-$runtimeIdentifier"
$zipName = "VSModifier-v$version-$runtimeIdentifier.zip"
$zipPath = Join-Path $releaseDirectory $zipName
$checksumsPath = Join-Path $releaseDirectory 'SHA256SUMS'
$sourceHead = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceHead -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the source HEAD commit.'
}
$sourceChanges = @(& git -C $repoRoot status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the source working tree.'
}
$sourceState = if ($sourceChanges.Count -eq 0) { 'clean' } else { 'HEAD plus uncommitted working-tree changes' }
if ($Tag -and $sourceChanges.Count -ne 0) {
    throw "Tagged releases require a clean working tree; found $($sourceChanges.Count) changed paths."
}
if ($Tag) {
    $tagCommit = (& git -C $repoRoot rev-list -n 1 $Tag).Trim()
    if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $sourceHead) {
        throw "Release tag '$Tag' does not point to source HEAD '$sourceHead'."
    }
}

foreach ($target in @($releaseDirectory, $stagingDirectory)) {
    $fullTarget = [IO.Path]::GetFullPath($target)
    $fullReleaseBase = [IO.Path]::GetFullPath($releaseBase).TrimEnd('\') + '\'
    if (-not $fullTarget.StartsWith($fullReleaseBase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean release path outside '$fullReleaseBase': '$fullTarget'."
    }

    if (Test-Path -LiteralPath $fullTarget) {
        Remove-Item -LiteralPath $fullTarget -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $releaseDirectory, $stagingDirectory | Out-Null

try {
    if (-not $NoRestore) {
        & dotnet restore $appProject --runtime $runtimeIdentifier --configfile $nugetConfig
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }
    }

    & dotnet publish $appProject `
        --configuration Release `
        --runtime $runtimeIdentifier `
        --self-contained true `
        --no-restore `
        --output $stagingDirectory `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:ContinuousIntegrationBuild=true `
        "-p:SourceRevisionId=$sourceHead" `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $createdumpPath = Join-Path $stagingDirectory 'createdump.exe'
    if (Test-Path -LiteralPath $createdumpPath) {
        Remove-Item -LiteralPath $createdumpPath -Force
    }

    & (Join-Path $PSScriptRoot 'Test-ReleaseContents.ps1') -Path $stagingDirectory -Version $version
    if ($LASTEXITCODE -ne 0) {
        throw "Publish-directory audit failed with exit code $LASTEXITCODE."
    }

    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

    & (Join-Path $PSScriptRoot 'Test-ReleaseContents.ps1') -Path $zipPath -Version $version
    if ($LASTEXITCODE -ne 0) {
        throw "ZIP audit failed with exit code $LASTEXITCODE."
    }

    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumText = "$zipHash  $zipName`n"
    [IO.File]::WriteAllText($checksumsPath, $checksumText, [Text.UTF8Encoding]::new($false))

    Write-Output "Release package: $zipPath"
    Write-Output "SHA-256: $zipHash"
    Write-Output "Checksums: $checksumsPath"
    Write-Output "Source HEAD: $sourceHead"
    Write-Output "Source state: $sourceState"
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
