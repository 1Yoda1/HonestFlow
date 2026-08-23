package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"crypto/rsa"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/json"
	"encoding/pem"
	"errors"
	"math/big"
	"net"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestCacheParsingAndRedaction(t *testing.T) {
	b := []byte(`{"items":[{"host":"cdn01.crpt.ru","latency_ms":18,"block":false,"token":"secret"},{"host":"evil.example","port":19101}]}`)
	e := parseCache(b)
	if len(e) != 2 || e[0].Host != "cdn01.crpt.ru" {
		t.Fatal(e)
	}
	d := EsmDiscovery{CacheEntries: e}
	for _, x := range e {
		if allowedCRPT(x.Host, x.Port) {
			d.Endpoints = append(d.Endpoints, CdnEndpoint{x.Host, x.Port})
		}
	}
	raw, _ := json.Marshal(d)
	if string(raw) == "" || contains(string(raw), "secret") {
		t.Fatal("sensitive cache field leaked")
	}
}
func TestNormalizeAndAllowlist(t *testing.T) {
	h, p, ok := normalizeEndpoint("https://cdn01-ts.crpt.ru:19101", 0)
	if !ok || h != "cdn01-ts.crpt.ru" || p != 19101 {
		t.Fatal(h, p, ok)
	}
	if allowedCRPT("evil.example", 19101) || !allowedCRPT("crpt.ru", 19101) {
		t.Fatal("allowlist")
	}
}

func TestAllowlistRejectsInjectionAndMalformedNames(t *testing.T) {
	bad := []string{"evil.example", "evilcrpt.ru", "crpt.ru.evil.com", "cdn01.crpt.ru.evil.example", "cdn01..crpt.ru", "-cdn.crpt.ru", "cdn_.crpt.ru", "xn--e1afmkfd.crpt.ru", "cdn.crpt.ru/path", "cdn.crpt.ru\n.evil", "127.0.0.1"}
	for _, host := range bad {
		if allowedCRPT(host, 19101) {
			t.Fatalf("accepted unsafe host %q", host)
		}
	}
	if allowedCRPT("cdn01.crpt.ru", 443) {
		t.Fatal("accepted unsafe port")
	}
	if _, _, ok := normalizeEndpoint("https://cdn01.crpt.ru:19101/path", 0); ok {
		t.Fatal("accepted URL path")
	}
	if _, _, ok := normalizeEndpoint("https://user@cdn01.crpt.ru:19101", 0); ok {
		t.Fatal("accepted userinfo")
	}
	if _, _, ok := normalizeEndpoint("http://cdn01.crpt.ru:19101", 0); ok {
		t.Fatal("accepted non-HTTPS scheme")
	}
	host, port, ok := normalizeEndpoint("HTTPS://CDN01.CRPT.RU.:19101/", 0)
	if !ok || host != "cdn01.crpt.ru" || port != 19101 {
		t.Fatalf("normalized=%s:%d ok=%v", host, port, ok)
	}
}

func TestDiscoveryCacheStatesAndInvalidJSON(t *testing.T) {
	missing := discoverESMAt(filepath.Join(t.TempDir(), "missing"))
	if missing.CacheDirectoryStatus != "cacheDirectoryMissing" {
		t.Fatalf("missing=%+v", missing)
	}
	root := t.TempDir()
	empty := discoverESMAt(root)
	if empty.CacheDirectoryStatus != "cacheDirectoryMissing" {
		t.Fatalf("empty root=%+v", empty)
	}
	cache := filepath.Join(root, "cache")
	if err := os.Mkdir(cache, 0700); err != nil {
		t.Fatal(err)
	}
	presentEmpty := discoverESMAt(root)
	if presentEmpty.CacheDirectoryStatus != "cacheDirectoryPresentButEmpty" {
		t.Fatalf("present=%+v", presentEmpty)
	}
	if err := os.WriteFile(filepath.Join(cache, "bad_cdn_cache.json"), []byte(`{"bad":`), 0600); err != nil {
		t.Fatal(err)
	}
	invalid := discoverESMAt(root)
	if invalid.CacheDirectoryStatus != "cacheParseFailed" || invalid.CacheParseFailed != 1 {
		t.Fatalf("invalid=%+v", invalid)
	}
}
func TestDNSComparison(t *testing.T) {
	ok := DnsResult{Status: "ok", IPv4: []string{"1.1.1.1"}}
	if compareDNS(ok, ok) != "same_result" {
		t.Fatal()
	}
	if compareDNS(ok, DnsResult{Status: "fail"}) != "windows_only_success" {
		t.Fatal()
	}
}
func TestTLSErrorClassification(t *testing.T) {
	e := x509.UnknownAuthorityError{}
	if classifyTLS(e) != "unknown_authority" {
		t.Fatal(classifyTLS(e))
	}
}
func TestAdditionalTLSErrorClassification(t *testing.T) {
	now := time.Now()
	tests := []struct {
		err  error
		want string
	}{{x509.HostnameError{}, "hostname_mismatch"}, {x509.CertificateInvalidError{Cert: &x509.Certificate{NotAfter: now.Add(-time.Hour)}, Reason: x509.Expired}, "certificate_expired"}, {x509.CertificateInvalidError{Cert: &x509.Certificate{NotBefore: now.Add(time.Hour)}, Reason: x509.Expired}, "certificate_not_yet_valid"}, {errors.New("tls: protocol version not supported"), "protocol_version"}, {errors.New("EOF"), "connection_closed"}}
	for _, test := range tests {
		if got := classifyTLS(test.err); got != test.want {
			t.Fatalf("err=%v got=%s want=%s", test.err, got, test.want)
		}
	}
}
func TestTimeoutClassification(t *testing.T) {
	if classifyDNS(context.DeadlineExceeded) != "dns_timeout" || classifyTCP(context.DeadlineExceeded) != "tcp_timeout" || classifyTLS(context.DeadlineExceeded) != "handshake_timeout" {
		t.Fatal("timeout classification")
	}
}
func TestLeafFingerprintComparison(t *testing.T) {
	local := &CertificateSummary{SHA256: "a"}
	yes := leafMatch(local, []TlsResult{{Certificate: &CertificateSummary{SHA256: "a"}}})
	no := leafMatch(local, []TlsResult{{Certificate: &CertificateSummary{SHA256: "b"}}})
	if yes == nil || !*yes || no == nil || *no {
		t.Fatal("fingerprint comparison")
	}
}
func TestCertificateSummaryDoesNotContainRawCertificate(t *testing.T) {
	s := certificateSummary(&x509.Certificate{Raw: []byte("raw-certificate"), RawSubjectPublicKeyInfo: []byte("spki")})
	b, _ := json.Marshal(s)
	if contains(string(b), "raw-certificate") {
		t.Fatal("raw certificate leaked")
	}
}

func TestGoTLSControlAgainstLocalServer(t *testing.T) {
	serverCertificate, leaf := makeLocalTLSServerCertificate(t, "probe.test")
	listener, err := tls.Listen("tcp", "127.0.0.1:0", &tls.Config{
		Certificates: []tls.Certificate{serverCertificate},
		MinVersion:   tls.VersionTLS12,
		MaxVersion:   tls.VersionTLS12,
	})
	if err != nil {
		t.Fatal(err)
	}
	defer listener.Close()
	serverDone := make(chan error, 1)
	go func() {
		conn, acceptErr := listener.Accept()
		if acceptErr != nil {
			serverDone <- acceptErr
			return
		}
		defer conn.Close()
		_ = conn.SetDeadline(time.Now().Add(5 * time.Second))
		serverDone <- conn.(*tls.Conn).Handshake()
	}()
	roots := x509.NewCertPool()
	roots.AddCert(leaf)
	endpoint := CdnEndpoint{Host: "probe.test", Port: listener.Addr().(*net.TCPAddr).Port}
	result := tlsProbe(context.Background(), "127.0.0.1", endpoint, roots, "")
	if result.Status != "ok" || result.Version != "TLS 1.2" || result.Certificate == nil || result.Certificate.Subject != leaf.Subject.String() {
		t.Fatalf("result=%+v", result)
	}
	if err := <-serverDone; err != nil {
		t.Fatal(err)
	}
}
func TestCacheTimeParsing(t *testing.T) {
	e := parseCache([]byte(`[{"hostname":"cdn01.crpt.ru","last_checked_utc":"2026-08-20T10:00:00Z"}]`))
	if len(e) != 1 || e[0].LastChecked == nil || !e[0].LastChecked.Equal(time.Date(2026, 8, 20, 10, 0, 0, 0, time.UTC)) {
		t.Fatal(e)
	}
}

func TestCacheSupportsHostnameMapKeys(t *testing.T) {
	entries := parseCache([]byte(`{"cdn02-ts.crpt.ru":{"latency_ms":12,"blocked":true}}`))
	if len(entries) != 1 || entries[0].Host != "cdn02-ts.crpt.ru" || entries[0].Blocked == nil || !*entries[0].Blocked {
		t.Fatalf("entries=%+v", entries)
	}
}

func TestDiscoveryCollectsCachesSelectsNewestAndFiltersEndpoints(t *testing.T) {
	root := t.TempDir()
	cacheDir := filepath.Join(root, "cache")
	if err := os.Mkdir(cacheDir, 0700); err != nil {
		t.Fatal(err)
	}
	oldPath := filepath.Join(cacheDir, "old_cdn_cache.json")
	newPath := filepath.Join(cacheDir, "new_cdn_cache.json")
	if err := os.WriteFile(oldPath, []byte(`[{"host":"cdn01.crpt.ru"},{"host":"cdn01.crpt.ru"},{"host":"evil.example"}]`), 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(newPath, []byte(`[{"host":"cdn01.crpt.ru"},{"host":"cdn02.crpt.ru"}]`), 0600); err != nil {
		t.Fatal(err)
	}
	now := time.Now()
	if err := os.Chtimes(oldPath, now.Add(-time.Hour), now.Add(-time.Hour)); err != nil {
		t.Fatal(err)
	}
	if err := os.Chtimes(newPath, now, now); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(root, "private-key.pem"), []byte("must not be read"), 0000); err != nil {
		t.Fatal(err)
	}
	d := discoverESMAt(root)
	if d.CacheFiles != 2 || d.SelectedCache != "new_cdn_cache.json" || len(d.Endpoints) != 2 || d.IgnoredEndpoints != 1 {
		t.Fatalf("discovery=%+v", d)
	}
	for _, entry := range d.CacheEntries {
		if !allowedCRPT(entry.Host, entry.Port) {
			t.Fatalf("unsafe entry retained: %+v", entry)
		}
	}
}

func TestPureGoResolverIsForcedAndDNSDialRequiresLiteralIP(t *testing.T) {
	called := false
	var networks []string
	r := newPureGoResolver(func(_ context.Context, network, _ string) (net.Conn, error) {
		called = true
		networks = append(networks, network)
		return nil, errors.New("stop")
	})
	if !r.PreferGo {
		t.Fatal("pure Go resolver is not forced")
	}
	if _, err := r.Dial(context.Background(), "udp", "dns.example:53"); err == nil || called {
		t.Fatal("hostname DNS server address was not rejected")
	}
	if _, err := r.Dial(context.Background(), "udp", "192.0.2.53:53"); err == nil || !called {
		t.Fatal("literal DNS server address did not reach dialer")
	}
	if _, err := r.Dial(context.Background(), "tcp", "[2001:db8::53]:53"); err == nil {
		t.Fatal("expected fake dial error")
	}
	if strings.Join(networks, ",") != "udp,tcp" {
		t.Fatalf("DNS transports changed: %v", networks)
	}
}

func TestTCPDialUsesLiteralIPAndClosesConnection(t *testing.T) {
	client, server := net.Pipe()
	defer server.Close()
	var got string
	result := tcpProbeWithDial(context.Background(), "203.0.113.10", 19101, func(_ context.Context, network, address string) (net.Conn, error) {
		if network != "tcp" {
			t.Fatalf("network=%s", network)
		}
		got = address
		return client, nil
	})
	if result.Status != "ok" || got != "203.0.113.10:19101" {
		t.Fatalf("result=%+v address=%q", result, got)
	}
	_ = server.SetReadDeadline(time.Now().Add(time.Second))
	one := make([]byte, 1)
	if _, err := server.Read(one); err == nil {
		t.Fatal("TCP connection was not closed")
	}
}

func TestIPv6LiteralAddress(t *testing.T) {
	got, err := literalSocketAddress("2001:db8::1", 19101)
	if err != nil || got != "[2001:db8::1]:19101" {
		t.Fatalf("got=%q err=%v", got, err)
	}
}

func TestTCPTimeoutHonorsContext(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 25*time.Millisecond)
	defer cancel()
	started := time.Now()
	result := tcpProbeWithDial(ctx, "203.0.113.10", 19101, func(ctx context.Context, _, _ string) (net.Conn, error) { <-ctx.Done(); return nil, ctx.Err() })
	if result.Error != "tcp_timeout" || time.Since(started) > time.Second {
		t.Fatalf("result=%+v elapsed=%s", result, time.Since(started))
	}
}

func TestTLSConfigUsesSNIAndVerification(t *testing.T) {
	pool := x509.NewCertPool()
	config := newTLSConfig("cdn01.crpt.ru", pool)
	if config.ServerName != "cdn01.crpt.ru" || config.RootCAs != pool || config.InsecureSkipVerify {
		t.Fatalf("unsafe TLS config: %+v", config)
	}
}

func TestTLSDialUsesLiteralIPAndClosesConnection(t *testing.T) {
	client, server := net.Pipe()
	var got string
	done := make(chan struct{})
	go func() {
		defer close(done)
		defer server.Close()
		buffer := make([]byte, 4096)
		_, _ = server.Read(buffer)
	}()
	result := tlsProbeWithDial(context.Background(), "203.0.113.11", CdnEndpoint{Host: "cdn01.crpt.ru", Port: 19101}, x509.NewCertPool(), "", func(_ context.Context, network, address string) (net.Conn, error) {
		if network != "tcp" {
			t.Errorf("network=%s", network)
		}
		got = address
		return client, nil
	})
	if got != "203.0.113.11:19101" || result.Status != "fail" {
		t.Fatalf("address=%q result=%+v", got, result)
	}
	select {
	case <-done:
	case <-time.After(time.Second):
		t.Fatal("TLS connection was not closed")
	}
}

func TestESPTrustPoolIsPrivateAndRejectsLeaf(t *testing.T) {
	caPEM, ca := makeTestCertificate(t, true)
	path := filepath.Join(t.TempDir(), "gismt_base.crt")
	if err := os.WriteFile(path, caPEM, 0600); err != nil {
		t.Fatal(err)
	}
	pools := loadTrustPools(path)
	if pools.system == nil || pools.systemESP == nil || pools.espError != "" {
		t.Fatalf("pools=%+v", pools)
	}
	for _, subject := range pools.system.Subjects() {
		if bytes.Equal(subject, ca.RawSubject) {
			t.Fatal("ESP CA contaminated system-only pool")
		}
	}
	found := false
	for _, subject := range pools.systemESP.Subjects() {
		if bytes.Equal(subject, ca.RawSubject) {
			found = true
		}
	}
	if !found {
		t.Fatal("ESP CA absent from private ESP pool")
	}
	leafPEM, _ := makeTestCertificate(t, false)
	leafPath := filepath.Join(t.TempDir(), "gismt.crt")
	if err := os.WriteFile(leafPath, leafPEM, 0600); err != nil {
		t.Fatal(err)
	}
	leafPools := loadTrustPools(leafPath)
	if leafPools.systemESP != nil || leafPools.espError != "esp_root_not_ca" {
		t.Fatalf("leaf accepted as root: %+v", leafPools)
	}
}
func TestCertificateMissingAndInvalidAreSafe(t *testing.T) {
	if cert, category := parseCertFile(filepath.Join(t.TempDir(), "missing.crt")); cert != nil || category != "not_found" {
		t.Fatalf("cert=%v category=%s", cert, category)
	}
	path := filepath.Join(t.TempDir(), "invalid.crt")
	if err := os.WriteFile(path, []byte("not a certificate"), 0600); err != nil {
		t.Fatal(err)
	}
	if cert, category := parseCertFile(path); cert != nil || category != "parse_failed" {
		t.Fatalf("cert=%v category=%s", cert, category)
	}
	pools := loadTrustPools(path)
	if pools.systemESP != nil || pools.espError != "esp_root_parse_failed" {
		t.Fatalf("pools=%+v", pools)
	}
}

func TestProbeAllHonorsPreCancelledContext(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	results := probeAll(ctx, EsmDiscovery{Endpoints: []CdnEndpoint{{Host: "cdn01.crpt.ru", Port: 19101}}})
	if len(results) != 1 || !results[0].Cancelled {
		t.Fatalf("results=%+v", results)
	}
}

func makeTestCertificate(t *testing.T, isCA bool) ([]byte, *x509.Certificate) {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	now := time.Now()
	template := &x509.Certificate{SerialNumber: big.NewInt(1), Subject: pkix.Name{CommonName: "GoNetworkProbe test"}, NotBefore: now.Add(-time.Hour), NotAfter: now.Add(time.Hour), BasicConstraintsValid: true, IsCA: isCA, KeyUsage: x509.KeyUsageDigitalSignature}
	if isCA {
		template.KeyUsage |= x509.KeyUsageCertSign
	}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	cert, err := x509.ParseCertificate(der)
	if err != nil {
		t.Fatal(err)
	}
	return pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}), cert
}

func makeLocalTLSServerCertificate(t *testing.T, host string) (tls.Certificate, *x509.Certificate) {
	t.Helper()
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	now := time.Now()
	template := &x509.Certificate{
		SerialNumber: big.NewInt(2),
		Subject:      pkix.Name{CommonName: host},
		DNSNames:     []string{host},
		NotBefore:    now.Add(-time.Hour),
		NotAfter:     now.Add(time.Hour),
		KeyUsage:     x509.KeyUsageDigitalSignature | x509.KeyUsageKeyEncipherment,
		ExtKeyUsage:  []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth},
	}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	keyDER := x509.MarshalPKCS1PrivateKey(key)
	certificate, err := tls.X509KeyPair(
		pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der}),
		pem.EncodeToMemory(&pem.Block{Type: "RSA PRIVATE KEY", Bytes: keyDER}),
	)
	if err != nil {
		t.Fatal(err)
	}
	leaf, err := x509.ParseCertificate(der)
	if err != nil {
		t.Fatal(err)
	}
	return certificate, leaf
}
func contains(s, x string) bool {
	for i := 0; i+len(x) <= len(s); i++ {
		if s[i:i+len(x)] == x {
			return true
		}
	}
	return false
}
