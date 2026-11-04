#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
param(
    [string]$Directory = "C:\Work\Distributives\HonestFlow-components",
    [string]$KeyPath = (Join-Path $env:LOCALAPPDATA "HonestFlowReleaseSigning\release.hfrelkey"),
    [string]$ApiBase = "https://api.honestflow.ru",
    [string]$LmVersion = "2.6.3-4",
    [string]$AtolVersion = "10.10.8.24",
    [string]$EsmVersion = "1.6.3.2",
    [string]$ControllerVersion = "1.6.3.2",
    [switch]$SetCurrent
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository "tools\HonestFlow.ReleaseSigning\HonestFlow.ReleaseSigning.csproj"

$requiredFiles = @(
    (Join-Path $Directory "regime-$LmVersion.msi"),
    (Join-Path $Directory "KKT10-$AtolVersion-windows64-setup-signed.exe"),
    (Join-Path $Directory "KKT10-$AtolVersion-windows32-setup-signed.exe"),
    (Join-Path $Directory "esm_$EsmVersion-windows-signed-setup.exe"),
    (Join-Path $Directory "esm-lm-controller_$ControllerVersion-windows-setup.exe")
)

if (-not (Test-Path -LiteralPath $KeyPath -PathType Leaf)) {
    throw "Release key was not found: $KeyPath"
}

foreach ($file in $requiredFiles) {
    $item = Get-Item -LiteralPath $file -ErrorAction SilentlyContinue
    if ($null -eq $item -or $item.Length -le 0) {
        throw "Required release file is missing or empty: $file"
    }
}

Write-Host "All five release files are present:" -ForegroundColor Green
$requiredFiles | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    Write-Host ("  {0} ({1:N1} MB)" -f $item.Name, ($item.Length / 1MB))
}
Write-Host "Uploads are sequential and may take a long time on a slow connection." -ForegroundColor Yellow
Write-Host "The release-key password and admin key will be requested only once."

$operation = if ($SetCurrent) {
    "Upload five signed component releases and make their versions current"
} else {
    "Upload five signed component releases without changing current versions"
}
if (-not $PSCmdlet.ShouldProcess($ApiBase, $operation)) {
    return
}

$dotnetArguments = @(
    "run", "--project", $project, "--configuration", "Release", "--",
    "publish-components", $KeyPath, $Directory, $LmVersion, $AtolVersion,
    $EsmVersion, $ControllerVersion, $ApiBase
)
if ($SetCurrent) {
    $dotnetArguments += "--set-current"
}

& dotnet @dotnetArguments
if ($LASTEXITCODE -ne 0) {
    throw "Component release publication failed with exit code $LASTEXITCODE."
}

Write-Host "All component releases were published successfully." -ForegroundColor Green
