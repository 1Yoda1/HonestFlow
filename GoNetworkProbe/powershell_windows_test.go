//go:build windows

package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"os"
	"strings"
	"testing"
	"time"
)

func TestPowerShellHTTPSLive(t *testing.T) {
	if os.Getenv("GONETWORKPROBE_LIVE") != "1" {
		t.Skip("set GONETWORKPROBE_LIVE=1 for the optional network test")
	}
	defaultResult := runPowerShellHTTPSDefault(context.Background(), "cdn01.crpt.ru")
	noProxyResult := runPowerShellHTTPSNoProxy(context.Background(), "cdn01.crpt.ru")
	t.Logf("default=%+v noProxy=%+v", defaultResult, noProxyResult)
}

func TestFullTransportLive(t *testing.T) {
	if os.Getenv("GONETWORKPROBE_LIVE") != "1" {
		t.Skip("set GONETWORKPROBE_LIVE=1 for the optional network test")
	}
	endpoint := CdnEndpoint{Host: "cdn01.crpt.ru", Port: 19101}
	discovery := EsmDiscovery{Endpoints: []CdnEndpoint{endpoint}}
	result := probeOne(context.Background(), discovery, endpoint, loadTrustPools(`C:\ProgramData\ESP\ESM\um\gismt_base.crt`))
	t.Logf("windowsDNS=%+v goDNS=%+v goTCP=%+v dotNetTCP=%+v tlsSystem=%+v tlsESP=%+v dotNetTLS=%+v powershellDefault=%+v powershellNoProxy=%+v", result.WindowsDNS, result.GoDNS, result.TCP, result.DotNetTCP, result.TLSSystem, result.TLSESP, result.DotNetTLS, result.PowerShellDefault, result.PowerShellNoProxy)
}

func TestProxyPathTargetsLive(t *testing.T) {
	if os.Getenv("GONETWORKPROBE_LIVE") != "1" {
		t.Skip("set GONETWORKPROBE_LIVE=1 for the optional network test")
	}
	for _, host := range []string{"cdn04.crpt.ru", "cdn05.crpt.ru"} {
		t.Run(host, func(t *testing.T) {
			endpoint := CdnEndpoint{Host: host, Port: 19101}
			result := probeOne(context.Background(), EsmDiscovery{Endpoints: []CdnEndpoint{endpoint}}, endpoint, loadTrustPools(`C:\ProgramData\ESP\ESM\um\gismt_base.crt`))
			raw, err := json.Marshal(result)
			if err != nil {
				t.Fatal(err)
			}
			t.Logf("result=%s", raw)
		})
	}
}

func TestPowerShellHTTP403IsTransportSuccess(t *testing.T) {
	result := normalizePowerShellOutput("GNP|HTTP|403", 0)
	if !result.TransportSuccess || result.HTTPStatus == nil || *result.HTTPStatus != 403 || result.Category != "powershell_http_response" {
		t.Fatalf("result=%+v", result)
	}
}

func TestPowerShellHTTP401IsTransportSuccess(t *testing.T) {
	result := normalizePowerShellOutput("GNP|HTTP|401", 0)
	if !result.TransportSuccess || result.HTTPStatus == nil || *result.HTTPStatus != 401 {
		t.Fatalf("result=%+v", result)
	}
}

func TestPowerShellOutputNormalization(t *testing.T) {
	tests := map[string]string{
		"GNP|ERROR|NameResolutionFailure|System.Net.Sockets.SocketException": "powershell_dns_failure",
		"GNP|ERROR|Timeout|": "powershell_tcp_timeout",
		"GNP|ERROR|TrustFailure|System.Security.Authentication.AuthenticationException":         "powershell_certificate_failure",
		"GNP|ERROR|SecureChannelFailure|System.Security.Authentication.AuthenticationException": "powershell_tls_failure",
		"GNP|ERROR|ReceiveFailure|System.ComponentModel.Win32Exception":                         "powershell_receive_failure",
	}
	for output, want := range tests {
		if got := normalizePowerShellOutput(output, 2).Category; got != want {
			t.Fatalf("output=%q got=%s want=%s", output, got, want)
		}
	}
}

func TestPowerShellInvocationCannotInjectHostname(t *testing.T) {
	if _, err := buildPowerShellInvocation("cdn01.crpt.ru;Write-Output PWNED"); err == nil {
		t.Fatal("injection hostname accepted")
	}
	invocation, err := buildPowerShellInvocation("cdn01.crpt.ru")
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(strings.Join(invocation.args, " "), "cdn01.crpt.ru") {
		t.Fatal("hostname embedded into PowerShell code/arguments")
	}
	if len(invocation.env) != 1 || invocation.env[0] != "GONETWORKPROBE_HOST=cdn01.crpt.ru" {
		t.Fatalf("env=%v", invocation.env)
	}
	noProxy, err := buildPowerShellHTTPSInvocation("cdn01.crpt.ru", true)
	if err != nil || strings.Contains(strings.Join(noProxy.args, " "), "cdn01.crpt.ru") || len(noProxy.env) != 1 {
		t.Fatalf("unsafe no-proxy invocation: %+v err=%v", noProxy, err)
	}
	merged := mergeEnvironment([]string{"Path=x", "gonetworkprobe_host=evil.example"}, invocation.env[0])
	if len(merged) != 2 || merged[1] != invocation.env[0] {
		t.Fatalf("environment override was ambiguous: %v", merged)
	}
	minimal := minimalPowerShellEnvironment([]string{"SystemRoot=C:\\Windows", "TEMP=C:\\Temp", "API_TOKEN=secret"})
	if len(minimal) != 2 || strings.Contains(strings.Join(minimal, ";"), "secret") {
		t.Fatalf("sensitive environment inherited: %v", minimal)
	}
}

func TestPowerShellScriptIsReadOnlyAndUnauthenticated(t *testing.T) {
	if !strings.Contains(powerShellHTTPSProbeScript, "-Method Get") || !strings.Contains(powerShellHTTPSProbeScript, "/api/v4/cdn/health/check") {
		t.Fatal("safe GET health request missing")
	}
	if !strings.Contains(powerShellHTTPSNoProxyScript, "$request.Proxy=$null") || !strings.Contains(powerShellHTTPSNoProxyScript, "$request.Method='GET'") {
		t.Fatal("explicit no-proxy GET is missing")
	}
	for _, script := range []string{powerShellHTTPSProbeScript, powerShellHTTPSNoProxyScript, dotNetDirectProbeScript} {
		for _, forbidden := range []string{"Authorization", "X-FN-SID", "codes/check", "req_get_chlg", "req_chk_chlg", "-Method Post", "Bearer", "FN-SID"} {
			if strings.Contains(script, forbidden) {
				t.Fatalf("forbidden PowerShell behavior %q", forbidden)
			}
		}
	}
}

func TestDotNetDirectUsesLiteralIPAndOriginalTLSHostname(t *testing.T) {
	invocation, err := buildDotNetDirectInvocation("192.0.2.44", "cdn04.crpt.ru", 19101)
	if err != nil {
		t.Fatal(err)
	}
	arguments := strings.Join(invocation.args, " ")
	if strings.Contains(arguments, "192.0.2.44") || strings.Contains(arguments, "cdn04.crpt.ru") {
		t.Fatal("dynamic values embedded in executable script")
	}
	joinedEnv := strings.Join(invocation.env, ";")
	if !strings.Contains(joinedEnv, "GONETWORKPROBE_IP=192.0.2.44") || !strings.Contains(joinedEnv, "GONETWORKPROBE_HOST=cdn04.crpt.ru") || !strings.Contains(joinedEnv, "GONETWORKPROBE_PORT=19101") || !strings.Contains(joinedEnv, "GONETWORKPROBE_TLS13_SUPPORTED=") {
		t.Fatalf("env=%v", invocation.env)
	}
	if !strings.Contains(dotNetDirectProbeScript, "[System.Net.IPAddress]::Parse($ipText)") || !strings.Contains(dotNetDirectProbeScript, "BeginConnect($address,$port") || strings.Contains(dotNetDirectProbeScript, "GetHost") || strings.Contains(dotNetDirectProbeScript, "Dns") {
		t.Fatal(".NET direct TCP can resolve a hostname")
	}
	if !strings.Contains(dotNetDirectProbeScript, "$ssl.AuthenticateAsClient($h)") ||
		!strings.Contains(dotNetDirectProbeScript, "$ssl.AuthenticateAsClient($h,$null,$protocol,$false)") ||
		!strings.Contains(dotNetDirectProbeScript, "SslStream]::new($stream,$false,$callback)") {
		t.Fatal("SslStream does not use the original hostname with normal verification")
	}
}

func TestDotNetDirectOutputNormalization(t *testing.T) {
	input := DotNetTLSResult{IP: "192.0.2.44", Mode: "tls12", Status: "ok", Phase: "success", Protocol: "Tls12", DurationMS: 34, CertificateReceived: true, Certificate: &CertificateSummary{Subject: "CN=cdn04.crpt.ru"}, PolicyErrors: []string{}, ChainStatus: []string{}}
	raw, err := json.Marshal(input)
	if err != nil {
		t.Fatal(err)
	}
	output := "GNP|DNTCP|OK|12|192.0.2.10:50000|192.0.2.44:19101\nGNP|DNTLSJSON|" + base64.StdEncoding.EncodeToString(raw)
	tcp, tlsResults := normalizeDotNetDirectOutput(output, "192.0.2.44")
	tls, ok := dotNetTLSModeResult(tlsResults, "tls12")
	if !ok || tcp.Status != "ok" || tcp.RemoteAddress != "192.0.2.44:19101" || tls.Status != "ok" || tls.Protocol != "Tls12" || tls.Certificate == nil || tls.Certificate.Subject != "CN=cdn04.crpt.ru" {
		t.Fatalf("tcp=%+v tls=%+v", tcp, tlsResults)
	}
}

func TestPowerShellExcerptSanitization(t *testing.T) {
	got := sanitizePowerShellExcerpt(`failure at C:\Users\Alice\secret.txt Authorization: Bearer abc token=abc123 JWT=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature FN=123 INN=770123 CIS=mark`)
	for _, forbidden := range []string{`C:\Users`, "Alice", "Bearer abc", "abc123", "eyJhbGci", "123", "770123", "mark"} {
		if strings.Contains(got, forbidden) {
			t.Fatalf("excerpt leaks %q: %q", forbidden, got)
		}
	}
	if len(got) > 243 {
		t.Fatalf("excerpt=%q", got)
	}
}

func TestPowerShellCancelledContextDoesNotHang(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	started := time.Now()
	result := runPowerShellHTTPS(ctx, "cdn01.crpt.ru")
	if result.Category != "powershell_tcp_timeout" || time.Since(started) > time.Second {
		t.Fatalf("result=%+v elapsed=%s", result, time.Since(started))
	}
}
