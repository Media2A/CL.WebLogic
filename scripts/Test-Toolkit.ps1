$ErrorActionPreference = "Stop"

$testProject = Join-Path $PSScriptRoot "..\tests\CL.WebLogic.Tests\CL.WebLogic.Tests.csproj"

Write-Host "Running CL.WebLogic toolkit tests..."
& dotnet test $testProject -p:UseSharedCompilation=false
