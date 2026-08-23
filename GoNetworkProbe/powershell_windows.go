//go:build windows

package main

import (
	"bytes"
	"context"
	"encoding/base64"
	"errors"
	"os"
	"os/exec"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
	"unicode/utf16"
)

const (
	windowsPowerShellPath = `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`
	powerShellTimeout     = 12 * time.Second
	powerShellOutputLimit = 4096
)

const powerShellHTTPSProbeScript = `$ErrorActionPreference='Stop'
$h=[Environment]::GetEnvironmentVariable('GONETWORKPROBE_HOST','Process')
try {
  $u=[Uri]::new(('https://{0}:19101/api/v4/cdn/health/check' -f $h))
  try {
    $r=Invoke-WebRequest -Uri $u -Method Get -UseBasicParsing -TimeoutSec 8 -MaximumRedirection 0 -ErrorAction Stop
    [Console]::Out.WriteLine(('GNP|HTTP|{0}' -f [int]$r.StatusCode))
    exit 0
  } catch [System.Net.WebException] {
    $e=$_.Exception
    if ($null -ne $e.Response) {
      [Console]::Out.WriteLine(('GNP|HTTP|{0}' -f [int]$e.Response.StatusCode))
      exit 0
    }
    $inner=''
    if ($null -ne $e.InnerException) { $inner=$e.InnerException.GetType().FullName }
    [Console]::Out.WriteLine(('GNP|ERROR|{0}|{1}' -f $e.Status,$inner))
    exit 2
  }
} catch {
  [Console]::Out.WriteLine(('GNP|ERROR|Other|{0}' -f $_.Exception.GetType().FullName))
  exit 3
}`

const powerShellHTTPSNoProxyScript = `$ErrorActionPreference='Stop'
$h=[Environment]::GetEnvironmentVariable('GONETWORKPROBE_HOST','Process')
try {
  $u=[Uri]::new(('https://{0}:19101/api/v4/cdn/health/check' -f $h))
  $request=[System.Net.HttpWebRequest]::Create($u)
  $request.Method='GET'
  $request.Proxy=$null
  $request.AllowAutoRedirect=$false
  $request.Timeout=8000
  $request.ReadWriteTimeout=8000
  try {
    $response=$request.GetResponse()
    try { [Console]::Out.WriteLine(('GNP|HTTP|{0}' -f [int]$response.StatusCode)) } finally { $response.Close() }
    exit 0
  } catch [System.Net.WebException] {
    $e=$_.Exception
    if ($null -ne $e.Response) {
      try { [Console]::Out.WriteLine(('GNP|HTTP|{0}' -f [int]$e.Response.StatusCode)) } finally { $e.Response.Close() }
      exit 0
    }
    $inner=''
    if ($null -ne $e.InnerException) { $inner=$e.InnerException.GetType().FullName }
    [Console]::Out.WriteLine(('GNP|ERROR|{0}|{1}' -f $e.Status,$inner))
    exit 2
  }
} catch {
  [Console]::Out.WriteLine(('GNP|ERROR|Other|{0}' -f $_.Exception.GetType().FullName))
  exit 3
}`

type powerShellInvocation struct {
	path string
	args []string
	env  []string
}

type powerShellExecution struct {
	output      string
	exitCode    int
	durationMS  int64
	timedOut    bool
	unavailable bool
}

func buildPowerShellInvocation(host string) (powerShellInvocation, error) {
	return buildPowerShellHTTPSInvocation(host, false)
}

func buildPowerShellHTTPSInvocation(host string, noProxy bool) (powerShellInvocation, error) {
	if !allowedCRPT(host, 19101) {
		return powerShellInvocation{}, errors.New("host rejected by CRPT allowlist")
	}
	script := powerShellHTTPSProbeScript
	if noProxy {
		script = powerShellHTTPSNoProxyScript
	}
	encoded := encodePowerShellCommand(script)
	return powerShellInvocation{path: windowsPowerShellPath, args: []string{"-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded}, env: []string{"GONETWORKPROBE_HOST=" + host}}, nil
}

func runPowerShellHTTPS(ctx context.Context, host string) PowerShellResult {
	return runPowerShellHTTPSDefault(ctx, host)
}

func runPowerShellHTTPSDefault(ctx context.Context, host string) PowerShellResult {
	return runPowerShellHTTPSMode(ctx, host, false)
}

func runPowerShellHTTPSNoProxy(ctx context.Context, host string) PowerShellResult {
	return runPowerShellHTTPSMode(ctx, host, true)
}

func runPowerShellHTTPSMode(ctx context.Context, host string, noProxy bool) PowerShellResult {
	started := time.Now()
	invocation, err := buildPowerShellHTTPSInvocation(host, noProxy)
	if err != nil {
		return PowerShellResult{ExitCode: -1, DurationMS: time.Since(started).Milliseconds(), Category: "powershell_other", ErrorExcerpt: "host rejected by allowlist"}
	}
	execution := executePowerShell(ctx, invocation, powerShellTimeout)
	if execution.timedOut {
		return PowerShellResult{ExitCode: execution.exitCode, DurationMS: execution.durationMS, Category: "powershell_tcp_timeout", ErrorExcerpt: "PowerShell HTTPS probe timed out"}
	}
	if execution.unavailable {
		return PowerShellResult{ExitCode: -1, DurationMS: execution.durationMS, Category: "powershell_unavailable", ErrorExcerpt: "Windows PowerShell unavailable"}
	}
	result := normalizePowerShellOutput(execution.output, execution.exitCode)
	result.DurationMS = execution.durationMS
	return result
}

func executePowerShell(ctx context.Context, invocation powerShellInvocation, timeout time.Duration) powerShellExecution {
	started := time.Now()
	runCtx, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()
	cmd := exec.CommandContext(runCtx, invocation.path, invocation.args...)
	cmd.Env = mergeEnvironmentAssignments(minimalPowerShellEnvironment(os.Environ()), invocation.env...)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	var stdout, stderr limitedBuffer
	stdout.limit, stderr.limit = powerShellOutputLimit, powerShellOutputLimit
	cmd.Stdout, cmd.Stderr = &stdout, &stderr
	err := cmd.Run()
	result := powerShellExecution{durationMS: time.Since(started).Milliseconds()}
	exitCode := 0
	if err != nil {
		exitCode = -1
		var exitErr *exec.ExitError
		if errors.As(err, &exitErr) {
			exitCode = exitErr.ExitCode()
		}
	}
	result.exitCode = exitCode
	result.output = strings.TrimSpace(stdout.String() + "\n" + stderr.String())
	if runCtx.Err() != nil {
		result.timedOut = true
		return result
	}
	if exitCode == -1 && err != nil {
		var pathError *os.PathError
		if errors.As(err, &pathError) {
			result.unavailable = true
			return result
		}
	}
	return result
}

func normalizePowerShellOutput(output string, exitCode int) PowerShellResult {
	result := PowerShellResult{ExitCode: exitCode, Category: "powershell_other"}
	for _, line := range strings.Split(strings.ReplaceAll(output, "\r", ""), "\n") {
		line = strings.TrimSpace(line)
		if strings.HasPrefix(line, "GNP|HTTP|") {
			status, err := strconv.Atoi(strings.TrimPrefix(line, "GNP|HTTP|"))
			if err == nil && status >= 100 && status <= 599 {
				result.TransportSuccess = true
				result.HTTPStatus = &status
				result.Category = "powershell_http_response"
				return result
			}
		}
		if strings.HasPrefix(line, "GNP|ERROR|") {
			parts := strings.SplitN(line, "|", 5)
			marker := strings.ToLower(strings.Join(parts[2:], " "))
			switch {
			case strings.Contains(marker, "nameresolutionfailure"):
				result.Category = "powershell_dns_failure"
			case strings.Contains(marker, "timeout"):
				result.Category = "powershell_tcp_timeout"
			case strings.Contains(marker, "trustfailure") || strings.Contains(marker, "certificate"):
				result.Category = "powershell_certificate_failure"
			case strings.Contains(marker, "securechannelfailure") || strings.Contains(marker, "authenticationexception"):
				result.Category = "powershell_tls_failure"
			case strings.Contains(marker, "receivefailure"):
				result.Category = "powershell_receive_failure"
			default:
				result.Category = "powershell_other"
			}
			result.ErrorExcerpt = sanitizePowerShellExcerpt(line)
			return result
		}
	}
	lower := strings.ToLower(output)
	switch {
	case strings.Contains(lower, "name resolution") || strings.Contains(lower, "не удалось разрешить"):
		result.Category = "powershell_dns_failure"
	case strings.Contains(lower, "timed out") || strings.Contains(lower, "timeout") || strings.Contains(lower, "превышен интервал"):
		result.Category = "powershell_tcp_timeout"
	case strings.Contains(lower, "certificate") || strings.Contains(lower, "сертификат"):
		result.Category = "powershell_certificate_failure"
	case strings.Contains(lower, "ssl") || strings.Contains(lower, "tls") || strings.Contains(lower, "secure channel"):
		result.Category = "powershell_tls_failure"
	}
	result.ErrorExcerpt = sanitizePowerShellExcerpt(output)
	return result
}

func encodePowerShellCommand(script string) string {
	units := utf16.Encode([]rune(script))
	raw := make([]byte, len(units)*2)
	for i, unit := range units {
		raw[i*2], raw[i*2+1] = byte(unit), byte(unit>>8)
	}
	return base64.StdEncoding.EncodeToString(raw)
}

func mergeEnvironment(base []string, assignment string) []string {
	return mergeEnvironmentAssignments(base, assignment)
}

func mergeEnvironmentAssignments(base []string, assignments ...string) []string {
	names := make(map[string]bool, len(assignments))
	for _, assignment := range assignments {
		names[strings.ToUpper(strings.SplitN(assignment, "=", 2)[0])] = true
	}
	result := make([]string, 0, len(base)+len(assignments))
	for _, item := range base {
		if names[strings.ToUpper(strings.SplitN(item, "=", 2)[0])] {
			continue
		}
		result = append(result, item)
	}
	return append(result, assignments...)
}

func minimalPowerShellEnvironment(base []string) []string {
	allowed := map[string]bool{"SYSTEMROOT": true, "WINDIR": true, "TEMP": true, "TMP": true, "COMSPEC": true}
	result := make([]string, 0, len(allowed))
	for _, item := range base {
		name := strings.ToUpper(strings.SplitN(item, "=", 2)[0])
		if allowed[name] {
			result = append(result, item)
		}
	}
	return result
}

type limitedBuffer struct {
	mu    sync.Mutex
	data  bytes.Buffer
	limit int
}

func (b *limitedBuffer) Write(p []byte) (int, error) {
	b.mu.Lock()
	defer b.mu.Unlock()
	original := len(p)
	remaining := b.limit - b.data.Len()
	if remaining > 0 {
		if len(p) > remaining {
			p = p[:remaining]
		}
		_, _ = b.data.Write(p)
	}
	return original, nil
}
func (b *limitedBuffer) String() string { b.mu.Lock(); defer b.mu.Unlock(); return b.data.String() }
