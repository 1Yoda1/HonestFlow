param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "HonestFlow.csproj"
$publishDir = Join-Path $repoRoot "bin\Release"

if (-not $SkipTests) {
    dotnet test (Join-Path $repoRoot "HonestFlow.slnx") -c $Configuration --no-restore /p:UseSharedCompilation=false
}

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --no-restore `
    -o $publishDir `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    /p:UseSharedCompilation=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exePath = Join-Path $publishDir "HonestFlow.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Publish finished, but HonestFlow.exe was not found at $exePath."
}

Write-Host "HonestFlow published."
Write-Host "Output: $publishDir"
