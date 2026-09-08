[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "StuWard.csproj"

Write-Host "[DebugQuickBuild] Building and deploying STUWard (Debug)..."
& dotnet build $projectFile "-c" "Debug" "-p:DeployToGame=true"
if ($LASTEXITCODE -ne 0)
{
    throw "[DebugQuickBuild] Debug build or game deployment failed with exit code $LASTEXITCODE."
}
