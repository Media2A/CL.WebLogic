param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\samples\StarterWebsite\StarterWebsite.csproj"
$arguments = @("run", "--project", $project)

if ($NoBuild)
{
    $arguments += "--no-build"
}

Write-Host "Starting StarterWebsite..."
& dotnet @arguments
