param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "HonestFlow.WpfPrototype\HonestFlow.WpfPrototype.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts\HonestFlow"
$currentDir = Join-Path $artifactsRoot "current"
$versionsDir = Join-Path $artifactsRoot "versions"

[xml]$project = Get-Content -LiteralPath $projectPath
$version = $project.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version is not set in the WPF HonestFlow project."
}

$versionDir = Join-Path $versionsDir $version

if (-not $SkipTests) {
    dotnet test (Join-Path $repoRoot "HonestFlow.slnx") -c $Configuration --no-restore /p:UseSharedCompilation=false
}

if (Test-Path -LiteralPath $currentDir) {
    Remove-Item -LiteralPath $currentDir -Recurse -Force
}
if (Test-Path -LiteralPath $versionDir) {
    Remove-Item -LiteralPath $versionDir -Recurse -Force
}

New-Item -ItemType Directory -Path $currentDir -Force | Out-Null
New-Item -ItemType Directory -Path $versionDir -Force | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --no-restore `
    -o $currentDir `
    /p:UseSharedCompilation=false

Copy-Item -Path (Join-Path $currentDir "*") -Destination $versionDir -Recurse -Force

$exePath = Join-Path $currentDir "HonestFlow.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Publish finished, but HonestFlow.exe was not found at $exePath."
}

$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash
$manifest = [ordered]@{
    Product = "HonestFlow"
    Version = $version
    Runtime = $Runtime
    Configuration = $Configuration
    PublishedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    CurrentPath = $currentDir
    VersionPath = $versionDir
    Exe = "HonestFlow.exe"
    Sha256 = $sha256
}

$manifestJson = $manifest | ConvertTo-Json
Set-Content -LiteralPath (Join-Path $currentDir "publish-manifest.json") -Value $manifestJson -Encoding UTF8
Set-Content -LiteralPath (Join-Path $versionDir "publish-manifest.json") -Value $manifestJson -Encoding UTF8

Write-Host "HonestFlow published."
Write-Host "Current: $currentDir"
Write-Host "Version: $versionDir"
Write-Host "SHA256:  $sha256"
