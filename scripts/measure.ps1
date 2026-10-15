#requires -Version 7.0
param(
    [string]$AppPath,
    [string]$OutputDirectory = 'artifacts/performance'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = if ($env:TASKTODO_DOTNET) { $env:TASKTODO_DOTNET } else { 'dotnet' }
$previousArtifacts = $env:TASKTODO_TEST_ARTIFACTS
$previousAppPath = $env:TASKTODO_APP_PATH

Push-Location $projectRoot
try {
    $env:TASKTODO_TEST_ARTIFACTS = [IO.Path]::GetFullPath($OutputDirectory)
    if ($AppPath) { $env:TASKTODO_APP_PATH = (Resolve-Path -LiteralPath $AppPath).Path }
    & $dotnetCommand test 'tests/TaskTodo.App.Tests/TaskTodo.App.Tests.csproj' -c Release --filter 'Category=Performance' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw "Performance observation failed with exit code $LASTEXITCODE" }
}
finally {
    $env:TASKTODO_TEST_ARTIFACTS = $previousArtifacts
    $env:TASKTODO_APP_PATH = $previousAppPath
    Pop-Location
}
