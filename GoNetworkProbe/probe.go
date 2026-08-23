package main

import (
	"context"
	"crypto/tls"
	"crypto/x509"
	"encoding/pem"
	"errors"
	"net"
	"net/netip"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
)

const (
	dnsTimeout    = 3 * time.Second
	tcpTimeout    = 3 * time.Second
	tlsTimeout    = 5 * time.Second
	perCDNTimeout = 75 * time.Second
	workers       = 4
)

type dialContextFunc func(context.Context, string, string) (net.Conn, error)
type GoDNSResolver struct{ dial dialContextFunc }
type trustPools struct {
	system      *x509.CertPool
	systemESP   *x509.CertPool
	systemError string
	espError    string
}

func (resolver GoDNSResolver) Lookup(ctx context.Context, host string) DnsResult {
	dial := resolver.dial
	if dial == nil {
		dial = (&net.Dialer{}).DialContext
	}
	var mu sync.Mutex
	var servers []string
	var transports []string
	observedDial := func(ctx context.Context, network, address string) (net.Conn, error) {
		mu.Lock()
		servers = append(servers, address)
		transports = append(transports, network)
		mu.Unlock()
		return dial(ctx, network, address)
	}
	result := lookupGoDNS(ctx, newPureGoResolver(observedDial), host)
	result.Backend = "net.Resolver PreferGo"
	mu.Lock()
	result.ServersUsed = sortedUnique(append([]string(nil), servers...))
	result.TransportsUsed = sortedUnique(append([]string(nil), transports...))
	mu.Unlock()
	if result.ServersUsed == nil {
		result.ServersUsed = []string{}
	}
	if result.TransportsUsed == nil {
		result.TransportsUsed = []string{}
	}
	return result
}

func newPureGoResolver(dial dialContextFunc) *net.Resolver {
	return &net.Resolver{PreferGo: true, Dial: func(ctx context.Context, network, address string) (net.Conn, error) {
		if !isLiteralSocketAddress(address) {
			return nil, errors.New("Go DNS server address is not a literal IP")
		}
		return dial(ctx, network, address)
	}}
}

func lookupGoDNS(ctx context.Context, resolver *net.Resolver, host string) DnsResult {
	started := time.Now()
	lookupCtx, cancel := context.WithTimeout(ctx, dnsTimeout)
	defer cancel()
	ips, err := resolver.LookupIPAddr(lookupCtx, host)
	result := DnsResult{DurationMS: time.Since(started).Milliseconds()}
	if err != nil {
		result.Status, result.Error = "fail", classifyDNS(err)
		return result
	}
	result.Status = "ok"
	for _, value := range ips {
		if value.IP.To4() != nil {
			result.IPv4 = append(result.IPv4, value.IP.String())
		} else if value.IP.To16() != nil {
			result.IPv6 = append(result.IPv6, value.IP.String())
		}
	}
	result.IPv4 = sortedUnique(result.IPv4)
	result.IPv6 = sortedUnique(result.IPv6)
	return result
}

func loadTrustPools(baseCertPath string) trustPools {
	var pools trustPools
	pools.system, pools.systemError = loadSystemPool()
	pools.systemESP, pools.espError = loadSystemPool()
	if pools.systemESP == nil {
		return pools
	}
	b, err := readLimitedFile(baseCertPath, maxCertFileSize)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			pools.espError = "esp_root_not_found"
		} else {
			pools.espError = "esp_root_read_failed"
		}
		pools.systemESP = nil
		return pools
	}
	cert, err := parseFirstCertificate(b)
	if err != nil {
		pools.espError = "esp_root_parse_failed"
		pools.systemESP = nil
		return pools
	}
	if !cert.IsCA || !cert.BasicConstraintsValid || cert.KeyUsage&x509.KeyUsageCertSign == 0 {
		pools.espError = "esp_root_not_ca"
		pools.systemESP = nil
		return pools
	}
	block, rest := pem.Decode(b)
	if block == nil || block.Type != "CERTIFICATE" || len(strings.TrimSpace(string(rest))) != 0 || !pools.systemESP.AppendCertsFromPEM(pem.EncodeToMemory(block)) {
		pools.espError = "esp_root_append_failed"
		pools.systemESP = nil
	}
	return pools
}

func loadSystemPool() (*x509.CertPool, string) {
	pool, err := x509.SystemCertPool()
	if err != nil || pool == nil {
		return nil, "system_roots_unavailable"
	}
	return pool, ""
}

func probeAll(ctx context.Context, discovery EsmDiscovery) []CdnResult {
	results := make([]CdnResult, len(discovery.Endpoints))
	for i, endpoint := range discovery.Endpoints {
		results[i].Endpoint = endpoint
	}
	pools := loadTrustPools(filepath.Join(esmUM, "gismt_base.crt"))
	jobs := make(chan int)
	var wg sync.WaitGroup
	for worker := 0; worker < workers; worker++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for index := range jobs {
				results[index] = probeOne(ctx, discovery, discovery.Endpoints[index], pools)
			}
		}()
	}
sendLoop:
	for index := range discovery.Endpoints {
		select {
		case jobs <- index:
		case <-ctx.Done():
			break sendLoop
		}
	}
	close(jobs)
	wg.Wait()
	for i := range results {
		if results[i].WindowsDNS.Status == "" && ctx.Err() != nil {
			results[i].Cancelled = true
		}
	}
	return results
}

func probeOne(parent context.Context, discovery EsmDiscovery, endpoint CdnEndpoint, pools trustPools) CdnResult {
	ctx, cancel := context.WithTimeout(parent, perCDNTimeout)
	defer cancel()
	result := CdnResult{Endpoint: endpoint}
	result.Cache = preferredCacheEntry(discovery.CacheEntries, endpoint)
	result.WindowsDNS = (WindowsDNSResolver{}).Lookup(ctx, endpoint.Host)
	result.GoDNS = (GoDNSResolver{}).Lookup(ctx, endpoint.Host)
	result.DNSComparison = compareDNS(result.WindowsDNS, result.GoDNS)
	if ctx.Err() == nil {
		result.PowerShellDefault = runPowerShellHTTPSDefault(ctx, endpoint.Host)
	} else {
		result.PowerShellDefault = cancelledPowerShellResult()
	}
	if ctx.Err() == nil {
		result.PowerShellNoProxy = runPowerShellHTTPSNoProxy(ctx, endpoint.Host)
	} else {
		result.PowerShellNoProxy = cancelledPowerShellResult()
	}
	for _, ip := range uniqueIPs(result.WindowsDNS, result.GoDNS) {
		if ctx.Err() != nil {
			result.Cancelled = true
			break
		}
		tcpResult := tcpProbe(ctx, ip, endpoint.Port)
		result.TCP = append(result.TCP, tcpResult)
		dotNetTCP, dotNetTLS := runDotNetDirect(ctx, ip, endpoint)
		result.DotNetTCP = append(result.DotNetTCP, dotNetTCP)
		result.DotNetTLS = append(result.DotNetTLS, dotNetTLS...)
		if tcpResult.Status == "ok" {
			result.TLSSystem = append(result.TLSSystem, tlsProbe(ctx, ip, endpoint, pools.system, pools.systemError))
			if pools.systemESP != nil {
				result.TLSESP = append(result.TLSESP, tlsProbe(ctx, ip, endpoint, pools.systemESP, pools.espError))
			} else {
				result.TLSESP = append(result.TLSESP, TlsResult{IP: ip, Status: "not_run", Error: pools.espError})
			}
		}
	}
	result.ESPLeafMatch = leafMatch(discovery.ESPLeaf, result.TLSSystem)
	if ctx.Err() != nil {
		result.Cancelled = true
	}
	return result
}

func cancelledPowerShellResult() PowerShellResult {
	return PowerShellResult{ExitCode: -1, Category: "powershell_other", ErrorExcerpt: "cancelled"}
}

func preferredCacheEntry(entries []EsmCacheEntry, endpoint CdnEndpoint) *EsmCacheEntry {
	var fallback *EsmCacheEntry
	for i := range entries {
		entry := &entries[i]
		if entry.Host != endpoint.Host || entry.Port != endpoint.Port {
			continue
		}
		if entry.Selected {
			return entry
		}
		if fallback == nil {
			fallback = entry
		}
	}
	return fallback
}
func uniqueIPs(a, b DnsResult) []string {
	return sortedUnique(append(append(append(append([]string{}, a.IPv4...), a.IPv6...), b.IPv4...), b.IPv6...))
}

func tcpProbe(ctx context.Context, ip string, port int) TcpResult {
	return tcpProbeWithDial(ctx, ip, port, (&net.Dialer{}).DialContext)
}
func tcpProbeWithDial(ctx context.Context, ip string, port int, dial dialContextFunc) TcpResult {
	address, err := literalSocketAddress(ip, port)
	if err != nil {
		return TcpResult{IP: ip, Status: "fail", Error: "invalid_ip_literal"}
	}
	started := time.Now()
	dialCtx, cancel := context.WithTimeout(ctx, tcpTimeout)
	defer cancel()
	conn, err := dial(dialCtx, "tcp", address)
	result := TcpResult{IP: ip, DurationMS: time.Since(started).Milliseconds()}
	if err != nil {
		result.Status, result.Error = "fail", classifyTCP(err)
		return result
	}
	defer conn.Close()
	result.Status = "ok"
	result.LocalAddress = conn.LocalAddr().String()
	result.RemoteAddress = conn.RemoteAddr().String()
	return result
}

func tlsProbe(ctx context.Context, ip string, endpoint CdnEndpoint, roots *x509.CertPool, rootsError string) TlsResult {
	return tlsProbeWithDial(ctx, ip, endpoint, roots, rootsError, (&net.Dialer{}).DialContext)
}

func tlsProbeWithDial(ctx context.Context, ip string, endpoint CdnEndpoint, roots *x509.CertPool, rootsError string, dial dialContextFunc) TlsResult {
	if roots == nil {
		return TlsResult{IP: ip, Status: "fail", Error: rootsError}
	}
	address, err := literalSocketAddress(ip, endpoint.Port)
	if err != nil {
		return TlsResult{IP: ip, Status: "fail", Error: "invalid_ip_literal"}
	}
	dialCtx, cancelDial := context.WithTimeout(ctx, tcpTimeout)
	conn, err := dial(dialCtx, "tcp", address)
	cancelDial()
	if err != nil {
		return TlsResult{IP: ip, Status: "fail", Error: classifyTCP(err)}
	}
	defer conn.Close()
	tlsConn := tls.Client(conn, newTLSConfig(endpoint.Host, roots))
	handshakeCtx, cancelHandshake := context.WithTimeout(ctx, tlsTimeout)
	defer cancelHandshake()
	started := time.Now()
	err = tlsConn.HandshakeContext(handshakeCtx)
	result := TlsResult{IP: ip, DurationMS: time.Since(started).Milliseconds()}
	if err != nil {
		result.Status, result.Error = "fail", classifyTLS(err)
		return result
	}
	state := tlsConn.ConnectionState()
	result.Status = "ok"
	result.Version = tls.VersionName(state.Version)
	result.CipherSuite = tls.CipherSuiteName(state.CipherSuite)
	result.ALPN = state.NegotiatedProtocol
	if len(state.PeerCertificates) > 0 {
		result.Certificate = certificateSummary(state.PeerCertificates[0])
	}
	return result
}

func newTLSConfig(host string, roots *x509.CertPool) *tls.Config {
	return &tls.Config{ServerName: host, RootCAs: roots, MinVersion: tls.VersionTLS12}
}
func literalSocketAddress(ip string, port int) (string, error) {
	address, err := netip.ParseAddr(ip)
	if err != nil || port < 1 || port > 65535 {
		return "", errors.New("invalid literal endpoint")
	}
	return net.JoinHostPort(address.String(), strconv.Itoa(port)), nil
}
func isLiteralSocketAddress(address string) bool {
	host, port, err := net.SplitHostPort(address)
	if err != nil {
		return false
	}
	parsedPort, err := strconv.Atoi(port)
	if err != nil || parsedPort < 1 || parsedPort > 65535 {
		return false
	}
	_, err = netip.ParseAddr(host)
	return err == nil
}
func sortedUnique(values []string) []string {
	seen := make(map[string]struct{}, len(values))
	out := make([]string, 0, len(values))
	for _, value := range values {
		if _, exists := seen[value]; exists {
			continue
		}
		seen[value] = struct{}{}
		out = append(out, value)
	}
	sort.Strings(out)
	return out
}
func compareDNS(a, b DnsResult) string {
	if a.Status == "ok" && b.Status == "ok" {
		x := sortedUnique(append(append([]string{}, a.IPv4...), a.IPv6...))
		y := sortedUnique(append(append([]string{}, b.IPv4...), b.IPv6...))
		if strings.Join(x, ",") == strings.Join(y, ",") {
			return "same_result"
		}
		return "different_ip_set"
	}
	if a.Status == "ok" {
		return "windows_only_success"
	}
	if b.Status == "ok" {
		return "go_only_success"
	}
	return "both_failed"
}
func leafMatch(local *CertificateSummary, results []TlsResult) *bool {
	if local == nil {
		return nil
	}
	for _, result := range results {
		if result.Certificate != nil {
			match := strings.EqualFold(local.SHA256, result.Certificate.SHA256)
			return &match
		}
	}
	return nil
}

func classifyDNS(err error) string {
	if errors.Is(err, context.DeadlineExceeded) {
		return "dns_timeout"
	}
	var dnsErr *net.DNSError
	if errors.As(err, &dnsErr) && dnsErr.IsNotFound {
		return "dns_not_found"
	}
	if errors.As(err, &dnsErr) && (dnsErr.IsTemporary || dnsErr.IsTimeout) {
		return "dns_temp_failure"
	}
	return "dns_other"
}
func classifyTCP(err error) string {
	if errors.Is(err, context.DeadlineExceeded) {
		return "tcp_timeout"
	}
	var netErr net.Error
	if errors.As(err, &netErr) && netErr.Timeout() {
		return "tcp_timeout"
	}
	message := strings.ToLower(err.Error())
	if strings.Contains(message, "refused") {
		return "connection_refused"
	}
	if strings.Contains(message, "host is down") || strings.Contains(message, "host unreachable") {
		return "host_unreachable"
	}
	if strings.Contains(message, "unreachable") {
		return "network_unreachable"
	}
	return "tcp_other"
}
func classifyTLS(err error) string {
	if errors.Is(err, context.DeadlineExceeded) {
		return "handshake_timeout"
	}
	var netErr net.Error
	if errors.As(err, &netErr) && netErr.Timeout() {
		return "handshake_timeout"
	}
	var unknown x509.UnknownAuthorityError
	if errors.As(err, &unknown) {
		return "unknown_authority"
	}
	var hostname x509.HostnameError
	if errors.As(err, &hostname) {
		return "hostname_mismatch"
	}
	var invalid x509.CertificateInvalidError
	if errors.As(err, &invalid) {
		if invalid.Cert != nil && time.Now().Before(invalid.Cert.NotBefore) {
			return "certificate_not_yet_valid"
		}
		if invalid.Reason == x509.Expired {
			return "certificate_expired"
		}
	}
	message := strings.ToLower(err.Error())
	if strings.Contains(message, "protocol version") {
		return "protocol_version"
	}
	if strings.Contains(message, "closed") || strings.Contains(message, "eof") {
		return "connection_closed"
	}
	return "tls_other"
}
func safeError(err error) string {
	if errors.Is(err, context.DeadlineExceeded) {
		return "timeout"
	}
	return "operation failed"
}
