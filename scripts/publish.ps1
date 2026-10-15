#requires -Version 7.0
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory = 'artifacts/publish/win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = if ($env:TASKTODO_DOTNET) { $env:TASKTODO_DOTNET } else { 'dotnet' }

Push-Location $projectRoot
try {
    & $dotnetCommand publish 'src/TaskTodo.App/TaskTodo.App.csproj' -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    # The directory is the deliverable, including the runtime and native SQLite library.
    foreach ($fileName in @('TaskTodo.exe', 'TaskTodo.dll', 'TaskTodo.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'e_sqlite3.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory $fileName) -PathType Leaf)) {
            throw "Incomplete self-contained output: missing $fileName"
        }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $OutputDirectory 'README.md')
    $publishedDocs = Join-Path $OutputDirectory 'docs'
    New-Item -ItemType Directory -Path $publishedDocs -Force | Out-Null
    foreach ($document in @('STATUS.md', 'ACCEPTANCE.md', 'TASKS.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot "docs/$document") -Destination (Join-Path $publishedDocs $document)
    }
    Write-Output "Published complete win-x64 directory: $([IO.Path]::GetFullPath($OutputDirectory))"
}
finally {
    Pop-Location
}
