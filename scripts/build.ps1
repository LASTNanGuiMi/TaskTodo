#requires -Version 7.0
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = if ($env:TASKTODO_DOTNET) { $env:TASKTODO_DOTNET } else { 'dotnet' }

Push-Location $projectRoot
try {
    & $dotnetCommand restore 'TaskTodo.sln'
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    & $dotnetCommand build 'TaskTodo.sln' -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}
