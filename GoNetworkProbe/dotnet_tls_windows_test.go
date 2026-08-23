//go:build windows

package main

import (
	"strings"
	"testing"
)

func TestCertificateCallbackNeverAcceptsPolicyErrors(t *testing.T) {
	if !certificatePolicyAccepted(nil) || certificatePolicyAccepted([]string{"RemoteCertificateChainErrors"}) {
		t.Fatal("certificate policy acceptance is unsafe")
	}
	if !strings.Contains(dotNetDirectProbeScript, "return ($policyErrors -eq [System.Net.Security.SslPolicyErrors]::None)") || strings.Contains(dotNetDirectProbeScript, "return $true") {
		t.Fatal("PowerShell certificate callback can bypass validation")
	}
}

func TestDotNetTLSPhaseClassification(t *testing.T) {
	tests := []struct {
		name   string
		input  DotNetTLSResult
		phase  string
		status string
	}{
		{"certificate validation", DotNetTLSResult{Status: "fail", CertificateReceived: true, PolicyErrors: []string{"RemoteCertificateChainErrors"}}, "certificate_validation_failed", "fail"},
		{"protocol negotiation", DotNetTLSResult{Status: "fail", Exception: &DotNetExceptionDiagnostic{SafeMessage: "TLS protocol version is not supported"}}, "protocol_negotiation_failed", "fail"},
		{"remote close", DotNetTLSResult{Status: "fail", Exception: &DotNetExceptionDiagnostic{NativeErrorCode: 10054}}, "remote_closed", "fail"},
		{"timeout", DotNetTLSResult{Status: "fail", Exception: &DotNetExceptionDiagnostic{SocketErrorCode: "TimedOut"}}, "handshake_timeout", "fail"},
		{"credentials unavailable", DotNetTLSResult{Status: "fail", Exception: &DotNetExceptionDiagnostic{NativeErrorCode: -2146893042}}, "handshake_started", "fail"},
		{"certificate received", DotNetTLSResult{Status: "fail", CertificateReceived: true}, "certificate_received", "fail"},
		{"certificate not received", DotNetTLSResult{Status: "fail", Phase: "handshake_started"}, "handshake_started", "fail"},
		{"tls13 unsupported", DotNetTLSResult{Mode: "tls13", Status: "unsupported"}, "unknown", "unsupported"},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			phase, _, status := classifyDotNetTLSResult(test.input)
			if phase != test.phase || status != test.status {
				t.Fatalf("phase=%s status=%s", phase, status)
			}
			if test.name == "credentials unavailable" {
				_, category, _ := classifyDotNetTLSResult(test.input)
				if category != "dotnet_tls_credentials_unavailable" {
					t.Fatalf("category=%s", category)
				}
			}
		})
	}
}

func TestDotNetExceptionChainSanitizer(t *testing.T) {
	result := sanitizeAndClassifyDotNetTLS(DotNetTLSResult{Status: "fail", Phase: "handshake_started", Exception: &DotNetExceptionDiagnostic{ExceptionType: "System.IO.IOException", InnerExceptionTypes: []string{"System.ComponentModel.Win32Exception"}, Category: "io_exception", SafeMessage: `failed C:\Users\Alice\secret.txt Authorization: Bearer aaa token=abc`}})
	if result.Exception == nil {
		t.Fatal("exception missing")
	}
	for _, forbidden := range []string{"Alice", "Bearer aaa", "abc", `C:\Users`} {
		if strings.Contains(result.Exception.SafeMessage, forbidden) {
			t.Fatalf("leaked %q: %s", forbidden, result.Exception.SafeMessage)
		}
	}
}

func TestTLSRuntimeMetadataCollectionIsReadOnly(t *testing.T) {
	for _, forbidden := range []string{"ServicePointManager]::SecurityProtocol =", "Set-ItemProperty", "New-ItemProperty", "RegSetValue", "SchUseStrongCrypto", "DefaultSecureProtocols"} {
		if strings.Contains(powerShellRuntimeMetadataScript, forbidden) || strings.Contains(dotNetDirectProbeScript, forbidden) {
			t.Fatalf("TLS diagnostic script contains mutation %q", forbidden)
		}
	}
	if !strings.Contains(powerShellRuntimeMetadataScript, "[System.Net.ServicePointManager]::SecurityProtocol.ToString()") {
		t.Fatal("current ServicePointManager.SecurityProtocol is not observed")
	}
}

func TestTLS13PlatformSupportBaseline(t *testing.T) {
	if tls13SupportedOnWindowsBuild(19045) {
		t.Fatal("Windows 10 22H2 must not be treated as Schannel TLS 1.3 capable")
	}
	for _, build := range []uint32{20348, 22000, 26100} {
		if !tls13SupportedOnWindowsBuild(build) {
			t.Fatalf("build %d should meet the documented TLS 1.3 platform baseline", build)
		}
	}
}
