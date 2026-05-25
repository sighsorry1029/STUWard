$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "StuWard.csproj"
$environmentPropsFile = Join-Path $projectRoot "environment.props"
$configuration = "Debug"
$assemblyName = "STUWard"
$binDir = Join-Path $projectRoot "bin\Debug"
$translationsSourceDir = Join-Path $projectRoot "translations"

Write-Host "[DebugQuickBuild] Building $assemblyName ($configuration)..."
& dotnet msbuild $projectFile "/t:Build" "/p:Configuration=$configuration"
if ($LASTEXITCODE -ne 0)
{
    throw "[DebugQuickBuild] dotnet msbuild /t:Build failed with exit code $LASTEXITCODE."
}

$targetDll = Join-Path $binDir "$assemblyName.dll"
$targetPdb = Join-Path $binDir "$assemblyName.pdb"
$targetConfig = Join-Path $binDir "$assemblyName.dll.config"
$targetTranslationsDir = Join-Path $binDir "translations"

if (!(Test-Path $targetDll))
{
    throw "[DebugQuickBuild] Expected built DLL was not found: $targetDll"
}

New-Item -ItemType Directory -Force -Path $binDir | Out-Null

if (Test-Path $translationsSourceDir)
{
    New-Item -ItemType Directory -Force -Path $targetTranslationsDir | Out-Null
    Copy-Item -Path (Join-Path $translationsSourceDir "*") -Destination $targetTranslationsDir -Recurse -Force
}

$dllInfo = Get-Item $targetDll
Write-Host "[DebugQuickBuild] Updated DLL:"
Write-Host "  Path: $($dllInfo.FullName)"
Write-Host "  Size: $($dllInfo.Length) bytes"
Write-Host "  LastWriteTime: $($dllInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))"

if (Test-Path $targetPdb)
{
    $pdbInfo = Get-Item $targetPdb
    Write-Host "[DebugQuickBuild] Updated PDB:"
    Write-Host "  Path: $($pdbInfo.FullName)"
    Write-Host "  LastWriteTime: $($pdbInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))"
}

if (Test-Path $targetTranslationsDir)
{
    Write-Host "[DebugQuickBuild] Staged translations:"
    Get-ChildItem $targetTranslationsDir -File | ForEach-Object {
        Write-Host "  $($_.Name) ($($_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")))"
    }
}

$deployTargets = @()
if (Test-Path $environmentPropsFile)
{
    try
    {
        [xml]$environmentProps = Get-Content $environmentPropsFile
        $propertyGroup = $environmentProps.Project.PropertyGroup
        foreach ($node in $propertyGroup.ChildNodes)
        {
            if ($node.Name -like "CopyOutputDLLPath*")
            {
                $rawValue = [string]$node.InnerText
                $rawValue = $rawValue.Trim()
                if (![string]::IsNullOrWhiteSpace($rawValue) -and -not $rawValue.Contains('$('))
                {
                    $deployTargets += $rawValue
                }
            }
        }
    }
    catch
    {
        Write-Warning "[DebugQuickBuild] Failed to parse environment.props for deploy targets: $($_.Exception.Message)"
    }
}

$deployTargets = $deployTargets | Where-Object { $_ -and ($_ -ne $binDir) } | Select-Object -Unique
foreach ($deployTarget in $deployTargets)
{
    if (!(Test-Path $deployTarget))
    {
        continue
    }

    Copy-Item -Path $targetDll -Destination (Join-Path $deployTarget "$assemblyName.dll") -Force

    if (Test-Path $targetConfig)
    {
        Copy-Item -Path $targetConfig -Destination (Join-Path $deployTarget "$assemblyName.dll.config") -Force
    }

    if (Test-Path $targetTranslationsDir)
    {
        $deployTranslationsDir = Join-Path $deployTarget "translations"
        New-Item -ItemType Directory -Force -Path $deployTranslationsDir | Out-Null
        Copy-Item -Path (Join-Path $targetTranslationsDir "*") -Destination $deployTranslationsDir -Recurse -Force
    }

    Write-Host "[DebugQuickBuild] Deployed runtime files:"
    Write-Host "  Target: $deployTarget"
}
