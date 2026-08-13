[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$manifestFile,

    [Parameter(Mandatory = $true)]
    [string]$versionString
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($versionString -notmatch '^\d+\.\d+\.\d+$')
{
    throw "Invalid manifest version '$versionString'. Expected major.minor.patch."
}

$manifestPath = [IO.Path]::GetFullPath($manifestFile)
$thunderstoreRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "Thunderstore"))
$sourceManifestPath = [IO.Path]::GetFullPath((Join-Path $thunderstoreRoot "manifest.json"))
$stagingPrefix = $thunderstoreRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

if (!$manifestPath.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $manifestPath.Equals($sourceManifestPath, [StringComparison]::OrdinalIgnoreCase))
{
    throw "UpdateManifest.ps1 only accepts a staged manifest below Thunderstore; the tracked source manifest must remain unchanged."
}

if (![IO.File]::Exists($manifestPath))
{
    throw "Staged manifest not found: $manifestPath"
}

$bytes = [IO.File]::ReadAllBytes($manifestPath)
$hasUtf8Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
$offset = if ($hasUtf8Bom) { 3 } else { 0 }
$strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
$manifest = $strictUtf8.GetString($bytes, $offset, $bytes.Length - $offset)

$versionPattern = New-Object Text.RegularExpressions.Regex('("version_number"\s*:\s*")[^"]*(")')
$matches = $versionPattern.Matches($manifest)
if ($matches.Count -ne 1)
{
    throw "Expected exactly one version_number field in staged manifest, found $($matches.Count)."
}

$updatedManifest = $versionPattern.Replace($manifest, '${1}' + $versionString + '${2}', 1)
$outputEncoding = New-Object Text.UTF8Encoding($hasUtf8Bom)
[IO.File]::WriteAllText($manifestPath, $updatedManifest, $outputEncoding)
