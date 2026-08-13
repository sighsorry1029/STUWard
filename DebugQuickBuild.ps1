[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "StuWard.csproj"

Write-Host "[DebugQuickBuild] Building and deploying STUWard (Debug)..."
& dotnet msbuild $projectFile "/t:DeployLocal" "/p:Configuration=Debug" "/p:Platform=AnyCPU"
if ($LASTEXITCODE -ne 0)
{
    throw "[DebugQuickBuild] DeployLocal failed with exit code $LASTEXITCODE."
}
