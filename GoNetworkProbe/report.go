package main

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"time"
)

func printDiscovery(d EsmDiscovery) {
	fmt.Printf("ESM detected: %s\nCDN cache state: %s\nCDN cache files: %d\nCache parse failures: %d\nCache read failures: %d\n", yesNo(d.Detected), d.CacheDirectoryStatus, d.CacheFiles, d.CacheParseFailed, d.CacheReadFailed)
	if d.SelectedCache != "" {
		fmt.Println("Selected cache:", d.SelectedCache)
	}
	if d.ESPLeaf != nil {
		fmt.Println("ESP gismt.crt: present")
	}
	if d.ESPRoot != nil {
		fmt.Println("ESP gismt_base.crt: present")
	}
	if d.IgnoredEndpoints > 0 {
		fmt.Printf("ignored non-CRPT endpoint: %d\n", d.IgnoredEndpoints)
	}
}
func printAdapters(a []AdapterInfo) {
	if len(a) == 0 {
		fmt.Println("No active adapters found.")
		return
	}
	for _, x := range a {
		fmt.Printf("%s (index %d, %s)\n  IPv4: %s\n  IPv6: %s\n  Gateway: %s\n  DNS: %s\n", x.Name, x.Index, x.Status, joinOrDash(x.IPv4), joinOrDash(x.IPv6), joinOrDash(x.Gateways), joinOrDash(x.DNSServers))
	}
}
func printProxyConfiguration(configuration ProxyConfiguration) {
	winHTTP, user, auto := proxySummary(configuration)
	fmt.Printf("WinHTTP: %s\nUser proxy: %s\nAutoConfig/WPAD: %s\n", valueOrDash(winHTTP), valueOrDash(user), valueOrDash(auto))
}
func printTLSRuntimeMetadata(metadata TLSRuntimeMetadata) {
	ps := metadata.PowerShell
	fmt.Printf("PowerShell: %s version=%s CLR=%s ServicePointManager.SecurityProtocol=%s\nWindows: version=%s build=%d Schannel TLS1.2=%s TLS1.3=%s TLS1.3-platform-supported=%s\n", valueOrDash(ps.Status), valueOrDash(ps.PowerShellVersion), valueOrDash(ps.CLRVersion), valueOrDash(ps.ServicePointSecurity), valueOrDash(metadata.Windows.OSVersion), metadata.Windows.OSBuild, valueOrDash(metadata.Windows.TLS12.State), valueOrDash(metadata.Windows.TLS13.State), yesNo(metadata.Windows.TLS13PlatformSupported))
}
func printResults(rs []CdnResult) {
	for _, r := range rs {
		fmt.Printf("\n==================================================\n%s:%d\n==================================================\n", r.Endpoint.Host, r.Endpoint.Port)
		if r.Cache != nil {
			fmt.Printf("ESM cache\nblocked: %s\nlatency: %s\n", displayBool(r.Cache.Blocked), displayFloat(r.Cache.LatencyMS))
		}
		printDNS("WINDOWS DNS", r.WindowsDNS)
		printDNS("GO DNS", r.GoDNS)
		fmt.Println("DNS comparison:", r.DNSComparison)
		for _, t := range r.TCP {
			fmt.Printf("GO DIRECT TCP %s:%d: %s (%d ms) local=%s remote=%s%s\n", t.IP, r.Endpoint.Port, strings.ToUpper(t.Status), t.DurationMS, valueOrDash(t.LocalAddress), valueOrDash(t.RemoteAddress), errorSuffix(t.Error))
		}
		for _, t := range r.DotNetTCP {
			fmt.Printf(".NET DIRECT TCP %s:%d: %s (%d ms) local=%s remote=%s%s\n", t.IP, r.Endpoint.Port, strings.ToUpper(t.Status), t.DurationMS, valueOrDash(t.LocalAddress), valueOrDash(t.RemoteAddress), errorSuffix(t.Error))
		}
		for _, t := range r.TLSSystem {
			fmt.Printf("GO TLS / SYSTEM %s: %s (%d ms)%s %s %s ALPN=%s\n", t.IP, strings.ToUpper(t.Status), t.DurationMS, errorSuffix(t.Error), t.Version, t.CipherSuite, valueOrDash(t.ALPN))
			printCert(t.Certificate)
		}
		for _, t := range r.TLSESP {
			fmt.Printf("GO TLS / SYSTEM + ESP ROOT %s: %s (%d ms)%s %s %s ALPN=%s\n", t.IP, strings.ToUpper(t.Status), t.DurationMS, errorSuffix(t.Error), t.Version, t.CipherSuite, valueOrDash(t.ALPN))
		}
		for _, t := range r.DotNetTLS {
			fmt.Printf(".NET TLS %s %s: %s (%d ms) phase=%s certReceived=%s category=%s protocol=%s\n", strings.ToUpper(t.Mode), t.IP, strings.ToUpper(t.Status), t.DurationMS, valueOrDash(t.Phase), yesNo(t.CertificateReceived), valueOrDash(t.Category), valueOrDash(t.Protocol))
			printCert(t.Certificate)
		}
		printPowerShellResult("POWERSHELL DEFAULT HTTPS", r.PowerShellDefault)
		printPowerShellResult("POWERSHELL NO-PROXY HTTPS", r.PowerShellNoProxy)
		if r.Correlation.Status != "" {
			fmt.Printf("ESM CORRELATION: %s freshness=%s\n%s\n", r.Correlation.Status, r.Correlation.Freshness, r.Correlation.Details)
		}
		if r.ESPLeafMatch != nil {
			fmt.Println("ESP gismt.crt match:", yesNo(*r.ESPLeafMatch))
		}
	}
}
func printPowerShellResult(label string, result PowerShellResult) {
	fmt.Printf("%s: %s (%d ms) category=%s status=%s%s\n", label, strings.ToUpper(transportStatus(result.TransportSuccess)), result.DurationMS, valueOrDash(result.Category), displayHTTPStatus(result.HTTPStatus), errorSuffix(result.ErrorExcerpt))
}
func printDNS(title string, d DnsResult) {
	fmt.Printf("%s\nbackend: %s\nservers: %s\ntransports: %s\n%s\n%s\n%d ms%s\n", title, valueOrDash(d.Backend), joinOrDash(d.ServersUsed), joinOrDash(d.TransportsUsed), strings.ToUpper(d.Status), joinOrDash(append(d.IPv4, d.IPv6...)), d.DurationMS, errorSuffix(d.Error))
}
func printCert(c *CertificateSummary) {
	if c != nil {
		fmt.Printf("Certificate: %s\nIssuer: %s\nValidity: %s .. %s\nThumbprint: %s\nSHA-256: %s\nSPKI SHA-256: %s\n", c.Subject, c.Issuer, c.NotBefore.Format(time.RFC3339), c.NotAfter.Format(time.RFC3339), valueOrDash(c.Thumbprint), valueOrDash(c.SHA256), valueOrDash(c.SPKISHA256))
	}
}
func printSummary(run ProbeRun) {
	s := run.Summary
	fmt.Printf("\n==================================================\nSUMMARY\n==================================================\n\nGIS transport: %s\n\nCDN endpoints: %d\nReachable: %d\nUnavailable: %d\n\nWindows DNS: %d/%d\nGo DNS: %d/%d\nGo direct TCP: %d/%d\n.NET direct TCP: %d/%d\nGo TLS / system: %d/%d\nGo TLS / system + ESP: %d/%d\n.NET TLS system default: %d/%d\n.NET TLS 1.2: %d/%d\n.NET TLS 1.3: %d/%d (unsupported=%d)\nPowerShell default HTTPS: %d/%d\nPowerShell no-proxy HTTPS: %d/%d\nESM cache blocked: %d\n\nProxy:\nWinHTTP = %s\nUser proxy = %s\nAutoConfig/WPAD = %s\n\nDirect-path divergence: %d endpoints\nDefault-vs-no-proxy divergence: %d endpoints\n", strings.ToUpper(s.GIStransport), s.CDNTested, s.Reachable, s.Unavailable, s.WindowsDNSSuccess, s.CDNTested, s.GoDNSSuccess, s.CDNTested, s.TCPSuccess, s.CDNTested, s.DotNetTCPSuccess, s.CDNTested, s.TLSSystemSuccess, s.CDNTested, s.TLSESPSuccess, s.CDNTested, s.DotNetTLSSuccess, s.CDNTested, s.DotNetTLS12Success, s.CDNTested, s.DotNetTLS13Success, s.CDNTested, s.DotNetTLS13Unsupported, s.PowerShellDefaultSuccess, s.CDNTested, s.PowerShellNoProxySuccess, s.CDNTested, s.ESMBlocked, valueOrDash(s.ProxyWinHTTP), valueOrDash(s.ProxyUser), valueOrDash(s.ProxyAutoConfig), s.DirectPathDivergence, s.DefaultNoProxyDivergence)
	if s.BestTCPHost != "" {
		fmt.Printf("Best TCP: %s (%d ms)\n", s.BestTCPHost, s.BestTCPMS)
	}
	if s.ESMActive != "" {
		fmt.Println("ESM active observed:", s.ESMActive)
	}
	if s.ESMBestObserved != "" {
		fmt.Println("ESM best observed:", s.ESMBestObserved)
	}
	if s.ProbeBestObserved != "" {
		fmt.Println("Probe best observed:", s.ProbeBestObserved)
	}
	if len(run.Findings) > 0 {
		fmt.Println("\nKey findings:")
		limit := 5
		if len(run.Findings) < limit {
			limit = len(run.Findings)
		}
		for _, finding := range run.Findings[:limit] {
			fmt.Printf("- [%s] %s: %s\n", strings.ToUpper(finding.Severity), finding.Title, strings.Join(finding.AffectedHosts, ", "))
		}
		if len(run.Findings) > limit {
			fmt.Printf("- ... and %d more; see report\n", len(run.Findings)-limit)
		}
	}
}
func summarize(rs []CdnResult) Summary {
	s := Summary{CDNTested: len(rs)}
	for _, r := range rs {
		if r.WindowsDNS.Status == "ok" {
			s.WindowsDNSSuccess++
		}
		if r.GoDNS.Status == "ok" {
			s.GoDNSSuccess++
		}
		if r.PowerShellDefault.TransportSuccess {
			s.PowerShellDefaultSuccess++
		}
		if r.PowerShellNoProxy.TransportSuccess {
			s.PowerShellNoProxySuccess++
		}
		if r.PowerShellDefault.TransportSuccess != r.PowerShellNoProxy.TransportSuccess {
			s.DefaultNoProxyDivergence++
		}
		if len(directSocketStackDivergences(r)) > 0 {
			s.DirectPathDivergence++
		}
		tcpOK := false
		for _, v := range r.TCP {
			if v.Status == "ok" {
				tcpOK = true
				if s.BestTCPHost == "" || v.DurationMS < s.BestTCPMS {
					s.BestTCPMS = v.DurationMS
					s.BestTCPHost = r.Endpoint.Host
				}
			}
		}
		if tcpOK {
			s.TCPSuccess++
		}
		if anyTCPOK(r.DotNetTCP) {
			s.DotNetTCPSuccess++
		}
		if anyTLSOK(r.TLSSystem) {
			s.TLSSystemSuccess++
		}
		if anyTLSOK(r.TLSESP) {
			s.TLSESPSuccess++
		}
		if dotNetTLSModeOK(r.DotNetTLS, "systemDefault") {
			s.DotNetTLSSuccess++
		}
		if dotNetTLSModeOK(r.DotNetTLS, "tls12") {
			s.DotNetTLS12Success++
		}
		if dotNetTLSModeOK(r.DotNetTLS, "tls13") {
			s.DotNetTLS13Success++
		}
		if dotNetTLSModeHasStatus(r.DotNetTLS, "tls13", "unsupported") {
			s.DotNetTLS13Unsupported++
		}
	}
	return s
}
func dotNetTLSModeOK(results []DotNetTLSResult, mode string) bool {
	return dotNetTLSModeHasStatus(results, mode, "ok")
}
func dotNetTLSModeHasStatus(results []DotNetTLSResult, mode, status string) bool {
	for _, result := range results {
		if result.Mode == mode && result.Status == status {
			return true
		}
	}
	return false
}
func writeReports(r ProbeRun) (string, string, error) {
	return writeReportsToDir(r, AppBaseDir())
}

func writeReportsToDir(r ProbeRun, dir string) (string, string, error) {
	r = sanitizeRunForReport(r)
	stem := "gonetworkprobe_" + r.StartedAt.Format("2006-01-02_15-04-05")
	txt, js, e := uniqueReportPaths(dir, stem)
	if e != nil {
		return "", "", e
	}
	b, e := json.MarshalIndent(r, "", "  ")
	if e != nil {
		return "", "", e
	}
	var textReport bytes.Buffer
	if e = renderTextReport(&textReport, r); e != nil {
		return "", "", e
	}
	jsonFile, e := os.OpenFile(js, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if e != nil {
		return "", "", e
	}
	if _, e = jsonFile.Write(b); e != nil {
		_ = jsonFile.Close()
		return "", "", e
	}
	if e = jsonFile.Close(); e != nil {
		return "", "", e
	}
	f, e := os.OpenFile(txt, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if e != nil {
		return "", "", e
	}
	if _, e = f.Write(textReport.Bytes()); e != nil {
		_ = f.Close()
		return "", "", e
	}
	if e = f.Close(); e != nil {
		return "", "", e
	}
	return txt, js, nil
}

func renderTextReport(w io.Writer, r ProbeRun) error {
	r = sanitizeRunForReport(r)
	var writeError error
	write := func(format string, args ...any) {
		if writeError == nil {
			_, writeError = fmt.Fprintf(w, format, args...)
		}
	}
	write("GoNetworkProbe %s\nESM / GIS MT transport diagnostics\nStarted: %s\nCompleted: %s\nCancelled: %s\n\n", r.ProbeVersion, r.StartedAt.Format(time.RFC3339), r.CompletedAt.Format(time.RFC3339), yesNo(r.Cancelled))
	write("SUMMARY\nGIS transport: %s\nCDN tested: %d\nReachable: %d\nUnavailable: %d\nWindows DNS: %d/%d\nGo DNS: %d/%d\nGo direct TCP: %d/%d\n.NET direct TCP: %d/%d\nGo TLS system: %d/%d\nGo TLS system + ESP: %d/%d\n.NET TLS system default: %d/%d\n.NET TLS 1.2: %d/%d\n.NET TLS 1.3: %d/%d unsupported=%d\nPowerShell default: %d/%d\nPowerShell no-proxy: %d/%d\nESM blocked: %d\nDirect-path divergence: %d\nDefault-vs-no-proxy divergence: %d\n", r.Summary.GIStransport, r.Summary.CDNTested, r.Summary.Reachable, r.Summary.Unavailable, r.Summary.WindowsDNSSuccess, r.Summary.CDNTested, r.Summary.GoDNSSuccess, r.Summary.CDNTested, r.Summary.TCPSuccess, r.Summary.CDNTested, r.Summary.DotNetTCPSuccess, r.Summary.CDNTested, r.Summary.TLSSystemSuccess, r.Summary.CDNTested, r.Summary.TLSESPSuccess, r.Summary.CDNTested, r.Summary.DotNetTLSSuccess, r.Summary.CDNTested, r.Summary.DotNetTLS12Success, r.Summary.CDNTested, r.Summary.DotNetTLS13Success, r.Summary.CDNTested, r.Summary.DotNetTLS13Unsupported, r.Summary.PowerShellDefaultSuccess, r.Summary.CDNTested, r.Summary.PowerShellNoProxySuccess, r.Summary.CDNTested, r.Summary.ESMBlocked, r.Summary.DirectPathDivergence, r.Summary.DefaultNoProxyDivergence)
	if r.Summary.BestTCPHost != "" {
		write("Best TCP: %s %d ms\n", r.Summary.BestTCPHost, r.Summary.BestTCPMS)
	} else {
		write("Best TCP: -\n")
	}
	write("\nPROXY CONFIGURATION (READ-ONLY)\nWinHTTP = %s\nUser proxy = %s\nAutoConfig/WPAD = %s\n\n", valueOrDash(r.Summary.ProxyWinHTTP), valueOrDash(r.Summary.ProxyUser), valueOrDash(r.Summary.ProxyAutoConfig))
	write("TLS RUNTIME METADATA (READ-ONLY)\nPowerShell=%s version=%s CLR=%s reportedOS=%s ServicePointManager.SecurityProtocol=%s\nWindows version=%s build=%d Schannel TLS1.2=%s TLS1.3=%s TLS1.3-platform-supported=%s\n\n", valueOrDash(r.TLSRuntime.PowerShell.Status), valueOrDash(r.TLSRuntime.PowerShell.PowerShellVersion), valueOrDash(r.TLSRuntime.PowerShell.CLRVersion), valueOrDash(r.TLSRuntime.PowerShell.ReportedOSVersion), valueOrDash(r.TLSRuntime.PowerShell.ServicePointSecurity), valueOrDash(r.TLSRuntime.Windows.OSVersion), r.TLSRuntime.Windows.OSBuild, valueOrDash(r.TLSRuntime.Windows.TLS12.State), valueOrDash(r.TLSRuntime.Windows.TLS13.State), yesNo(r.TLSRuntime.Windows.TLS13PlatformSupported))
	write("NETWORK ADAPTERS\n")
	for _, a := range r.Adapters {
		write("%s | %s | index=%d | %s\n  IPv4: %s\n  IPv6: %s\n  gateway: %s\n  DNS: %s\n", a.Name, a.Description, a.Index, a.Status, joinOrDash(a.IPv4), joinOrDash(a.IPv6), joinOrDash(a.Gateways), joinOrDash(a.DNSServers))
	}
	write("\nESM CACHE\ndetected=%s state=%s cacheFiles=%d parseFailed=%d readFailed=%d selected=%s ignoredEndpoints=%d blocked=%d\nactiveObserved=%s bestObserved=%s\ngismt.crt=%s gismt_base.crt=%s\n\nPER-CDN RESULTS\n", yesNo(r.ESM.Detected), r.ESM.CacheDirectoryStatus, r.ESM.CacheFiles, r.ESM.CacheParseFailed, r.ESM.CacheReadFailed, valueOrDash(r.ESM.SelectedCache), r.ESM.IgnoredEndpoints, r.Summary.ESMBlocked, valueOrDash(r.Summary.ESMActive), valueOrDash(r.Summary.ESMBestObserved), certState(r.ESM.ESPLeaf, r.ESM.ESPLeafError), certState(r.ESM.ESPRoot, r.ESM.ESPRootError))
	for _, c := range r.CDNResults {
		write("\n%s:%d\nWindows DNS: backend=%s servers=%s transports=%s status=%s IPs=%s duration=%dms%s\nGo DNS: backend=%s servers=%s transports=%s status=%s IPs=%s duration=%dms%s\nComparison: %s\n", c.Endpoint.Host, c.Endpoint.Port, valueOrDash(c.WindowsDNS.Backend), joinOrDash(c.WindowsDNS.ServersUsed), joinOrDash(c.WindowsDNS.TransportsUsed), c.WindowsDNS.Status, joinOrDash(append(append([]string{}, c.WindowsDNS.IPv4...), c.WindowsDNS.IPv6...)), c.WindowsDNS.DurationMS, errorSuffix(c.WindowsDNS.Error), valueOrDash(c.GoDNS.Backend), joinOrDash(c.GoDNS.ServersUsed), joinOrDash(c.GoDNS.TransportsUsed), c.GoDNS.Status, joinOrDash(append(append([]string{}, c.GoDNS.IPv4...), c.GoDNS.IPv6...)), c.GoDNS.DurationMS, errorSuffix(c.GoDNS.Error), c.DNSComparison)
		if c.Cache != nil {
			write("ESM cache: blocked=%s latency=%s lastChecked=%s blockUntil=%s\n", displayBool(c.Cache.Blocked), displayFloat(c.Cache.LatencyMS), displayTime(c.Cache.LastChecked), displayTime(c.Cache.BlockUntil))
		}
		for _, v := range c.TCP {
			write("Go direct TCP %s: %s %dms local=%s remote=%s%s\n", v.IP, v.Status, v.DurationMS, valueOrDash(v.LocalAddress), valueOrDash(v.RemoteAddress), errorSuffix(v.Error))
		}
		for _, v := range c.DotNetTCP {
			write(".NET direct TCP %s: %s %dms local=%s remote=%s%s\n", v.IP, v.Status, v.DurationMS, valueOrDash(v.LocalAddress), valueOrDash(v.RemoteAddress), errorSuffix(v.Error))
		}
		for _, v := range c.TLSSystem {
			if writeError == nil {
				writeError = writeTLSLine(w, "TLS system", v)
			}
		}
		for _, v := range c.TLSESP {
			if writeError == nil {
				writeError = writeTLSLine(w, "TLS system + ESP", v)
			}
		}
		for _, v := range c.DotNetTLS {
			if writeError == nil {
				writeError = writeDotNetTLSLine(w, v)
			}
		}
		writePowerShellLine := func(label string, result PowerShellResult) {
			write("%s: transport=%s status=%s category=%s exitCode=%d duration=%dms%s\n", label, yesNo(result.TransportSuccess), displayHTTPStatus(result.HTTPStatus), valueOrDash(result.Category), result.ExitCode, result.DurationMS, errorSuffix(result.ErrorExcerpt))
		}
		writePowerShellLine("PowerShell default HTTPS", c.PowerShellDefault)
		writePowerShellLine("PowerShell no-proxy HTTPS", c.PowerShellNoProxy)
		write("ESM correlation: %s freshness=%s ageSeconds=%s\n  %s\n", valueOrDash(c.Correlation.Status), valueOrDash(c.Correlation.Freshness), displayInt64(c.Correlation.CacheAgeSeconds), c.Correlation.Details)
		if c.ESPLeafMatch != nil {
			write("Local gismt.crt leaf match: %s\n", yesNo(*c.ESPLeafMatch))
		}
	}
	write("\nFINDINGS\n")
	if len(r.Findings) == 0 {
		write("- none\n")
	} else {
		for _, finding := range r.Findings {
			write("[%s] %s (%s)\n%s\nAffected: %s\n", strings.ToUpper(finding.Severity), finding.Title, finding.Code, finding.Details, joinOrDash(finding.AffectedHosts))
		}
	}
	write("\nLIMITATIONS\n")
	for _, limitation := range r.Limitations {
		write("- %s\n", limitation)
	}
	return writeError
}

func writeDotNetTLSLine(w io.Writer, result DotNetTLSResult) error {
	if _, err := fmt.Fprintf(w, ".NET TLS %s %s: %s phase=%s handshake=%dms protocol=%s certReceived=%s category=%s\n", result.Mode, result.IP, result.Status, valueOrDash(result.Phase), result.DurationMS, valueOrDash(result.Protocol), yesNo(result.CertificateReceived), valueOrDash(result.Category)); err != nil {
		return err
	}
	if result.Certificate != nil {
		if _, err := fmt.Fprintf(w, "  cert subject=%s issuer=%s thumbprint=%s sha256=%s notBefore=%s notAfter=%s\n", result.Certificate.Subject, result.Certificate.Issuer, valueOrDash(result.Certificate.Thumbprint), valueOrDash(result.Certificate.SHA256), result.Certificate.NotBefore.Format(time.RFC3339), result.Certificate.NotAfter.Format(time.RFC3339)); err != nil {
			return err
		}
	}
	if _, err := fmt.Fprintf(w, "  policyErrors=%s chainStatus=%s\n", joinOrDash(result.PolicyErrors), joinOrDash(result.ChainStatus)); err != nil {
		return err
	}
	if result.Exception != nil {
		_, err := fmt.Fprintf(w, "  exceptionType=%s inner=%s hresult=%s socketError=%s nativeError=%d exceptionCategory=%s safeMessage=%s\n", valueOrDash(result.Exception.ExceptionType), joinOrDash(result.Exception.InnerExceptionTypes), valueOrDash(result.Exception.HResult), valueOrDash(result.Exception.SocketErrorCode), result.Exception.NativeErrorCode, valueOrDash(result.Exception.Category), valueOrDash(result.Exception.SafeMessage))
		return err
	}
	return nil
}

func writeTLSLine(w io.Writer, label string, v TlsResult) error {
	if _, err := fmt.Fprintf(w, "%s %s: %s handshake=%dms version=%s cipher=%s ALPN=%s%s\n", label, v.IP, v.Status, v.DurationMS, valueOrDash(v.Version), valueOrDash(v.CipherSuite), valueOrDash(v.ALPN), errorSuffix(v.Error)); err != nil {
		return err
	}
	if v.Certificate != nil {
		if _, err := fmt.Fprintf(w, "  cert subject=%s issuer=%s notBefore=%s notAfter=%s sha256=%s spkiSha256=%s\n", v.Certificate.Subject, v.Certificate.Issuer, v.Certificate.NotBefore.Format(time.RFC3339), v.Certificate.NotAfter.Format(time.RFC3339), v.Certificate.SHA256, v.Certificate.SPKISHA256); err != nil {
			return err
		}
	}
	return nil
}
func anyTLSOK(results []TlsResult) bool {
	for _, result := range results {
		if result.Status == "ok" {
			return true
		}
	}
	return false
}
func certState(cert *CertificateSummary, err string) string {
	if cert != nil {
		return "present"
	}
	return valueOrDash(err)
}
func displayTime(value *time.Time) string {
	if value == nil {
		return "-"
	}
	return value.Format(time.RFC3339)
}
func valueOrDash(value string) string {
	if value == "" {
		return "-"
	}
	return value
}
func transportStatus(success bool) string {
	if success {
		return "ok"
	}
	return "fail"
}
func displayHTTPStatus(value *int) string {
	if value == nil {
		return "-"
	}
	return fmt.Sprint(*value)
}
func displayInt64(value *int64) string {
	if value == nil {
		return "-"
	}
	return fmt.Sprint(*value)
}
func AppBaseDir() string {
	e, err := os.Executable()
	if err != nil {
		return "."
	}
	return filepath.Dir(e)
}
func uniqueReportPaths(dir, stem string) (string, string, error) {
	for i := 0; ; i++ {
		s := stem
		if i > 0 {
			s += fmt.Sprintf("_%02d", i)
		}
		txt := filepath.Join(dir, s+".txt")
		js := filepath.Join(dir, s+".json")
		txtExists, err := pathExists(txt)
		if err != nil {
			return "", "", err
		}
		jsonExists, err := pathExists(js)
		if err != nil {
			return "", "", err
		}
		if !txtExists && !jsonExists {
			return txt, js, nil
		}
	}
}
func pathExists(path string) (bool, error) {
	_, err := os.Stat(path)
	if err == nil {
		return true, nil
	}
	if errors.Is(err, os.ErrNotExist) {
		return false, nil
	}
	return false, err
}
func joinOrDash(x []string) string {
	if len(x) == 0 {
		return "-"
	}
	return strings.Join(x, ", ")
}
func displayBool(v *bool) string {
	if v == nil {
		return "-"
	}
	return yesNo(*v)
}
func displayFloat(v *float64) string {
	if v == nil {
		return "-"
	}
	return fmt.Sprintf("%.0f ms", *v)
}
func errorSuffix(v string) string {
	if v == "" {
		return ""
	}
	return " [" + v + "]"
}
