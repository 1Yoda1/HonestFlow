//go:build windows

package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"net/netip"
	"strconv"
	"strings"
	"time"
)

const dotNetDirectTimeout = 30 * time.Second

var dotNetTLSModes = []string{"systemDefault", "tls12", "tls13"}

const dotNetDirectProbeScript = `$ErrorActionPreference='Stop'
$ipText=[Environment]::GetEnvironmentVariable('GONETWORKPROBE_IP','Process')
$h=[Environment]::GetEnvironmentVariable('GONETWORKPROBE_HOST','Process')
$port=[int]::Parse([Environment]::GetEnvironmentVariable('GONETWORKPROBE_PORT','Process'),[Globalization.CultureInfo]::InvariantCulture)
$tls13PlatformSupported=([Environment]::GetEnvironmentVariable('GONETWORKPROBE_TLS13_SUPPORTED','Process') -eq '1')
$address=[System.Net.IPAddress]::Parse($ipText)

function Encode-Text([string]$value) {
  if ($null -eq $value) { $value='' }
  return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value))
}

function Get-ExceptionDiagnostic([Exception]$exception) {
  $innerTypes=@()
  $messages=@()
  $socketError=''
  $nativeError=0
  $hasAuthentication=$false
  $hasIO=$false
  $hasSocket=$false
  $hasWin32=$false
  $current=$exception
  $first=$true
  while ($null -ne $current) {
    if (-not $first) { $innerTypes+=@($current.GetType().FullName) }
    if (-not [String]::IsNullOrWhiteSpace($current.Message)) { $messages+=@($current.Message) }
    if ($current -is [System.Security.Authentication.AuthenticationException]) { $hasAuthentication=$true }
    if ($current -is [System.IO.IOException]) { $hasIO=$true }
    if ($current -is [System.Net.Sockets.SocketException]) {
      $hasSocket=$true
      $socketError=$current.SocketErrorCode.ToString()
    }
    if ($current -is [System.ComponentModel.Win32Exception]) {
      $hasWin32=$true
      $nativeError=$current.NativeErrorCode
    }
    $current=$current.InnerException
    $first=$false
  }
  $category='exception'
  if ($hasAuthentication) { $category='authentication_exception' }
  elseif ($hasIO) { $category='io_exception' }
  elseif ($hasSocket) { $category='socket_exception' }
  elseif ($hasWin32) { $category='win32_exception' }
  return [ordered]@{
    exceptionType=$exception.GetType().FullName
    innerExceptionTypes=@($innerTypes)
    hresult=$exception.HResult.ToString([Globalization.CultureInfo]::InvariantCulture)
    socketErrorCode=$socketError
    nativeErrorCode=$nativeError
    category=$category
    safeMessage=($messages -join ' | ')
  }
}

function New-TlsResult([string]$mode) {
  return [ordered]@{
    ip=$ipText
    mode=$mode
    status='fail'
    phase='unknown'
    protocol=''
    handshakeDurationMs=0
    certificateReceived=$false
    certificate=$null
    policyErrors=@()
    chainStatus=@()
    category=''
    exception=$null
  }
}

function Invoke-TlsMode([string]$mode) {
  $result=New-TlsResult $mode
  if ($mode -eq 'tls13' -and ((-not $tls13PlatformSupported) -or (-not ([Enum]::GetNames([System.Security.Authentication.SslProtocols]) -contains 'Tls13')))) {
    $result.status='unsupported'
    $result.category='unsupported_by_runtime'
    return [PSCustomObject]$result
  }
  $client=$null
  $ssl=$null
  $certificateState=$null
  $timer=[System.Diagnostics.Stopwatch]::StartNew()
  try {
    $client=[System.Net.Sockets.TcpClient]::new($address.AddressFamily)
    $pending=$client.BeginConnect($address,$port,$null,$null)
    try {
      if (-not $pending.AsyncWaitHandle.WaitOne(3000)) {
        $result.category='dotnet_tcp_timeout'
        return [PSCustomObject]$result
      }
      $client.EndConnect($pending)
    } finally { $pending.AsyncWaitHandle.Close() }
    $result.phase='tcp_connected'
    $client.ReceiveTimeout=5000
    $client.SendTimeout=5000
    $stream=$client.GetStream()
    $stream.ReadTimeout=5000
    $stream.WriteTimeout=5000
    $certificateState=[hashtable]::Synchronized(@{ received=$false; certificate=$null; policyErrors=@(); chainStatus=@() })
    $callbackScript={
      param($sender,$certificate,$chain,$policyErrors)
      $certificateState.received=($null -ne $certificate)
      if ($null -ne $certificate) {
        $cert2=[System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)
        $sha=[System.Security.Cryptography.SHA256]::Create()
        try { $sha256=([BitConverter]::ToString($sha.ComputeHash($cert2.RawData))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
        $certificateState.certificate=[ordered]@{
          subject=$cert2.Subject
          issuer=$cert2.Issuer
          thumbprint=$cert2.Thumbprint
          sha256=$sha256
          spkiSha256=''
          dnsNames=@()
          notBefore=$cert2.NotBefore.ToUniversalTime().ToString('o',[Globalization.CultureInfo]::InvariantCulture)
          notAfter=$cert2.NotAfter.ToUniversalTime().ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        }
      }
      if ($policyErrors -ne [System.Net.Security.SslPolicyErrors]::None) {
        $certificateState.policyErrors=@($policyErrors.ToString().Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
      }
      if ($null -ne $chain) {
        $certificateState.chainStatus=@($chain.ChainStatus | ForEach-Object { $_.Status.ToString() } | Sort-Object -Unique)
      }
      return ($policyErrors -eq [System.Net.Security.SslPolicyErrors]::None)
    }.GetNewClosure()
    $callback=[System.Net.Security.RemoteCertificateValidationCallback]$callbackScript
    $ssl=[System.Net.Security.SslStream]::new($stream,$false,$callback)
    $result.phase='handshake_started'
    $timer.Restart()
    if ($mode -eq 'systemDefault') {
      $ssl.AuthenticateAsClient($h)
    } else {
      $protocol=[Enum]::Parse([System.Security.Authentication.SslProtocols],$(if ($mode -eq 'tls12') { 'Tls12' } else { 'Tls13' }))
      $ssl.AuthenticateAsClient($h,$null,$protocol,$false)
    }
    $result.status='ok'
    $result.phase='success'
    $result.protocol=$ssl.SslProtocol.ToString()
  } catch {
    $result.exception=Get-ExceptionDiagnostic $_.Exception
    $result.category=$result.exception.category
  } finally {
    $result.handshakeDurationMs=$timer.ElapsedMilliseconds
    if ($null -ne $certificateState) {
      $result.certificateReceived=[bool]$certificateState.received
      $result.certificate=$certificateState.certificate
      $result.policyErrors=@($certificateState.policyErrors)
      $result.chainStatus=@($certificateState.chainStatus)
    }
    if ($null -ne $ssl) { $ssl.Dispose() }
    if ($null -ne $client) { $client.Close() }
  }
  return [PSCustomObject]$result
}

$tcpClient=$null
$tcpTimer=[System.Diagnostics.Stopwatch]::StartNew()
try {
  $tcpClient=[System.Net.Sockets.TcpClient]::new($address.AddressFamily)
  $pendingTcp=$tcpClient.BeginConnect($address,$port,$null,$null)
  try {
    if (-not $pendingTcp.AsyncWaitHandle.WaitOne(3000)) {
      [Console]::Out.WriteLine(('GNP|DNTCP|FAIL|{0}|TimedOut' -f $tcpTimer.ElapsedMilliseconds))
    } else {
      $tcpClient.EndConnect($pendingTcp)
      [Console]::Out.WriteLine(('GNP|DNTCP|OK|{0}|{1}|{2}' -f $tcpTimer.ElapsedMilliseconds,$tcpClient.Client.LocalEndPoint.ToString(),$tcpClient.Client.RemoteEndPoint.ToString()))
    }
  } finally { $pendingTcp.AsyncWaitHandle.Close() }
} catch {
  $kind=$_.Exception.GetType().Name
  if ($_.Exception -is [System.Net.Sockets.SocketException]) { $kind=$_.Exception.SocketErrorCode.ToString() }
  [Console]::Out.WriteLine(('GNP|DNTCP|FAIL|{0}|{1}' -f $tcpTimer.ElapsedMilliseconds,$kind))
} finally {
  if ($null -ne $tcpClient) { $tcpClient.Close() }
}

foreach ($mode in @('systemDefault','tls12','tls13')) {
  $result=Invoke-TlsMode $mode
  $json=$result | ConvertTo-Json -Depth 7 -Compress
  [Console]::Out.WriteLine(('GNP|DNTLSJSON|{0}' -f (Encode-Text $json)))
}`

func buildDotNetDirectInvocation(ip, host string, port int) (powerShellInvocation, error) {
	if !allowedCRPT(host, port) {
		return powerShellInvocation{}, errors.New("host rejected by CRPT allowlist")
	}
	address, err := netip.ParseAddr(ip)
	if err != nil {
		return powerShellInvocation{}, errors.New("invalid literal IP")
	}
	encoded := encodePowerShellCommand(dotNetDirectProbeScript)
	return powerShellInvocation{
		path: windowsPowerShellPath,
		args: []string{"-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded},
		env: []string{
			"GONETWORKPROBE_IP=" + address.String(),
			"GONETWORKPROBE_HOST=" + host,
			"GONETWORKPROBE_PORT=" + strconv.Itoa(port),
			"GONETWORKPROBE_TLS13_SUPPORTED=" + boolEnvValue(windowsTLS13PlatformSupported()),
		},
	}, nil
}

func boolEnvValue(value bool) string {
	if value {
		return "1"
	}
	return "0"
}

func runDotNetDirect(ctx context.Context, ip string, endpoint CdnEndpoint) (TcpResult, []DotNetTLSResult) {
	tcp := TcpResult{IP: ip, Status: "fail", Error: "dotnet_process_failed"}
	invocation, err := buildDotNetDirectInvocation(ip, endpoint.Host, endpoint.Port)
	if err != nil {
		tcp.Error = "invalid_literal_endpoint"
		return tcp, missingDotNetTLSResults(ip, "invalid_literal_endpoint", false)
	}
	execution := executePowerShell(ctx, invocation, dotNetDirectTimeout)
	tcp, tlsResults := normalizeDotNetDirectOutput(execution.output, ip)
	if execution.unavailable {
		tcp.Status, tcp.Error = "fail", "powershell_unavailable"
		return tcp, missingDotNetTLSResults(ip, "powershell_unavailable", false)
	}
	if execution.timedOut {
		if tcp.Status != "ok" {
			tcp.Status, tcp.Error, tcp.DurationMS = "fail", "dotnet_tcp_timeout", execution.durationMS
		}
		tlsResults = completeDotNetTLSModes(ip, tlsResults, "dotnet_tls_timeout", true)
	} else {
		tlsResults = completeDotNetTLSModes(ip, tlsResults, "dotnet_process_failed", false)
	}
	return tcp, tlsResults
}

func normalizeDotNetDirectOutput(output, ip string) (TcpResult, []DotNetTLSResult) {
	tcp := TcpResult{IP: ip, Status: "fail", Error: "dotnet_process_failed"}
	var tlsResults []DotNetTLSResult
	for _, line := range strings.Split(strings.ReplaceAll(output, "\r", ""), "\n") {
		parts := strings.Split(strings.TrimSpace(line), "|")
		if len(parts) < 3 || parts[0] != "GNP" {
			continue
		}
		if parts[1] == "DNTCP" && len(parts) >= 5 {
			tcp.DurationMS, _ = strconv.ParseInt(parts[3], 10, 64)
			if parts[2] == "OK" && len(parts) == 6 {
				tcp.Status, tcp.Error, tcp.LocalAddress, tcp.RemoteAddress = "ok", "", parts[4], parts[5]
			} else if parts[2] == "FAIL" {
				tcp.Status, tcp.Error = "fail", normalizeDotNetTCPError(parts[4])
			}
			continue
		}
		if parts[1] != "DNTLSJSON" || len(parts) != 3 {
			continue
		}
		decoded, err := base64.StdEncoding.DecodeString(parts[2])
		if err != nil {
			continue
		}
		var result DotNetTLSResult
		if json.Unmarshal(decoded, &result) != nil || result.IP != ip || !validDotNetTLSMode(result.Mode) {
			continue
		}
		result = sanitizeAndClassifyDotNetTLS(result)
		tlsResults = append(tlsResults, result)
	}
	return tcp, tlsResults
}

func sanitizeAndClassifyDotNetTLS(result DotNetTLSResult) DotNetTLSResult {
	result.PolicyErrors = sortedUnique(result.PolicyErrors)
	result.ChainStatus = sortedUnique(result.ChainStatus)
	if result.PolicyErrors == nil {
		result.PolicyErrors = []string{}
	}
	if result.ChainStatus == nil {
		result.ChainStatus = []string{}
	}
	if result.Exception != nil {
		result.Exception.ExceptionType = sanitizePowerShellExcerpt(result.Exception.ExceptionType)
		for i := range result.Exception.InnerExceptionTypes {
			result.Exception.InnerExceptionTypes[i] = sanitizePowerShellExcerpt(result.Exception.InnerExceptionTypes[i])
		}
		result.Exception.InnerExceptionTypes = sortedUnique(result.Exception.InnerExceptionTypes)
		result.Exception.SafeMessage = sanitizePowerShellExcerpt(result.Exception.SafeMessage)
		result.Exception.SocketErrorCode = sanitizePowerShellExcerpt(result.Exception.SocketErrorCode)
		result.Exception.Category = sanitizePowerShellExcerpt(result.Exception.Category)
	}
	result.Phase, result.Category, result.Status = classifyDotNetTLSResult(result)
	return result
}

func classifyDotNetTLSResult(result DotNetTLSResult) (string, string, string) {
	if result.Status == "ok" {
		return "success", "", "ok"
	}
	if result.Status == "unsupported" || (result.Mode == "tls13" && dotNetExceptionContains(result.Exception, "PlatformNotSupportedException", "NotSupportedException")) {
		return "unknown", "unsupported_by_runtime", "unsupported"
	}
	if result.CertificateReceived && len(result.PolicyErrors) > 0 {
		return "certificate_validation_failed", "certificate_validation_failed", "fail"
	}
	if dotNetExceptionTimeout(result.Exception) || result.Category == "dotnet_tls_timeout" {
		return "handshake_timeout", "dotnet_tls_timeout", "fail"
	}
	if dotNetExceptionCredentialsUnavailable(result.Exception) {
		return "handshake_started", "dotnet_tls_credentials_unavailable", "fail"
	}
	if dotNetExceptionRemoteClosed(result.Exception) {
		return "remote_closed", "dotnet_tls_remote_closed", "fail"
	}
	if dotNetExceptionProtocolFailure(result.Exception) {
		return "protocol_negotiation_failed", "dotnet_tls_protocol_negotiation_failed", "fail"
	}
	if result.CertificateReceived {
		return "certificate_received", normalizedDotNetExceptionCategory(result.Exception), "fail"
	}
	if result.Phase == "tcp_connected" {
		return "tcp_connected", normalizedDotNetExceptionCategory(result.Exception), "fail"
	}
	return "handshake_started", normalizedDotNetExceptionCategory(result.Exception), "fail"
}

func normalizedDotNetExceptionCategory(exception *DotNetExceptionDiagnostic) string {
	if exception == nil || exception.Category == "" {
		return "dotnet_tls_other"
	}
	switch exception.Category {
	case "authentication_exception":
		return "dotnet_tls_authentication_failed"
	case "io_exception", "socket_exception", "win32_exception":
		return "dotnet_tls_transport_failure"
	default:
		return "dotnet_tls_other"
	}
}

func dotNetExceptionCredentialsUnavailable(exception *DotNetExceptionDiagnostic) bool {
	if exception == nil {
		return false
	}
	if exception.NativeErrorCode == -2146893042 { // SEC_E_NO_CREDENTIALS (0x8009030E)
		return true
	}
	return strings.Contains(strings.ToLower(exception.SafeMessage), "no credentials are available in the security package")
}

func dotNetExceptionContains(exception *DotNetExceptionDiagnostic, names ...string) bool {
	if exception == nil {
		return false
	}
	values := append([]string{exception.ExceptionType}, exception.InnerExceptionTypes...)
	for _, value := range values {
		for _, name := range names {
			if strings.Contains(value, name) {
				return true
			}
		}
	}
	return false
}

func dotNetExceptionTimeout(exception *DotNetExceptionDiagnostic) bool {
	if exception == nil {
		return false
	}
	value := strings.ToLower(exception.SocketErrorCode + " " + exception.SafeMessage)
	return strings.Contains(value, "timedout") || strings.Contains(value, "timeout") || strings.Contains(value, "timed out")
}

func dotNetExceptionRemoteClosed(exception *DotNetExceptionDiagnostic) bool {
	if exception == nil {
		return false
	}
	if exception.NativeErrorCode == 10054 {
		return true
	}
	value := strings.ToLower(exception.SocketErrorCode + " " + exception.SafeMessage)
	return strings.Contains(value, "connectionreset") || strings.Contains(value, "forcibly closed") || strings.Contains(value, "remote host closed")
}

func dotNetExceptionProtocolFailure(exception *DotNetExceptionDiagnostic) bool {
	if exception == nil {
		return false
	}
	value := strings.ToLower(exception.SafeMessage)
	return strings.Contains(value, "protocol version") || strings.Contains(value, "handshake failure") || strings.Contains(value, "no common algorithm") || strings.Contains(value, "algorithm mismatch")
}

func certificatePolicyAccepted(policyErrors []string) bool {
	return len(policyErrors) == 0
}

func validDotNetTLSMode(mode string) bool {
	for _, candidate := range dotNetTLSModes {
		if mode == candidate {
			return true
		}
	}
	return false
}

func completeDotNetTLSModes(ip string, existing []DotNetTLSResult, category string, timeout bool) []DotNetTLSResult {
	byMode := make(map[string]DotNetTLSResult, len(existing))
	for _, result := range existing {
		byMode[result.Mode] = result
	}
	out := make([]DotNetTLSResult, 0, len(dotNetTLSModes))
	for _, mode := range dotNetTLSModes {
		if result, ok := byMode[mode]; ok {
			out = append(out, result)
			continue
		}
		phase := "unknown"
		if timeout {
			phase = "handshake_timeout"
		}
		out = append(out, DotNetTLSResult{IP: ip, Mode: mode, Status: "fail", Phase: phase, Category: category, PolicyErrors: []string{}, ChainStatus: []string{}})
	}
	return out
}

func missingDotNetTLSResults(ip, category string, timeout bool) []DotNetTLSResult {
	return completeDotNetTLSModes(ip, nil, category, timeout)
}

func normalizeDotNetTCPError(value string) string {
	lower := strings.ToLower(value)
	switch {
	case strings.Contains(lower, "timedout") || strings.Contains(lower, "timeout"):
		return "dotnet_tcp_timeout"
	case strings.Contains(lower, "connectionrefused"):
		return "connection_refused"
	case strings.Contains(lower, "networkunreachable"):
		return "network_unreachable"
	case strings.Contains(lower, "hostunreachable"):
		return "host_unreachable"
	default:
		return "dotnet_tcp_other"
	}
}
