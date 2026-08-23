package main

import (
	"testing"
	"time"
)

func TestCacheFreshness(t *testing.T) {
	now := time.Date(2026, 8, 20, 12, 0, 0, 0, time.UTC)
	tests := []struct {
		name    string
		checked *time.Time
		want    string
	}{
		{"unknown", nil, "Unknown"}, {"fresh", timePointer(now.Add(-2 * time.Minute)), "Fresh"}, {"recent", timePointer(now.Add(-10 * time.Minute)), "Recent"}, {"stale", timePointer(now.Add(-time.Hour)), "Stale"},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			got, _ := cacheFreshness(&EsmCacheEntry{LastChecked: test.checked}, now)
			if got != test.want {
				t.Fatalf("got=%s want=%s", got, test.want)
			}
		})
	}
}

func TestESMCorrelation(t *testing.T) {
	now := time.Now()
	blocked, available := true, false
	consistent := correlateESM(CdnResult{Endpoint: CdnEndpoint{Host: "cdn01.crpt.ru"}, Cache: &EsmCacheEntry{Blocked: &blocked, LastChecked: timePointer(now)}, WindowsDNS: DnsResult{Status: "ok"}, TCP: []TcpResult{{Status: "fail"}}}, now)
	if consistent.Status != "consistent" {
		t.Fatalf("consistent=%+v", consistent)
	}
	stale := correlateESM(CdnResult{Endpoint: CdnEndpoint{Host: "cdn01.crpt.ru"}, Cache: &EsmCacheEntry{Blocked: &available, LastChecked: timePointer(now.Add(-time.Hour))}, TLSSystem: []TlsResult{{Status: "ok"}}}, now)
	if stale.Status != "stale" {
		t.Fatalf("stale=%+v", stale)
	}
}

func TestCacheFreshnessTimezoneOffset(t *testing.T) {
	checked := time.Date(2026, 8, 20, 15, 55, 0, 0, time.FixedZone("UTC+3", 3*60*60))
	now := time.Date(2026, 8, 20, 13, 0, 0, 0, time.UTC)
	freshness, age := cacheFreshness(&EsmCacheEntry{LastChecked: &checked}, now)
	if freshness != "Fresh" || age == nil || *age != 300 {
		t.Fatalf("freshness=%s age=%v", freshness, age)
	}
}

func TestOverallTransportState(t *testing.T) {
	reachable := healthyResult("cdn01.crpt.ru")
	failed := failedResult("cdn02.crpt.ru")
	if got := determineTransportState(nil); got != "Unknown" {
		t.Fatal(got)
	}
	if got := determineTransportState([]CdnResult{reachable}); got != "Healthy" {
		t.Fatal(got)
	}
	if got := determineTransportState([]CdnResult{reachable, failed}); got != "Degraded" {
		t.Fatal(got)
	}
	if got := determineTransportState([]CdnResult{failed}); got != "Unavailable" {
		t.Fatal(got)
	}
}

func TestExpertRules(t *testing.T) {
	result := healthyResult("cdn01.crpt.ru")
	result.WindowsDNS = DnsResult{Status: "fail", Error: "dns_not_found"}
	result.GoDNS = DnsResult{Status: "ok", IPv4: []string{"192.0.2.1"}}
	result.TLSSystem = []TlsResult{{Status: "fail", Error: "unknown_authority"}}
	result.TLSESP = []TlsResult{{Status: "ok"}}
	result.PowerShellDefault = PowerShellResult{Category: "powershell_tls_failure"}
	summary := summarize([]CdnResult{result})
	summary.GIStransport = determineTransportState([]CdnResult{result})
	summary.Reachable = 1
	findings := buildFindings([]CdnResult{result}, summary)
	for _, code := range []string{"dns_resolver_difference", "esp_trust_anchor_required", "powershell_transport_difference"} {
		if !hasFinding(findings, code) {
			t.Fatalf("missing finding %s: %+v", code, findings)
		}
	}
}

func TestReverseDNSDivergenceFinding(t *testing.T) {
	result := healthyResult("cdn01.crpt.ru")
	result.WindowsDNS = DnsResult{Backend: "GetAddrInfoW", Status: "ok", IPv4: []string{"192.0.2.1"}}
	result.GoDNS = DnsResult{Backend: "net.Resolver PreferGo", ServersUsed: []string{"10.0.0.53:53"}, Status: "fail", Error: "dns_timeout"}
	summary := summarize([]CdnResult{result})
	summary.GIStransport = "Healthy"
	summary.Reachable = 1
	findings := buildFindings([]CdnResult{result}, summary)
	if !hasFinding(findings, "dns_resolver_difference") {
		t.Fatalf("findings=%+v", findings)
	}
}

func TestPartialFailureFindingAndUnavailableState(t *testing.T) {
	partial := []CdnResult{healthyResult("cdn01.crpt.ru"), failedResult("cdn02.crpt.ru")}
	summary := summarize(partial)
	summary.GIStransport = determineTransportState(partial)
	summary.Reachable, summary.Unavailable = 1, 1
	if summary.GIStransport != "Degraded" || !hasFinding(buildFindings(partial, summary), "isolated_cdn_failure") {
		t.Fatalf("summary=%+v", summary)
	}
	failed := []CdnResult{failedResult("cdn01.crpt.ru"), failedResult("cdn02.crpt.ru")}
	failedSummary := summarize(failed)
	failedSummary.GIStransport = determineTransportState(failed)
	failedSummary.Unavailable = 2
	if failedSummary.GIStransport != "Unavailable" || !hasFinding(buildFindings(failed, failedSummary), "systemic_transport_failure") {
		t.Fatalf("summary=%+v", failedSummary)
	}
}

func TestProxyPathSuspectedFinding(t *testing.T) {
	status := 401
	result := failedResult("cdn04.crpt.ru")
	result.WindowsDNS, result.GoDNS = DnsResult{Status: "ok", IPv4: []string{"192.0.2.4"}}, DnsResult{Status: "ok", IPv4: []string{"192.0.2.4"}}
	result.TCP = []TcpResult{{IP: "192.0.2.4", Status: "fail", Error: "tcp_timeout"}}
	result.DotNetTCP = []TcpResult{{IP: "192.0.2.4", Status: "fail", Error: "dotnet_tcp_timeout"}}
	result.PowerShellDefault = PowerShellResult{TransportSuccess: true, HTTPStatus: &status, Category: "powershell_http_response"}
	result.PowerShellNoProxy = PowerShellResult{Category: "powershell_tcp_timeout"}
	summary := summarize([]CdnResult{result})
	if summary.DefaultNoProxyDivergence != 1 || !hasFinding(buildFindings([]CdnResult{result}, summary), "proxy_path_suspected") {
		t.Fatal("missing proxy_path_suspected")
	}
}

func TestBothPowerShellModesSuccessHasNoProxyFinding(t *testing.T) {
	result := failedResult("cdn04.crpt.ru")
	status := 401
	result.TCP = []TcpResult{{IP: "192.0.2.4", Status: "fail", Error: "tcp_timeout"}}
	result.DotNetTCP = []TcpResult{{IP: "192.0.2.4", Status: "fail", Error: "dotnet_tcp_timeout"}}
	result.PowerShellDefault = PowerShellResult{TransportSuccess: true, HTTPStatus: &status, Category: "powershell_http_response"}
	result.PowerShellNoProxy = result.PowerShellDefault
	if hasFinding(buildFindings([]CdnResult{result}, summarize([]CdnResult{result})), "proxy_path_suspected") {
		t.Fatal("unexpected proxy_path_suspected")
	}
}

func TestDirectSocketStackDivergenceFinding(t *testing.T) {
	result := failedResult("cdn05.crpt.ru")
	result.TCP = []TcpResult{{IP: "192.0.2.5", Status: "fail", Error: "tcp_timeout"}}
	result.DotNetTCP = []TcpResult{{IP: "192.0.2.5", Status: "ok", LocalAddress: "192.0.2.10:50000", RemoteAddress: "192.0.2.5:19101"}}
	findings := buildFindings([]CdnResult{result}, summarize([]CdnResult{result}))
	if summarize([]CdnResult{result}).DirectPathDivergence != 1 || !hasFinding(findings, "direct_socket_stack_divergence") {
		t.Fatalf("findings=%+v", findings)
	}
}

func TestGoSuccessAndDotNetSystemFailureFinding(t *testing.T) {
	result := healthyResult("cdn04.crpt.ru")
	result.DotNetTLS = []DotNetTLSResult{
		{IP: "192.0.2.4", Mode: "systemDefault", Status: "fail", Phase: "remote_closed", Category: "dotnet_tls_remote_closed", PolicyErrors: []string{}, ChainStatus: []string{}},
		{IP: "192.0.2.4", Mode: "tls12", Status: "fail", Phase: "remote_closed", Category: "dotnet_tls_remote_closed", PolicyErrors: []string{}, ChainStatus: []string{}},
		{IP: "192.0.2.4", Mode: "tls13", Status: "unsupported", Phase: "unknown", Category: "unsupported_by_runtime", PolicyErrors: []string{}, ChainStatus: []string{}},
	}
	findings := buildFindings([]CdnResult{result}, summarize([]CdnResult{result}))
	for _, code := range []string{"dotnet_tls_before_certificate", "dotnet_tls_remote_closed"} {
		if !hasFinding(findings, code) {
			t.Fatalf("missing %s: %+v", code, findings)
		}
	}
}

func TestDotNetSystemSuccessAndPowerShellReceiveFailureFinding(t *testing.T) {
	result := healthyResult("cdn05.crpt.ru")
	result.PowerShellDefault = PowerShellResult{Category: "powershell_receive_failure"}
	findings := buildFindings([]CdnResult{result}, summarize([]CdnResult{result}))
	if !hasFinding(findings, "powershell_failure_after_independent_tls") {
		t.Fatalf("findings=%+v", findings)
	}
}

func healthyResult(host string) CdnResult {
	status := 403
	ps := PowerShellResult{TransportSuccess: true, HTTPStatus: &status, Category: "powershell_http_response"}
	return CdnResult{Endpoint: CdnEndpoint{Host: host, Port: 19101}, WindowsDNS: DnsResult{Status: "ok"}, GoDNS: DnsResult{Status: "ok"}, TCP: []TcpResult{{IP: "192.0.2.1", Status: "ok", DurationMS: 10}}, DotNetTCP: []TcpResult{{IP: "192.0.2.1", Status: "ok", DurationMS: 10}}, TLSSystem: []TlsResult{{IP: "192.0.2.1", Status: "ok", Version: "TLS 1.3", DurationMS: 20}}, DotNetTLS: []DotNetTLSResult{{IP: "192.0.2.1", Mode: "systemDefault", Status: "ok", Phase: "success", Protocol: "Tls12", DurationMS: 20}, {IP: "192.0.2.1", Mode: "tls12", Status: "ok", Phase: "success", Protocol: "Tls12", DurationMS: 20}, {IP: "192.0.2.1", Mode: "tls13", Status: "unsupported", Phase: "unknown", Category: "unsupported_by_runtime"}}, PowerShellDefault: ps, PowerShellNoProxy: ps}
}
func failedResult(host string) CdnResult {
	ps := PowerShellResult{Category: "powershell_dns_failure"}
	return CdnResult{Endpoint: CdnEndpoint{Host: host, Port: 19101}, WindowsDNS: DnsResult{Status: "fail", Error: "dns_not_found"}, GoDNS: DnsResult{Status: "fail", Error: "dns_not_found"}, PowerShellDefault: ps, PowerShellNoProxy: ps}
}
func hasFinding(findings []Finding, code string) bool {
	for _, finding := range findings {
		if finding.Code == code {
			return true
		}
	}
	return false
}
func timePointer(value time.Time) *time.Time { return &value }
