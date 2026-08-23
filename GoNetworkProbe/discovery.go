package main

import (
	"crypto/sha256"
	"crypto/x509"
	"encoding/hex"
	"encoding/json"
	"encoding/pem"
	"errors"
	"io"
	"net"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"
)

const (
	esmUM                  = `C:\ProgramData\ESP\ESM\um`
	maxCacheFileSize int64 = 4 << 20
	maxCertFileSize  int64 = 1 << 20
)

func discoverESM() EsmDiscovery {
	return discoverESMAt(esmUM)
}

func discoverESMAt(root string) EsmDiscovery {
	d := EsmDiscovery{CacheDirectoryStatus: "cacheDirectoryMissing", Endpoints: []CdnEndpoint{}, CacheEntries: []EsmCacheEntry{}}
	if info, err := os.Stat(root); err != nil || !info.IsDir() {
		return d
	}
	d.Detected = true
	cacheDirectory := filepath.Join(root, "cache")
	cacheInfo, err := os.Stat(cacheDirectory)
	if err != nil || !cacheInfo.IsDir() {
		if err != nil && !errors.Is(err, os.ErrNotExist) {
			d.CacheDirectoryStatus = "cacheDirectoryUnreadable"
		}
		d.ESPLeaf, d.ESPLeafError = parseCertFile(filepath.Join(root, "gismt.crt"))
		d.ESPRoot, d.ESPRootError = parseCertFile(filepath.Join(root, "gismt_base.crt"))
		return d
	}
	directoryEntries, err := os.ReadDir(cacheDirectory)
	if err != nil {
		d.CacheDirectoryStatus = "cacheDirectoryUnreadable"
		d.ESPLeaf, d.ESPLeafError = parseCertFile(filepath.Join(root, "gismt.crt"))
		d.ESPRoot, d.ESPRootError = parseCertFile(filepath.Join(root, "gismt_base.crt"))
		return d
	}
	files := make([]string, 0)
	for _, entry := range directoryEntries {
		if !entry.IsDir() && strings.HasSuffix(strings.ToLower(entry.Name()), "_cdn_cache.json") {
			files = append(files, filepath.Join(cacheDirectory, entry.Name()))
		}
	}
	sort.Strings(files)
	d.CacheFiles = len(files)
	if len(files) == 0 {
		d.CacheDirectoryStatus = "cacheDirectoryPresentButEmpty"
	} else {
		d.CacheDirectoryStatus = "cacheFilesFound"
	}
	var selected string
	var selectedTime time.Time
	for _, path := range files {
		info, err := os.Stat(path)
		if err != nil {
			d.CacheReadFailed++
			continue
		}
		name := filepath.Base(path)
		if selected == "" || info.ModTime().After(selectedTime) {
			selected, selectedTime = name, info.ModTime()
		}
		entries, err := parseCacheFile(path)
		if err != nil {
			var syntaxError *json.SyntaxError
			if errors.As(err, &syntaxError) || strings.Contains(err.Error(), "cache file too large") {
				d.CacheParseFailed++
			} else {
				d.CacheReadFailed++
			}
			continue
		}
		for i := range entries {
			entries[i].CacheFile = name
			if !allowedCRPT(entries[i].Host, entries[i].Port) {
				d.IgnoredEndpoints++
				continue
			}
			d.CacheEntries = append(d.CacheEntries, entries[i])
		}
	}
	if d.CacheFiles > 0 && d.CacheParseFailed+d.CacheReadFailed == d.CacheFiles {
		if d.CacheReadFailed > 0 {
			d.CacheDirectoryStatus = "cacheReadFailed"
		} else {
			d.CacheDirectoryStatus = "cacheParseFailed"
		}
	}
	d.SelectedCache = selected
	for i := range d.CacheEntries {
		d.CacheEntries[i].Selected = d.CacheEntries[i].CacheFile == selected
	}
	seen := make(map[string]struct{})
	for _, entry := range d.CacheEntries {
		key := net.JoinHostPort(entry.Host, strconv.Itoa(entry.Port))
		if _, exists := seen[key]; exists {
			continue
		}
		seen[key] = struct{}{}
		d.Endpoints = append(d.Endpoints, CdnEndpoint{Host: entry.Host, Port: entry.Port})
	}
	sort.Slice(d.Endpoints, func(i, j int) bool {
		if d.Endpoints[i].Host == d.Endpoints[j].Host {
			return d.Endpoints[i].Port < d.Endpoints[j].Port
		}
		return d.Endpoints[i].Host < d.Endpoints[j].Host
	})
	d.ESPLeaf, d.ESPLeafError = parseCertFile(filepath.Join(root, "gismt.crt"))
	d.ESPRoot, d.ESPRootError = parseCertFile(filepath.Join(root, "gismt_base.crt"))
	return d
}

func parseCacheFile(path string) ([]EsmCacheEntry, error) {
	b, err := readLimitedFile(path, maxCacheFileSize)
	if err != nil {
		return nil, err
	}
	var value any
	if err := json.Unmarshal(b, &value); err != nil {
		return nil, err
	}
	var found []EsmCacheEntry
	walkCache(value, &found)
	return found, nil
}
func parseCache(b []byte) []EsmCacheEntry {
	var value any
	if json.Unmarshal(b, &value) != nil {
		return nil
	}
	var found []EsmCacheEntry
	walkCache(value, &found)
	return found
}
func walkCache(value any, out *[]EsmCacheEntry) {
	switch current := value.(type) {
	case []any:
		for _, item := range current {
			walkCache(item, out)
		}
	case map[string]any:
		if entry, ok := cacheEntry(current); ok {
			*out = append(*out, entry)
		}
		for key, nested := range current {
			keyHost, keyPort, keyAllowed := normalizeEndpoint(key, 0)
			if nestedMap, ok := nested.(map[string]any); ok && keyAllowed && allowedCRPT(keyHost, keyPort) && stringField(nestedMap, "host", "hostname", "cdn_host", "cdnHost") == "" {
				copyWithHost := make(map[string]any, len(nestedMap)+1)
				for nestedKey, nestedValue := range nestedMap {
					copyWithHost[nestedKey] = nestedValue
				}
				copyWithHost["host"] = key
				if entry, valid := cacheEntry(copyWithHost); valid {
					*out = append(*out, entry)
				}
			}
			switch nested.(type) {
			case map[string]any, []any:
				walkCache(nested, out)
			}
		}
	}
}
func cacheEntry(m map[string]any) (EsmCacheEntry, bool) {
	rawHost := stringField(m, "host", "hostname", "cdn_host", "cdnHost")
	if rawHost == "" {
		return EsmCacheEntry{}, false
	}
	host, port, ok := normalizeEndpoint(rawHost, numberField(m, "port", "cdn_port", "cdnPort"))
	if !ok {
		return EsmCacheEntry{}, false
	}
	return EsmCacheEntry{Host: host, Port: port, LatencyMS: numberPtr(m, "latency_ms", "latency", "latencyMs"), Blocked: boolPtr(m, "block", "blocked", "is_blocked", "isBlocked"), LastChecked: timePtr(m, "last_checked_utc", "lastCheckedUtc", "last_checked"), BlockUntil: timePtr(m, "block_until_utc", "blockUntilUtc", "block_until"), Active: boolPtr(m, "active", "is_active", "isActive")}, true
}

func normalizeEndpoint(raw string, numericPort float64) (string, int, bool) {
	raw = strings.TrimSpace(raw)
	if raw == "" || strings.ContainsAny(raw, "\r\n\t") {
		return "", 0, false
	}
	port := 19101
	if numericPort != 0 {
		if numericPort != float64(int(numericPort)) || numericPort < 1 || numericPort > 65535 {
			return "", 0, false
		}
		port = int(numericPort)
	}
	var host string
	if strings.Contains(raw, "://") {
		u, err := url.Parse(raw)
		if err != nil || u == nil {
			return "", 0, false
		}
		scheme := strings.ToLower(u.Scheme)
		if scheme != "https" || u.User != nil || (u.Path != "" && u.Path != "/") || u.RawQuery != "" || u.Fragment != "" {
			return "", 0, false
		}
		host = u.Hostname()
		if textPort := u.Port(); textPort != "" {
			parsed, err := strconv.Atoi(textPort)
			if err != nil || parsed < 1 || parsed > 65535 || (numericPort != 0 && parsed != port) {
				return "", 0, false
			}
			port = parsed
		}
	} else {
		if strings.ContainsAny(raw, "/\\?#@ ") {
			return "", 0, false
		}
		host = raw
		if splitHost, textPort, err := net.SplitHostPort(raw); err == nil {
			parsed, parseErr := strconv.Atoi(textPort)
			if parseErr != nil || parsed < 1 || parsed > 65535 || (numericPort != 0 && parsed != port) {
				return "", 0, false
			}
			host, port = splitHost, parsed
		} else if strings.Contains(raw, ":") {
			return "", 0, false
		}
	}
	host = strings.TrimSuffix(strings.ToLower(host), ".")
	if !validDNSName(host) {
		return "", 0, false
	}
	return host, port, true
}

func allowedCRPT(host string, port int) bool {
	host = strings.TrimSuffix(strings.ToLower(host), ".")
	return port == 19101 && validDNSName(host) && (host == "crpt.ru" || strings.HasSuffix(host, ".crpt.ru"))
}
func validDNSName(host string) bool {
	if host == "" || len(host) > 253 || strings.ContainsAny(host, "\r\n\t /\\:@?#") {
		return false
	}
	for _, label := range strings.Split(host, ".") {
		if strings.HasPrefix(label, "xn--") {
			return false
		}
		if len(label) == 0 || len(label) > 63 || label[0] == '-' || label[len(label)-1] == '-' {
			return false
		}
		for _, ch := range label {
			if !((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-') {
				return false
			}
		}
	}
	return true
}

func stringField(m map[string]any, keys ...string) string {
	for _, key := range keys {
		if value, ok := m[key].(string); ok {
			return value
		}
	}
	return ""
}
func numberField(m map[string]any, keys ...string) float64 {
	value := numberPtr(m, keys...)
	if value == nil {
		return 0
	}
	return *value
}
func numberPtr(m map[string]any, keys ...string) *float64 {
	for _, key := range keys {
		switch value := m[key].(type) {
		case float64:
			return &value
		case string:
			if parsed, err := strconv.ParseFloat(value, 64); err == nil {
				return &parsed
			}
		}
	}
	return nil
}
func boolPtr(m map[string]any, keys ...string) *bool {
	for _, key := range keys {
		if value, ok := m[key].(bool); ok {
			return &value
		}
	}
	return nil
}
func timePtr(m map[string]any, keys ...string) *time.Time {
	for _, key := range keys {
		if value, ok := m[key].(string); ok {
			if parsed, err := time.Parse(time.RFC3339, value); err == nil {
				return &parsed
			}
		}
	}
	return nil
}

func parseCertFile(path string) (*CertificateSummary, string) {
	b, err := readLimitedFile(path, maxCertFileSize)
	if errors.Is(err, os.ErrNotExist) {
		return nil, "not_found"
	}
	if err != nil {
		return nil, "read_failed"
	}
	cert, err := parseFirstCertificate(b)
	if err != nil {
		return nil, "parse_failed"
	}
	return certificateSummary(cert), ""
}
func readLimitedFile(path string, limit int64) ([]byte, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	b, err := io.ReadAll(io.LimitReader(f, limit+1))
	if err != nil {
		return nil, err
	}
	if int64(len(b)) > limit {
		return nil, errors.New("file too large")
	}
	return b, nil
}
func parseFirstCertificate(b []byte) (*x509.Certificate, error) {
	if block, _ := pem.Decode(b); block != nil && block.Type == "CERTIFICATE" {
		return x509.ParseCertificate(block.Bytes)
	}
	return x509.ParseCertificate(b)
}
func certificateSummary(cert *x509.Certificate) *CertificateSummary {
	fingerprint := sha256.Sum256(cert.Raw)
	spki := sha256.Sum256(cert.RawSubjectPublicKeyInfo)
	return &CertificateSummary{Subject: cert.Subject.String(), Issuer: cert.Issuer.String(), SHA256: hex.EncodeToString(fingerprint[:]), SPKISHA256: hex.EncodeToString(spki[:]), DNSNames: append([]string(nil), cert.DNSNames...), NotBefore: cert.NotBefore, NotAfter: cert.NotAfter}
}
