param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [string]$DotNetRuntimeVersion = "10.0.10",
    [string]$DotNetRuntimeUrl = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.10/windowsdesktop-runtime-10.0.10-win-x64.exe",
    [string]$DotNetRuntimeSha256 = "E82FC901C8F52D716293B2BC0830CE0DD254A06268C457A19E8FC503560A84D1"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "HonestFlow.csproj"
$publishScript = Join-Path $PSScriptRoot "publish-honestflow.ps1"
$publishDir = Join-Path $repoRoot "bin\Release"
$outputDir = Join-Path $repoRoot "bin\Release"
$installerScript = Join-Path $repoRoot "installer\HonestFlow.Web.iss"

if ($Runtime -ne "win-x64") {
    throw "The Web Setup currently supports only win-x64."
}

if ($DotNetRuntimeSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
    throw "DotNetRuntimeSha256 must contain exactly 64 hexadecimal characters."
}

if (-not [Uri]::IsWellFormedUriString($DotNetRuntimeUrl, [UriKind]::Absolute) -or
    -not $DotNetRuntimeUrl.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
    throw "DotNetRuntimeUrl must be an absolute HTTPS URL."
}

[xml]$project = Get-Content -LiteralPath $projectPath
$version = $project.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version is not set in HonestFlow.csproj."
}

& $publishScript `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -SkipTests:$SkipTests

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 7\ISCC.exe")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$isccPath = $isccCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    $isccCommand = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($isccCommand) {
        $isccPath = $isccCommand.Source
    }
}

if ([string]::IsNullOrWhiteSpace($isccPath)) {
    throw "Inno Setup Compiler was not found. Install JRSoftware.InnoSetup with winget."
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

& $isccPath `
    "/DMyAppVersion=$version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$outputDir" `
    "/DDotNetRuntimeVersion=$DotNetRuntimeVersion" `
    "/DDotNetRuntimeUrl=$DotNetRuntimeUrl" `
    "/DDotNetRuntimeSha256=$($DotNetRuntimeSha256.ToUpperInvariant())" `
    $installerScript

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup Compiler failed with exit code $LASTEXITCODE."
}

$setupPath = Join-Path $outputDir "HonestFlow-Web-Setup-$version.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Installer compilation succeeded, but output was not found: $setupPath"
}

$setupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $setupPath).Hash
$setupInfo = Get-Item -LiteralPath $setupPath

Write-Host "HonestFlow Web Setup built."
Write-Host "Path:    $setupPath"
Write-Host "Size:    $($setupInfo.Length) bytes"
Write-Host "SHA256:  $setupHash"
Write-Host "Runtime: Microsoft .NET Desktop Runtime $DotNetRuntimeVersion x64"
