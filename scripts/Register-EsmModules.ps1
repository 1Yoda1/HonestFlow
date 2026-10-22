#requires -Version 5.1

<#+
.SYNOPSIS
Registers the first KKT currently exposed by the local ESM API.

.DESCRIPTION
The script uses the local ESM API at 127.0.0.1:51077, obtains the first KKT
serial number from GET /api/v1/dkktList and submits it to POST /api/v1/tspiot.

It does not open or configure a KKT connection. Registration is considered
successful only after the Windows service esm-cm-<serial> exists and
GET /api/v1/status/<instanceId> reports state "Зарегистрирован" with the same
regData.kktSerial. Instance ids are discovered through /instances/info.

.EXAMPLE
powershell.exe -ExecutionPolicy Bypass -File .\Register-EsmModules.ps1

.EXAMPLE
powershell.exe -ExecutionPolicy Bypass -File .\Register-EsmModules.ps1 -WhatIf

.EXAMPLE
powershell.exe -ExecutionPolicy Bypass -File .\Register-EsmModules.ps1 -ShowSerialNumbers -ReportPath .\esm-registration.csv
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter()]
    [ValidateRange(1, 600)]
    [int]$ServiceWaitSeconds = 60,

    [Parameter()]
    [ValidateRange(1, 600)]
    [int]$ConfirmationWaitSeconds = 60,

    [Parameter()]
    [ValidateRange(1, 30)]
    [int]$PollIntervalSeconds = 2,

    [Parameter()]
    [ValidateRange(1, 60)]
    [int]$HttpTimeoutSeconds = 5,

    [Parameter()]
    [switch]$ShowSerialNumbers,

    [Parameter()]
    [string]$ReportPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Write-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [Parameter()][ConsoleColor]$Color
    )

    Write-Verbose "[$([DateTime]::Now.ToString('HH:mm:ss'))] $Message"
}

function Get-DisplaySerial {
    param([Parameter(Mandatory = $true)][string]$Serial)

    if ($ShowSerialNumbers) {
        return $Serial
    }

    if ($Serial.Length -le 4) {
        return '****'
    }

    return ('*' * ($Serial.Length - 4)) + $Serial.Substring($Serial.Length - 4)
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)][AllowNull()]$InputObject,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Invoke-EsmGet {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$Uri
    )

    $timeout = [System.Threading.CancellationTokenSource]::new()
    $response = $null
    $requestPath = ([Uri]$Uri).PathAndQuery
    $startedAt = [DateTime]::UtcNow
    try {
        Write-Step "GET $requestPath — отправляем запрос."
        $timeout.CancelAfter([TimeSpan]::FromSeconds($HttpTimeoutSeconds))
        $response = $Client.GetAsync($Uri, $timeout.Token).GetAwaiter().GetResult()
        $elapsed = [int]([DateTime]::UtcNow - $startedAt).TotalMilliseconds
        Write-Step "GET $requestPath — HTTP $([int]$response.StatusCode), $elapsed мс."
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $json = $null
        if (-not [string]::IsNullOrWhiteSpace($content)) {
            try {
                $json = $content | ConvertFrom-Json
            }
            catch {
                throw "ESM вернул некорректный JSON для GET ${Uri}: $($_.Exception.Message)"
            }
        }

        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            IsSuccess  = $response.IsSuccessStatusCode
            Json       = $json
        }
    }
    catch {
        $elapsed = [int]([DateTime]::UtcNow - $startedAt).TotalMilliseconds
        Write-Step "GET $requestPath — ошибка через $elapsed мс: $($_.Exception.Message)" -Color Red
        throw
    }
    finally {
        if ($null -ne $response) {
            $response.Dispose()
        }
        $timeout.Dispose()
    }
}

function Get-KktSerials {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$BaseUri
    )

    $result = Invoke-EsmGet -Client $Client -Uri ($BaseUri + 'dkktList')
    if (-not $result.IsSuccess) {
        throw "GET /api/v1/dkktList вернул HTTP $($result.StatusCode)."
    }

    $kkt = Get-ObjectPropertyValue -InputObject $result.Json -Name 'kkt'
    if ($null -eq $kkt) {
        $data = Get-ObjectPropertyValue -InputObject $result.Json -Name 'data'
        if ($null -ne $data) {
            $kkt = Get-ObjectPropertyValue -InputObject $data -Name 'kkt'
        }
    }

    $items = @($kkt)
    if ($items.Count -eq 0 -or $null -eq $items[0]) {
        return @()
    }

    $serialValue = Get-ObjectPropertyValue -InputObject $items[0] -Name 'kktSerial'
    $serial = ([string]$serialValue).Trim()
    if (-not [string]::IsNullOrWhiteSpace($serial)) {
        return @($serial)
    }

    return @()
}

function Get-EsmInstanceIds {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$BaseUri
    )

    $result = Invoke-EsmGet -Client $Client -Uri ($BaseUri + 'instances/info')
    if ($result.StatusCode -eq 204) {
        return @()
    }
    if (-not $result.IsSuccess) {
        throw "GET /api/v1/instances/info вернул HTTP $($result.StatusCode)."
    }

    $instances = $null
    if ($result.Json -is [System.Array]) {
        $instances = $result.Json
    }
    else {
        $instances = Get-ObjectPropertyValue -InputObject $result.Json -Name 'instances'
        if ($null -eq $instances -and $null -ne (Get-ObjectPropertyValue -InputObject $result.Json -Name 'id')) {
            $instances = @($result.Json)
        }
    }

    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($instance in @($instances)) {
        if ($null -eq $instance) {
            continue
        }

        $idValue = Get-ObjectPropertyValue -InputObject $instance -Name 'id'
        $id = ([string]$idValue).Trim()
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            [void]$ids.Add($id)
        }
    }

    return @($ids)
}

function Get-EsmRegistrationSnapshot {
    param([Parameter(Mandatory = $true)]$Json)

    $queue = [System.Collections.Queue]::new()
    $queue.Enqueue([pscustomobject]@{ Value = $Json; Depth = 0; State = $null })

    while ($queue.Count -gt 0) {
        $entry = $queue.Dequeue()
        $candidate = $entry.Value
        if ($null -eq $candidate -or $entry.Depth -gt 8) {
            continue
        }

        if ($candidate -is [string]) {
            $text = $candidate.Trim()
            if (($text.StartsWith('{') -and $text.EndsWith('}')) -or
                ($text.StartsWith('[') -and $text.EndsWith(']'))) {
                try {
                    $nestedJson = $text | ConvertFrom-Json
                    $queue.Enqueue([pscustomobject]@{
                        Value = $nestedJson
                        Depth = $entry.Depth + 1
                        State = $entry.State
                    })
                }
                catch {
                    # This string only looked like JSON; keep searching other fields.
                }
            }
            continue
        }

        if ($candidate -is [System.Collections.IEnumerable] -and
            -not ($candidate -is [System.Management.Automation.PSCustomObject])) {
            foreach ($item in $candidate) {
                $queue.Enqueue([pscustomobject]@{
                    Value = $item
                    Depth = $entry.Depth + 1
                    State = $entry.State
                })
            }
            continue
        }

        $stateValue = Get-ObjectPropertyValue -InputObject $candidate -Name 'state'
        $effectiveState = if ($null -ne $stateValue) { ([string]$stateValue).Trim() } else { $entry.State }
        $regData = Get-ObjectPropertyValue -InputObject $candidate -Name 'regData'
        if ($null -ne $regData) {
            return [pscustomobject]@{
                State          = ([string]$effectiveState).Trim()
                KktSerial      = ([string](Get-ObjectPropertyValue -InputObject $regData -Name 'kktSerial')).Trim()
                TspiotId       = ([string](Get-ObjectPropertyValue -InputObject $regData -Name 'tspiotId')).Trim()
                GismtTspiotId  = ([string](Get-ObjectPropertyValue -InputObject $regData -Name 'gismtTspiotId')).Trim()
            }
        }

        foreach ($property in $candidate.PSObject.Properties) {
            $child = $property.Value
            if ($null -eq $child -or $child -is [ValueType]) {
                continue
            }
            if ($child -is [string]) {
                $trimmedChild = $child.Trim()
                if (-not (($trimmedChild.StartsWith('{') -and $trimmedChild.EndsWith('}')) -or
                    ($trimmedChild.StartsWith('[') -and $trimmedChild.EndsWith(']')))) {
                    continue
                }
            }

            $queue.Enqueue([pscustomobject]@{
                Value = $child
                Depth = $entry.Depth + 1
                State = $effectiveState
            })
        }
    }

    return $null
}

function Test-EsmRegistration {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$BaseUri,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    $displaySerial = Get-DisplaySerial -Serial $Serial
    Write-Step "[$displaySerial] Получаем список экземпляров ESM."
    $instanceIds = @(Get-EsmInstanceIds -Client $Client -BaseUri $BaseUri)
    if ($instanceIds.Count -eq 0) {
        Write-Step "[$displaySerial] /instances/info не содержит экземпляров."
        return [pscustomobject]@{ Success = $false; Message = '/instances/info пока не содержит экземпляров.' }
    }

    Write-Step "[$displaySerial] Найдено экземпляров ESM: $($instanceIds.Count). Проверяем их статусы."
    $lastState = $null
    $instanceNumber = 0
    foreach ($instanceId in $instanceIds) {
        $instanceNumber++
        $escapedId = [Uri]::EscapeDataString($instanceId)
        Write-Step "[$displaySerial] Проверяем экземпляр $instanceNumber из $($instanceIds.Count)."
        $statusResult = Invoke-EsmGet -Client $Client -Uri ($BaseUri + 'status/' + $escapedId)
        if ($statusResult.StatusCode -eq 204) {
            $lastState = "status/$escapedId вернул HTTP 204"
            Write-Step "[$displaySerial] Экземпляр $instanceNumber пока не вернул статус."
            continue
        }
        if (-not $statusResult.IsSuccess) {
            $lastState = "status/$escapedId вернул HTTP $($statusResult.StatusCode)"
            Write-Step "[$displaySerial] Экземпляр $instanceNumber вернул HTTP $($statusResult.StatusCode)."
            continue
        }
        if ($null -eq $statusResult.Json) {
            $lastState = "status/$escapedId вернул пустой ответ"
            Write-Step "[$displaySerial] Экземпляр $instanceNumber вернул пустой ответ."
            continue
        }

        $snapshot = Get-EsmRegistrationSnapshot -Json $statusResult.Json
        if ($null -eq $snapshot) {
            $lastState = "status/$escapedId не содержит state/regData"
            Write-Step "[$displaySerial] Экземпляр $instanceNumber не содержит state/regData."
            continue
        }
        if (-not [string]::Equals($snapshot.KktSerial, $Serial, [StringComparison]::Ordinal)) {
            $otherSerial = if ([string]::IsNullOrWhiteSpace($snapshot.KktSerial)) { 'не указан' } else { Get-DisplaySerial -Serial $snapshot.KktSerial }
            Write-Step "[$displaySerial] Экземпляр $instanceNumber относится к другой ККТ: $otherSerial."
            continue
        }

        Write-Step "[$displaySerial] Найден статус нужной ККТ: state='$($snapshot.State)'."
        if ([string]::Equals($snapshot.State, 'Зарегистрирован', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($snapshot.State, 'Registered', [StringComparison]::OrdinalIgnoreCase)) {
            Write-Step "[$displaySerial] Регистрация подтверждена." -Color Green
            return [pscustomobject]@{
                Success = $true
                Message = 'Статус ESM подтверждает регистрацию нужной ККТ.'
            }
        }

        return [pscustomobject]@{
            Success = $false
            Message = "Для нужной ККТ получен state='$($snapshot.State)'."
        }
    }

    if ([string]::IsNullOrWhiteSpace($lastState)) {
        $lastState = 'ни один status/<id> не относится к нужному kktSerial'
    }
    return [pscustomobject]@{ Success = $false; Message = $lastState }
}

function Test-ServiceExists {
    param([Parameter(Mandatory = $true)][string]$Name)

    return $null -ne (Get-Service -Name $Name -ErrorAction SilentlyContinue)
}

function Wait-ForRegistrationConfirmation {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$BaseUri,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($ConfirmationWaitSeconds)
    $lastError = $null
    $attempt = 0
    $displaySerial = Get-DisplaySerial -Serial $Serial
    Write-Step "[$displaySerial] Начинаем подтверждение регистрации. Таймаут: $ConfirmationWaitSeconds сек."
    do {
        $attempt++
        Write-Step "[$displaySerial] Проверка регистрации, попытка $attempt."
        try {
            $registration = Test-EsmRegistration -Client $Client -BaseUri $BaseUri -Serial $Serial
            if ($registration.Success) {
                return $registration
            }
            $lastError = $registration.Message
        }
        catch {
            $lastError = $_.Exception.Message
        }

        if ([DateTime]::UtcNow -lt $deadline) {
            Write-Step "[$displaySerial] Подтверждения пока нет: $lastError. Следующая проверка через $PollIntervalSeconds сек."
            Start-Sleep -Seconds $PollIntervalSeconds
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    return [pscustomobject]@{
        Success = $false
        Message = "За $ConfirmationWaitSeconds сек. /status/<id> не подтвердил регистрацию: $lastError"
    }
}

function Register-EsmModule {
    param(
        [Parameter(Mandatory = $true)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory = $true)][string]$BaseUri,
        [Parameter(Mandatory = $true)][string]$Serial
    )

    $serviceName = 'esm-cm-' + $Serial
    $body = @{ id = $Serial } | ConvertTo-Json -Compress
    $content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, ($BaseUri + 'tspiot'))
    $request.Content = $content
    $postCancellation = [System.Threading.CancellationTokenSource]::new()
    $postResponse = $null
    $displaySerial = Get-DisplaySerial -Serial $Serial
    $displayServiceName = 'esm-cm-' + $displaySerial

    try {
        Write-Step "[$displaySerial] POST /api/v1/tspiot — отправляем JSON {`"id`":`"$displaySerial`"}."
        $postTask = $Client.SendAsync($request, $postCancellation.Token)
        $postWasChecked = $false
        $serviceWaitStarted = [DateTime]::UtcNow
        $serviceDeadline = [DateTime]::UtcNow.AddSeconds($ServiceWaitSeconds)
        $serviceAppeared = $false
        $lastReportedSecond = -1
        Write-Step "[$displaySerial] POST отправлен. Ждём службу $displayServiceName до $ServiceWaitSeconds сек."

        do {
            if (Test-ServiceExists -Name $serviceName) {
                $serviceAppeared = $true
                $serviceElapsed = [int]([DateTime]::UtcNow - $serviceWaitStarted).TotalSeconds
                Write-Step "[$displaySerial] Служба $displayServiceName обнаружена через $serviceElapsed сек." -Color Green
                break
            }

            if ($postTask.IsCompleted -and -not $postWasChecked) {
                $postResponse = $postTask.GetAwaiter().GetResult()
                $postWasChecked = $true
                Write-Step "[$displaySerial] POST /api/v1/tspiot завершился с HTTP $([int]$postResponse.StatusCode)."
                if (-not $postResponse.IsSuccessStatusCode) {
                    return [pscustomobject]@{
                        Success = $false
                        Message = "POST /api/v1/tspiot вернул HTTP $([int]$postResponse.StatusCode)."
                    }
                }
                Write-Step "[$displaySerial] ESM принял запрос. Продолжаем ждать создание службы."
            }

            $elapsedSeconds = [int]([DateTime]::UtcNow - $serviceWaitStarted).TotalSeconds
            if ($elapsedSeconds -eq 0 -or ($elapsedSeconds % 5 -eq 0 -and $elapsedSeconds -ne $lastReportedSecond)) {
                $lastReportedSecond = $elapsedSeconds
                Write-Step "[$displaySerial] Служба ещё не появилась. Прошло $elapsedSeconds из $ServiceWaitSeconds сек."
            }
            if ([DateTime]::UtcNow -lt $serviceDeadline) {
                Start-Sleep -Seconds 1
            }
        } while ([DateTime]::UtcNow -lt $serviceDeadline)

        if (-not $serviceAppeared) {
            $postCancellation.Cancel()
            Write-Step "[$displaySerial] Таймаут ожидания службы $displayServiceName." -Color Red
            return [pscustomobject]@{
                Success = $false
                Message = "За $ServiceWaitSeconds сек. не появилась служба $serviceName."
            }
        }

        if (-not $postTask.IsCompleted) {
            Write-Step "[$displaySerial] Служба уже создана, но POST ещё открыт. Прекращаем ждать HTTP-ответ; регистрацию ESM это не отменяет."
            $postCancellation.Cancel()
            try {
                [void]$postTask.Wait(2000)
            }
            catch {
                # The ESM request can remain open while the service is being created.
                # Service appearance plus the read-only confirmation below are authoritative.
            }
        }
        elseif (-not $postWasChecked) {
            $postResponse = $postTask.GetAwaiter().GetResult()
            $postWasChecked = $true
            Write-Step "[$displaySerial] POST /api/v1/tspiot завершился с HTTP $([int]$postResponse.StatusCode)."
            if (-not $postResponse.IsSuccessStatusCode) {
                return [pscustomobject]@{
                    Success = $false
                    Message = "POST /api/v1/tspiot вернул HTTP $([int]$postResponse.StatusCode)."
                }
            }
        }

        Write-Step "[$displaySerial] Переходим к проверке /instances/info и /status/<id>."
        return Wait-ForRegistrationConfirmation -Client $Client -BaseUri $BaseUri -Serial $Serial
    }
    catch [System.OperationCanceledException] {
        return [pscustomobject]@{ Success = $false; Message = 'POST /api/v1/tspiot был отменён или превысил таймаут.' }
    }
    catch {
        return [pscustomobject]@{ Success = $false; Message = "Ошибка POST /api/v1/tspiot: $($_.Exception.Message)" }
    }
    finally {
        if ($null -ne $postResponse) {
            $postResponse.Dispose()
        }
        $postCancellation.Dispose()
        $request.Dispose()
        $content.Dispose()
    }
}

Add-Type -AssemblyName System.Net.Http
$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [System.Threading.Timeout]::InfiniteTimeSpan
$results = [System.Collections.Generic.List[object]]::new()

try {
    $baseUri = 'http://127.0.0.1:51077/api/v1/'
    Write-Step 'Запуск регистрации модулей ESM.'
    Write-Step "ESM API: $baseUri"
    Write-Step 'Получаем список ККТ из /dkktList.'

    $serials = @(Get-KktSerials -Client $httpClient -BaseUri $baseUri)
    if ($serials.Count -eq 0) {
        throw 'ESM не вернул ни одного kktSerial в /dkktList. Проверьте уже открытое соединение с ККТ.'
    }

    Write-Step 'Первая ККТ из /dkktList получена.'
    $kktNumber = 0
    foreach ($serial in $serials) {
        $kktNumber++
        $displaySerial = Get-DisplaySerial -Serial $serial
        Write-Step "[$displaySerial] ККТ $kktNumber из $($serials.Count)."
        Write-Step "[$displaySerial] Проверяем, зарегистрирована ли ККТ ранее."
        try {
            $existingRegistration = Test-EsmRegistration -Client $httpClient -BaseUri $baseUri -Serial $serial
            if ($existingRegistration.Success) {
                Write-Step "[$displaySerial] Уже зарегистрирован. POST отправляться не будет." -Color Green
                $results.Add([pscustomobject]@{
                    Serial = $displaySerial
                    Status = 'AlreadyRegistered'
                    Message = $existingRegistration.Message
                })
                continue
            }
            Write-Step "[$displaySerial] Действующая регистрация не подтверждена: $($existingRegistration.Message)"
        }
        catch {
            Write-Step "[$displaySerial] Не удалось проверить исходный статус: $($_.Exception.Message)" -Color Yellow
        }

        if (-not $PSCmdlet.ShouldProcess("ККТ $displaySerial", 'Зарегистрировать модуль ESM')) {
            $results.Add([pscustomobject]@{
                Serial = $displaySerial
                Status = 'Skipped'
                Message = 'Операция пропущена.'
            })
            continue
        }

        Write-Step "[$displaySerial] Начинаем новую регистрацию."
        $registration = Register-EsmModule -Client $httpClient -BaseUri $baseUri -Serial $serial
        if ($registration.Success) {
            Write-Step "[$displaySerial] Успешно: $($registration.Message)" -Color Green
            $status = 'Registered'
        }
        else {
            Write-Step "[$displaySerial] Ошибка: $($registration.Message)" -Color Red
            $status = 'Failed'
        }

        $results.Add([pscustomobject]@{
            Serial = $displaySerial
            Status = $status
            Message = $registration.Message
        })
    }

    Write-Host ''
    Write-Host 'Итог:'
    $results | Format-Table -AutoSize

    if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
        $resolvedReportPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath)
        $reportDirectory = Split-Path -Parent $resolvedReportPath
        if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
            [void][IO.Directory]::CreateDirectory($reportDirectory)
        }
        $results | Export-Csv -LiteralPath $resolvedReportPath -NoTypeInformation -Encoding UTF8
        Write-Host "Отчёт сохранён: $resolvedReportPath"
    }

    if (@($results | Where-Object { $_.Status -eq 'Failed' }).Count -gt 0) {
        exit 1
    }
    exit 0
}
catch {
    Write-Host "Ошибка: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
finally {
    $httpClient.Dispose()
}
