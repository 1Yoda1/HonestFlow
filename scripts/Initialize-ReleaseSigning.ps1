#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$KeyDirectory = (Join-Path $env:LOCALAPPDATA "HonestFlowReleaseSigning"),
    [string]$KeyFileName = "release.hfrelkey"
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository "tools\HonestFlow.ReleaseSigning\HonestFlow.ReleaseSigning.csproj"
$keyPath = Join-Path $KeyDirectory $KeyFileName

if (Test-Path -LiteralPath $keyPath) {
    throw "Release key already exists: $keyPath"
}

New-Item -ItemType Directory -Path $KeyDirectory -Force | Out-Null
Write-Host "The password will protect the release key. Keep it separately from the key file." -ForegroundColor Yellow
& dotnet run --project $project --configuration Release -- keygen $keyPath
if ($LASTEXITCODE -ne 0) {
    throw "Release key generation failed with exit code $LASTEXITCODE."
}

$publicPath = "$keyPath.public.json"
if (-not (Test-Path -LiteralPath $keyPath) -or -not (Test-Path -LiteralPath $publicPath)) {
    throw "Release key generation did not produce the expected files."
}

Write-Host "Release signing initialized." -ForegroundColor Green
Write-Host "Encrypted private key: $keyPath"
Write-Host "Public server/client key: $publicPath"
