package main

import (
	"fmt"
	"sort"
	"strings"
	"time"
)

const (
	cacheFreshMax  = 5 * time.Minute
	cacheRecentMax = 30 * time.Minute
	cacheClockSkew = 2 * time.Minute
)

func analyzeRun(run *ProbeRun, now time.Time) {
	if run.CDNResults == nil {
		run.CDNResults = make([]CdnResult, 0)
	}
	if run.ESM.Endpoints == nil {
		run.ESM.Endpoints = make([]CdnEndpoint, 0)
	}
	if run.ESM.CacheEntries == nil {
		run.ESM.CacheEntries = make([]EsmCacheEntry, 0)
	}
	run.Findings = make([]Finding, 0)
	run.PowerShellComparison = make([]PowerShellComparison, 0)
	run.ESMCorrelations = make([]EsmCorrelation, 0)
	run.Summary = summarize(run.CDNResults)
	run.Summary.ProxyWinHTTP, run.Summary.ProxyUser, run.Summary.ProxyAutoConfig = proxySummary(run.Proxy)
	run.Summary.GIStransport = determineTransportState(run.CDNResults)
	run.Summary.Reachable = countReachable(run.CDNResults)
	run.Summary.Unavailable = len(run.CDNResults) - run.Summary.Reachable
	run.Summary.ProbeBestObserved = probeBestObserved(run.CDNResults)
	run.Summary.ESMActive, run.Summary.ESMBestObserved = esmObservedHosts(run.CDNResults)
	for i := range run.CDNResults {
		result := &run.CDNResults[i]
		result.Correlation = correlateESM(*result, now)
		run.ESMCorrelations = append(run.ESMCorrelations, result.Correlation)
		run.PowerShellComparison = append(run.PowerShellComparison, PowerShellComparison{Host: result.Endpoint.Host, GoTLSSuccess: endpointGoTLSOK(*result), Default: result.PowerShellDefault, NoProxy: result.PowerShellNoProxy})
		if result.Cache != nil && result.Cache.Blocked != nil && *result.Cache.Blocked {
			run.Summary.ESMBlocked++
		}
	}
	run.Findings = buildFindings(run.CDNResults, run.Summary)
	run.Limitations = []string{
		"Transport diagnostics only; no GIS MT authentication, controlled channel, FN-SID, marks, or application requests are performed.",
		"PowerShell default and explicit no-proxy controls use an unauthenticated GET to /api/v4/cdn/health/check and treat any HTTP response as successful HTTP transport.",
		"Proxy configuration is collected read-only. A default/no-proxy difference suggests, but does not prove, proxy or automatic HTTP routing involvement.",
		"The .NET certificate callback is observational and accepts a certificate only when SslPolicyErrors is None; it never bypasses validation.",
		"Explicit TLS 1.2 and TLS 1.3 handshakes are independent diagnostics and do not change Windows, Schannel, or ServicePointManager settings.",
		"GetAddrInfoW has no synchronous cancellation API; the probe timeout returns while a timed-out Win32 lookup may finish in a background goroutine.",
		"ESM cache is observational data and is not treated as ground truth when stale or missing timestamps.",
	}
}

func cacheFreshness(entry *EsmCacheEntry, now time.Time) (string, *int64) {
	if entry == nil || entry.LastChecked == nil {
		return "Unknown", nil
	}
	age := now.Sub(*entry.LastChecked)
	if age < -cacheClockSkew {
		return "Unknown", nil
	}
	if age < 0 {
		age = 0
	}
	seconds := int64(age.Seconds())
	switch {
	case age <= cacheFreshMax:
		return "Fresh", &seconds
	case age <= cacheRecentMax:
		return "Recent", &seconds
	default:
		return "Stale", &seconds
	}
}

func correlateESM(result CdnResult, now time.Time) EsmCorrelation {
	freshness, age := cacheFreshness(result.Cache, now)
	correlation := EsmCorrelation{Host: result.Endpoint.Host, Freshness: freshness, CacheAgeSeconds: age}
	if result.Cache == nil {
		correlation.Status, correlation.Details = "unknown", "ESM cache entry is unavailable."
		return correlation
	}
	if freshness == "Stale" {
		correlation.Status, correlation.Details = "stale", "ESM cache is stale; no direct consistency conclusion is made."
		return correlation
	}
	if freshness == "Unknown" || result.Cache.Blocked == nil {
		correlation.Status, correlation.Details = "unknown", "ESM cache freshness or blocked state is unknown."
		return correlation
	}
	reachable := endpointReachable(result)
	if (*result.Cache.Blocked && !reachable) || (!*result.Cache.Blocked && reachable) {
		correlation.Status, correlation.Details = "consistent", "ESM availability and current transport observation broadly agree."
	} else {
		correlation.Status = "different"
		if *result.Cache.Blocked && reachable {
			correlation.Details = "Probe is currently reachable while ESM cache marks the endpoint blocked. The endpoint may have recovered or the observations may be from different times."
		} else {
			correlation.Details = "Probe is currently unavailable while ESM cache marks the endpoint available. Timing, routing, and CDN balancing may explain the different observation."
		}
	}
	return correlation
}

func determineTransportState(results []CdnResult) string {
	if len(results) == 0 {
		return "Unknown"
	}
	reachable := countReachable(results)
	if reachable == 0 {
		return "Unavailable"
	}
	if reachable < len(results) {
		return "Degraded"
	}
	return "Healthy"
}
func countReachable(results []CdnResult) int {
	count := 0
	for _, result := range results {
		if endpointReachable(result) {
			count++
		}
	}
	return count
}
func endpointReachable(result CdnResult) bool {
	return endpointGoTLSOK(result) || anyDotNetTLSOK(result.DotNetTLS) || result.PowerShellDefault.TransportSuccess || result.PowerShellNoProxy.TransportSuccess
}
func endpointGoTLSOK(result CdnResult) bool {
	return anyTLSOK(result.TLSSystem) || anyTLSOK(result.TLSESP)
}
func anyTCPOK(results []TcpResult) bool {
	for _, result := range results {
		if result.Status == "ok" {
			return true
		}
	}
	return false
}
func anyDNSOK(result CdnResult) bool {
	return result.WindowsDNS.Status == "ok" || result.GoDNS.Status == "ok"
}

func probeBestObserved(results []CdnResult) string {
	bestHost := ""
	bestScore := int64(0)
	for _, result := range results {
		for _, tcp := range result.TCP {
			if tcp.Status != "ok" {
				continue
			}
			tlsMS, ok := tlsLatencyForIP(result, tcp.IP)
			if !ok {
				continue
			}
			score := tcp.DurationMS + tlsMS
			if bestHost == "" || score < bestScore {
				bestHost, bestScore = result.Endpoint.Host, score
			}
		}
	}
	return bestHost
}
func tlsLatencyForIP(result CdnResult, ip string) (int64, bool) {
	for _, tlsResult := range result.TLSSystem {
		if tlsResult.IP == ip && tlsResult.Status == "ok" {
			return tlsResult.DurationMS, true
		}
	}
	for _, tlsResult := range result.TLSESP {
		if tlsResult.IP == ip && tlsResult.Status == "ok" {
			return tlsResult.DurationMS, true
		}
	}
	return 0, false
}
func esmObservedHosts(results []CdnResult) (string, string) {
	active, best := "", ""
	bestLatency := float64(0)
	for _, result := range results {
		entry := result.Cache
		if entry == nil {
			continue
		}
		if entry.Active != nil && *entry.Active {
			active = result.Endpoint.Host
		}
		if entry.LatencyMS == nil || (entry.Blocked != nil && *entry.Blocked) {
			continue
		}
		if best == "" || *entry.LatencyMS < bestLatency {
			best, bestLatency = result.Endpoint.Host, *entry.LatencyMS
		}
	}
	return active, best
}

func buildFindings(results []CdnResult, summary Summary) []Finding {
	var findings []Finding
	var unreachable []string
	allCoreOK := len(results) > 0
	for _, result := range results {
		host := result.Endpoint.Host
		if !endpointReachable(result) {
			unreachable = append(unreachable, host)
		}
		if result.WindowsDNS.Status != "ok" && result.GoDNS.Status == "ok" {
			findings = append(findings, finding("dns_resolver_difference", "warning", "Resolver behavior differs", "Pure Go resolved the host while the Windows resolver did not. This evidence does not identify a broken resolver.", []string{host}, dnsEvidence(result)))
		}
		if result.WindowsDNS.Status == "ok" && result.GoDNS.Status != "ok" {
			findings = append(findings, finding("dns_resolver_difference", "warning", "Resolver behavior differs", "The Windows resolver resolved the host while pure Go did not. This evidence does not identify a broken resolver.", []string{host}, dnsEvidence(result)))
		}
		if result.WindowsDNS.Status != "ok" && result.GoDNS.Status != "ok" {
			findings = append(findings, finding("dns_unavailable", "error", "DNS resolution unavailable", "Оба независимых DNS-теста не смогли разрешить CDN.", []string{host}, map[string]string{"windowsDns": result.WindowsDNS.Error, "goDns": result.GoDNS.Error}))
		}
		if anyDNSOK(result) && !anyTCPOK(result.TCP) {
			findings = append(findings, finding("tcp_19101_unavailable", "warning", "TCP :19101 не устанавливается", "Name resolution succeeds, but TCP :19101 cannot be established. The possible layer is route/firewall/provider/CDN; no specific cause is asserted.", []string{host}, map[string]string{"layer": "tcp", "port": "19101"}))
		}
		if proxyPathSuspected(result) {
			findings = append(findings, finding("proxy_path_suspected", "warning", "Proxy or automatic HTTP path may be involved", "PowerShell default HTTP path reaches the CDN while direct socket/no-proxy paths do not. A proxy or Windows automatic HTTP routing path may be involved.", []string{host}, map[string]string{"goDirectTcp": tcpFailureSummary(result.TCP), "dotNetDirectTcp": tcpFailureSummary(result.DotNetTCP), "powershellDefault": powerShellEvidence(result.PowerShellDefault), "powershellNoProxy": powerShellEvidence(result.PowerShellNoProxy)}))
		}
		for _, evidence := range directSocketStackDivergences(result) {
			findings = append(findings, finding("direct_socket_stack_divergence", "warning", "Go and .NET direct socket results differ", "Go direct TCP fails while .NET direct TCP succeeds to the same literal IP and port. The cause is not inferred.", []string{host}, evidence))
		}
		if anyTCPOK(result.TCP) && !endpointGoTLSOK(result) {
			findings = append(findings, finding("go_tls_failed_after_tcp", "warning", "TCP доступен, Go TLS не прошёл", "TCP is reachable, but Go TLS verification or handshake failed. The recorded TLS categories are evidence; no certificate-store or server fault is inferred.", []string{host}, map[string]string{"layer": "tls", "system": tlsFailureSummary(result.TLSSystem), "systemPlusEsp": tlsFailureSummary(result.TLSESP)}))
		}
		systemTLS, hasSystemTLS := dotNetTLSModeResult(result.DotNetTLS, "systemDefault")
		tls12, hasTLS12 := dotNetTLSModeResult(result.DotNetTLS, "tls12")
		goTLSOK := endpointGoTLSOK(result)
		if goTLSOK && hasSystemTLS && systemTLS.Status != "ok" && hasTLS12 && tls12.Status == "ok" {
			findings = append(findings, finding("dotnet_system_default_vs_tls12", "warning", "Windows/.NET system-default TLS differs from explicit TLS 1.2", "Windows/.NET system-default TLS negotiation differs from explicit TLS 1.2.", []string{host}, dotNetTLSFindingEvidence(systemTLS, tls12)))
		}
		if goTLSOK && anyTCPOK(result.DotNetTCP) && hasSystemTLS && systemTLS.Status == "fail" && hasTLS12 && tls12.Status == "fail" && !systemTLS.CertificateReceived && !tls12.CertificateReceived {
			findings = append(findings, finding("dotnet_tls_before_certificate", "warning", "Windows/.NET TLS fails before a validated certificate is observed", "TCP succeeds, but Windows/.NET TLS handshake fails before a validated server certificate is observed.", []string{host}, map[string]string{"systemPhase": systemTLS.Phase, "systemCategory": systemTLS.Category, "tls12Phase": tls12.Phase, "tls12Category": tls12.Category}))
		}
		if goTLSOK && hasSystemTLS && systemTLS.CertificateReceived && len(systemTLS.PolicyErrors) > 0 {
			findings = append(findings, finding("dotnet_certificate_validation_failed", "warning", "Windows/.NET rejects the observed certificate", "Windows/.NET TLS reaches certificate validation but rejects the certificate/chain.", []string{host}, map[string]string{"policyErrors": strings.Join(systemTLS.PolicyErrors, ","), "chainStatus": strings.Join(systemTLS.ChainStatus, ",")}))
		}
		if goTLSOK && hasSystemTLS && systemTLS.Phase == "remote_closed" {
			findings = append(findings, finding("dotnet_tls_remote_closed", "warning", "Windows/.NET TLS connection closes during negotiation", "Server connection is closed during Windows/.NET TLS negotiation while Go TLS succeeds.", []string{host}, map[string]string{"phase": systemTLS.Phase, "category": systemTLS.Category, "nativeErrorCode": dotNetNativeError(systemTLS)}))
		}
		if hasTLS12 && tls12.Status == "ok" && goTLSUsesVersion(result, "TLS 1.3") {
			findings = append(findings, finding("tls_protocols_differ", "info", "TLS stacks negotiated different protocol versions", "Both TLS stacks are functional; they negotiated different protocol versions.", []string{host}, map[string]string{"go": "TLS 1.3", "dotNet": tls12.Protocol}))
		}
		if hasSystemTLS && systemTLS.Status == "ok" && result.PowerShellDefault.Category == "powershell_receive_failure" {
			findings = append(findings, finding("powershell_failure_after_independent_tls", "warning", "PowerShell fails beyond an independently successful TLS handshake", "Windows/.NET TLS handshake succeeds independently, while PowerShell HTTP fails after/beyond the TLS layer.", []string{host}, map[string]string{"dotNetSystemTls": systemTLS.Protocol, "powershellDefault": result.PowerShellDefault.Category}))
		}
		if goTLSOK && hasSystemTLS && systemTLS.Status == "fail" && result.PowerShellDefault.Category == "powershell_receive_failure" {
			findings = append(findings, finding("windows_dotnet_tls_https_failure", "warning", "Windows/.NET TLS and HTTPS paths fail while Go TLS succeeds", "Both independent Windows/.NET TLS/HTTPS paths fail while Go TLS succeeds.", []string{host}, map[string]string{"dotNetPhase": systemTLS.Phase, "dotNetCategory": systemTLS.Category, "powershellDefault": result.PowerShellDefault.Category}))
		}
		if anyTCPOK(result.TCP) && !anyTLSOK(result.TLSSystem) && anyTLSOK(result.TLSESP) {
			findings = append(findings, finding("esp_trust_anchor_required", "warning", "TLS trust отличается", "Go TLS проходит только после добавления ESP trust anchor в приватный CertPool.", []string{host}, map[string]string{"tlsSystem": "failed", "tlsSystemPlusEsp": "ok"}))
		}
		if endpointGoTLSOK(result) && result.PowerShellDefault.Category != "" && !result.PowerShellDefault.TransportSuccess {
			details := "Go TLS transport работает, а Windows PowerShell HTTPS transport не работает. Возможен Windows/.NET/Schannel/client-specific слой; это не доказывает конкретную причину."
			if result.PowerShellDefault.Category == "powershell_receive_failure" {
				details = "Go TLS succeeds, Windows PowerShell HTTPS request failed while receiving the response. TLS failure or Schannel failure is not inferred without additional evidence."
			}
			findings = append(findings, finding("powershell_transport_difference", "warning", "Go TLS и PowerShell HTTPS расходятся", details, []string{host}, map[string]string{"goTls": "ok", "powershellDefault": result.PowerShellDefault.Category}))
		}
		if result.WindowsDNS.Status != "ok" || result.GoDNS.Status != "ok" || !anyTCPOK(result.TCP) || !anyTCPOK(result.DotNetTCP) || !endpointGoTLSOK(result) || !anyDotNetTLSOK(result.DotNetTLS) || !result.PowerShellDefault.TransportSuccess || !result.PowerShellNoProxy.TransportSuccess {
			allCoreOK = false
		}
	}
	sort.Strings(unreachable)
	if allCoreOK {
		findings = append(findings, finding("transport_layers_healthy", "info", "Транспортные слои доступны", "DNS/TCP/TLS transport исправен. Если ТС ПИоТ всё равно не работает, искать проблему выше транспортного уровня: controlled channel/auth/FN-SID/application protocol.", hostsOf(results), map[string]string{"gisTransport": summary.GIStransport}))
	}
	if len(unreachable) == 1 && summary.Reachable > 0 {
		findings = append(findings, finding("isolated_cdn_failure", "warning", "Недоступна отдельная CDN площадка", "Локальная проблема отдельной CDN площадки не означает недоступность GIS MT transport в целом.", unreachable, nil))
	}
	if len(unreachable) > 1 && summary.Reachable > 0 {
		findings = append(findings, finding("partial_cdn_degradation", "warning", "Частичная деградация CDN pool", "Несколько CDN недоступны, но существуют рабочие transport paths.", unreachable, map[string]string{"reachable": fmt.Sprint(summary.Reachable), "unavailable": fmt.Sprint(summary.Unavailable)}))
	}
	if len(results) > 0 && summary.Reachable == 0 {
		layer := commonFailureLayer(results)
		findings = append(findings, finding("systemic_transport_failure", "error", "Системная проблема transport layer", "Ни один CDN не достиг приемлемого transport layer. Общий наблюдаемый уровень: "+layer+"; конкретная причина не утверждается.", hostsOf(results), map[string]string{"layer": layer}))
	}
	return mergeFindings(findings)
}

func anyDotNetTLSOK(results []DotNetTLSResult) bool {
	for _, result := range results {
		if result.Status == "ok" {
			return true
		}
	}
	return false
}

func dotNetTLSModeResult(results []DotNetTLSResult, mode string) (DotNetTLSResult, bool) {
	for _, result := range results {
		if result.Mode == mode {
			return result, true
		}
	}
	return DotNetTLSResult{}, false
}

func dotNetTLSFindingEvidence(system, tls12 DotNetTLSResult) map[string]string {
	return map[string]string{"systemStatus": system.Status, "systemPhase": system.Phase, "systemCategory": system.Category, "tls12Status": tls12.Status, "tls12Phase": tls12.Phase, "tls12Protocol": tls12.Protocol}
}

func dotNetNativeError(result DotNetTLSResult) string {
	if result.Exception == nil || result.Exception.NativeErrorCode == 0 {
		return "not_available"
	}
	return fmt.Sprint(result.Exception.NativeErrorCode)
}

func goTLSUsesVersion(result CdnResult, version string) bool {
	for _, tlsResult := range append(append([]TlsResult{}, result.TLSSystem...), result.TLSESP...) {
		if tlsResult.Status == "ok" && tlsResult.Version == version {
			return true
		}
	}
	return false
}

func commonFailureLayer(results []CdnResult) string {
	allDNS, allTCP := true, true
	for _, result := range results {
		if anyDNSOK(result) {
			allDNS = false
		}
		if anyTCPOK(result.TCP) {
			allTCP = false
		}
	}
	if allDNS {
		return "dns"
	}
	if allTCP {
		return "tcp"
	}
	return "tls_or_client"
}

func proxyPathSuspected(result CdnResult) bool {
	return directTCPFailed(result.TCP) && directTCPFailed(result.DotNetTCP) && !result.PowerShellNoProxy.TransportSuccess && result.PowerShellDefault.TransportSuccess
}

func directTCPFailed(results []TcpResult) bool {
	return len(results) > 0 && !anyTCPOK(results)
}

func directSocketStackDivergences(result CdnResult) []map[string]string {
	var evidence []map[string]string
	for _, goResult := range result.TCP {
		if goResult.Status == "ok" {
			continue
		}
		for _, dotNetResult := range result.DotNetTCP {
			if dotNetResult.IP != goResult.IP || dotNetResult.Status != "ok" {
				continue
			}
			evidence = append(evidence, map[string]string{"ip": goResult.IP, "port": fmt.Sprint(result.Endpoint.Port), "goResult": goResult.Status + ":" + goResult.Error, "dotNetResult": dotNetResult.Status, "dotNetLocalEndpoint": dotNetResult.LocalAddress, "dotNetRemoteEndpoint": dotNetResult.RemoteAddress})
		}
	}
	return evidence
}

func powerShellEvidence(result PowerShellResult) string {
	if result.HTTPStatus != nil {
		return fmt.Sprintf("%s:http_%d", result.Category, *result.HTTPStatus)
	}
	return result.Category
}

func tcpFailureSummary(results []TcpResult) string {
	if len(results) == 0 {
		return "not_run"
	}
	values := make([]string, 0, len(results))
	for _, result := range results {
		values = append(values, result.IP+":"+result.Status+":"+result.Error)
	}
	return strings.Join(values, ",")
}
func finding(code, severity, title, details string, hosts []string, evidence map[string]string) Finding {
	return Finding{Code: code, Severity: severity, Title: title, Details: details, AffectedHosts: sortedUnique(hosts), Evidence: evidence}
}
func dnsEvidence(result CdnResult) map[string]string {
	return map[string]string{"windowsBackend": result.WindowsDNS.Backend, "windowsStatus": result.WindowsDNS.Status, "windowsDurationMs": fmt.Sprint(result.WindowsDNS.DurationMS), "windowsIPs": strings.Join(append(append([]string{}, result.WindowsDNS.IPv4...), result.WindowsDNS.IPv6...), ","), "goBackend": result.GoDNS.Backend, "goStatus": result.GoDNS.Status, "goDurationMs": fmt.Sprint(result.GoDNS.DurationMS), "goIPs": strings.Join(append(append([]string{}, result.GoDNS.IPv4...), result.GoDNS.IPv6...), ","), "goDnsServers": strings.Join(result.GoDNS.ServersUsed, ","), "goDnsTransports": strings.Join(result.GoDNS.TransportsUsed, ",")}
}
func tlsFailureSummary(results []TlsResult) string {
	if len(results) == 0 {
		return "not_reached"
	}
	values := make([]string, 0, len(results))
	for _, result := range results {
		if result.Status != "ok" {
			values = append(values, result.Error)
		}
	}
	if len(values) == 0 {
		return "ok"
	}
	return strings.Join(sortedUnique(values), ",")
}
func hostsOf(results []CdnResult) []string {
	hosts := make([]string, 0, len(results))
	for _, result := range results {
		hosts = append(hosts, result.Endpoint.Host)
	}
	return sortedUnique(hosts)
}
func mergeFindings(input []Finding) []Finding {
	type key struct{ code, severity, title, details, evidence string }
	order := make([]key, 0, len(input))
	merged := make(map[key]Finding)
	for _, item := range input {
		evidenceParts := make([]string, 0, len(item.Evidence))
		for k, v := range item.Evidence {
			evidenceParts = append(evidenceParts, k+"="+v)
		}
		sort.Strings(evidenceParts)
		k := key{item.Code, item.Severity, item.Title, item.Details, strings.Join(evidenceParts, ";")}
		if existing, ok := merged[k]; ok {
			existing.AffectedHosts = sortedUnique(append(existing.AffectedHosts, item.AffectedHosts...))
			merged[k] = existing
		} else {
			merged[k] = item
			order = append(order, k)
		}
	}
	out := make([]Finding, 0, len(order))
	for _, k := range order {
		out = append(out, merged[k])
	}
	return out
}
