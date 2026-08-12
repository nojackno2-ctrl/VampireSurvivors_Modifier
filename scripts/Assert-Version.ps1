[CmdletBinding()]
param(
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot 'Directory.Build.props'

[xml]$props = Get-Content -LiteralPath $propsPath -Raw -Encoding UTF8
$versionNodes = @($props.SelectNodes('/Project/PropertyGroup/Version'))
if ($versionNodes.Count -ne 1) {
    throw "Directory.Build.props must contain exactly one <Version> element; found $($versionNodes.Count)."
}

$version = $versionNodes[0].InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Product version '$version' is not a supported semantic version."
}

if ($Tag) {
    $expectedTag = "v$version"
    if ($Tag -cne $expectedTag) {
        throw "Release tag '$Tag' does not match product version '$version'. Expected '$expectedTag'."
    }
}

Write-Output $version
