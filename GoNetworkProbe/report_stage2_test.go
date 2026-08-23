package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

type failingWriter struct{}

func (failingWriter) Write([]byte) (int, error) { return 0, errors.New("write failed") }

func TestStage2ReportContainsRequiredSections(t *testing.T) {
	now := time.Now()
	blocked := false
	healthy := healthyResult("cdn01.crpt.ru")
	healthy.Cache = &EsmCacheEntry{Blocked: &blocked, LastChecked: &now, LatencyMS: floatPointer(18)}
	run := ProbeRun{SchemaVersion: "1", ProbeVersion: "test", StartedAt: now, CompletedAt: now, CDNResults: []CdnResult{healthy}}
	analyzeRun(&run, now)
	var text bytes.Buffer
	if err := renderTextReport(&text, run); err != nil {
		t.Fatal(err)
	}
	for _, section := range []string{"SUMMARY", "TLS RUNTIME METADATA", "PROXY CONFIGURATION", "NETWORK ADAPTERS", "ESM CACHE", "PER-CDN RESULTS", "FINDINGS", "LIMITATIONS", "PowerShell default HTTPS", "PowerShell no-proxy HTTPS", ".NET direct TCP", ".NET TLS", "ESM correlation"} {
		if !strings.Contains(text.String(), section) {
			t.Fatalf("missing %q in report", section)
		}
	}
	raw, err := json.Marshal(run)
	if err != nil {
		t.Fatal(err)
	}
	serialized := string(raw)
	for _, field := range []string{`"tlsRuntime"`, `"perCdn"`, `"findings"`, `"powershellComparison"`, `"esmCorrelation"`} {
		if !strings.Contains(serialized, field) {
			t.Fatalf("missing JSON field %s", field)
		}
	}
}

func TestReportDirectoryFailureReturnsError(t *testing.T) {
	run := ProbeRun{StartedAt: time.Now()}
	_, _, err := writeReportsToDir(run, filepath.Join(t.TempDir(), "missing"))
	if err == nil {
		t.Fatal("expected report directory error")
	}
}

func TestTextReportPropagatesWriterFailure(t *testing.T) {
	if err := renderTextReport(failingWriter{}, ProbeRun{}); err == nil {
		t.Fatal("expected writer failure")
	}
	if err := writeTLSLine(failingWriter{}, "TLS system", TlsResult{Status: "ok"}); err == nil {
		t.Fatal("expected TLS line writer failure")
	}
}

func TestTXTAndJSONReportsRedactAdversarialErrors(t *testing.T) {
	now := time.Now()
	secret := `Authorization: Bearer aaa eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0.signature C:\Users\Alice\file FN=123 INN=7701 CIS=mark -----BEGIN PRIVATE KEY----- abc -----END PRIVATE KEY-----`
	run := ProbeRun{StartedAt: now, CompletedAt: now, Proxy: ProxyConfiguration{WinHTTP: WinHTTPProxyConfiguration{Proxy: "http://user:pass@proxy.local:8080"}, User: UserProxyConfiguration{ProxyServer: "user:pass@proxy.local:8080"}}, CDNResults: []CdnResult{{Endpoint: CdnEndpoint{Host: "cdn01.crpt.ru", Port: 19101}, DotNetTLS: []DotNetTLSResult{{Mode: "systemDefault", Status: "fail", Exception: &DotNetExceptionDiagnostic{ExceptionType: "System.IO.IOException", InnerExceptionTypes: []string{"System.ComponentModel.Win32Exception"}, SafeMessage: secret}}}, PowerShellDefault: PowerShellResult{ErrorExcerpt: secret}, PowerShellNoProxy: PowerShellResult{ErrorExcerpt: secret}}}, PowerShellComparison: []PowerShellComparison{{Host: "cdn01.crpt.ru", Default: PowerShellResult{ErrorExcerpt: secret}, NoProxy: PowerShellResult{ErrorExcerpt: secret}}}, Findings: []Finding{{Code: "fake", Evidence: map[string]string{"error": secret}}}}
	dir := t.TempDir()
	txt, jsonPath, err := writeReportsToDir(run, dir)
	if err != nil {
		t.Fatal(err)
	}
	txtBytes, err := os.ReadFile(txt)
	if err != nil {
		t.Fatal(err)
	}
	jsonBytes, err := os.ReadFile(jsonPath)
	if err != nil {
		t.Fatal(err)
	}
	combined := string(txtBytes) + string(jsonBytes)
	for _, forbidden := range []string{"Bearer aaa", "eyJhbGci", `C:\Users\Alice`, "FN=123", "INN=7701", "CIS=mark", "BEGIN PRIVATE KEY", "user:pass"} {
		if strings.Contains(combined, forbidden) {
			t.Fatalf("report leaked %q", forbidden)
		}
	}
}

func floatPointer(value float64) *float64 { return &value }
