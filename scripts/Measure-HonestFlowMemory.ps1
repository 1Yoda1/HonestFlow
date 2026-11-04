[CmdletBinding()]
param(
    [int]$ProcessId,
    [ValidateRange(1, 1440)][int]$DurationMinutes = 15,
    [ValidateRange(1, 60)][int]$IntervalSeconds = 5,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ($ProcessId) {
    $targetProcess = Get-Process -Id $ProcessId
} else {
    $matches = @(Get-Process -Name HonestFlow -ErrorAction SilentlyContinue)
    if ($matches.Count -ne 1) {
        throw 'Start HonestFlow first. If several copies are running, specify -ProcessId.'
    }
    $targetProcess = $matches[0]
}
if ($targetProcess.ProcessName -ne 'HonestFlow') {
    $targetProcess.Dispose()
    throw 'The selected process is not HonestFlow.'
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot ('..\.build\memory\HonestFlow-{0}.csv' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'Output file already exists. Choose another -OutputPath.' }
$null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($OutputPath)) -Force
$deadlineUtc = [DateTime]::UtcNow.AddMinutes($DurationMinutes)
Write-Host "Monitoring HonestFlow PID $($targetProcess.Id). CSV: $OutputPath"
Write-Host 'Keep using the application normally. Ctrl+C stops the measurement; saved samples remain.'

try {
    do {
        $targetProcess.Refresh()
        if ($targetProcess.HasExited) { break }
        $sample = [pscustomobject]@{
            TimestampUtc = [DateTime]::UtcNow.ToString('o')
            ProcessId = $targetProcess.Id
            WorkingSetMiB = [Math]::Round($targetProcess.WorkingSet64 / 1MB, 2)
            PrivateMiB = [Math]::Round($targetProcess.PrivateMemorySize64 / 1MB, 2)
            Handles = $targetProcess.HandleCount
            Threads = $targetProcess.Threads.Count
            CpuSeconds = [Math]::Round($targetProcess.TotalProcessorTime.TotalSeconds, 2)
        }
        $sample | Export-Csv -LiteralPath $OutputPath -Append -NoTypeInformation -Encoding UTF8
        Write-Host ('RAM {0,8:N2} MiB | Private {1,8:N2} MiB | Handles {2} | Threads {3}' -f
            $sample.WorkingSetMiB, $sample.PrivateMiB, $sample.Handles, $sample.Threads)
        if ([DateTime]::UtcNow -lt $deadlineUtc) { Start-Sleep -Seconds $IntervalSeconds }
    } while ([DateTime]::UtcNow -lt $deadlineUtc)
} finally {
    $targetProcess.Dispose()
    Write-Host "Samples saved: $OutputPath"
}
