package main

import "time"

type ProbeRun struct {
	SchemaVersion        string                 `json:"schemaVersion"`
	ProbeVersion         string                 `json:"probeVersion"`
	StartedAt            time.Time              `json:"startedAt"`
	CompletedAt          time.Time              `json:"completedAt"`
	OS                   OSInfo                 `json:"os"`
	Proxy                ProxyConfiguration     `json:"proxy"`
	TLSRuntime           TLSRuntimeMetadata     `json:"tlsRuntime"`
	ESM                  EsmDiscovery           `json:"esm"`
	Adapters             []AdapterInfo          `json:"networkAdapters"`
	CDNResults           []CdnResult            `json:"perCdn"`
	Findings             []Finding              `json:"findings"`
	PowerShellComparison []PowerShellComparison `json:"powershellComparison"`
	ESMCorrelations      []EsmCorrelation       `json:"esmCorrelation"`
	Limitations          []string               `json:"limitations"`
	Summary              Summary                `json:"summary"`
	Cancelled            bool                   `json:"cancelled"`
}

type OSInfo struct {
	OS           string `json:"os"`
	Architecture string `json:"architecture"`
	Admin        bool   `json:"admin"`
}

type ProxyConfiguration struct {
	WinHTTP WinHTTPProxyConfiguration `json:"winHttp"`
	User    UserProxyConfiguration    `json:"currentUser"`
}

type WinHTTPProxyConfiguration struct {
	Status     string `json:"status"`
	AccessType string `json:"accessType,omitempty"`
	Proxy      string `json:"proxy,omitempty"`
	Error      string `json:"error,omitempty"`
}

type UserProxyConfiguration struct {
	Status               string `json:"status"`
	ProxyEnabled         *bool  `json:"proxyEnabled"`
	ProxyServer          string `json:"proxyServer,omitempty"`
	AutoConfigURLPresent *bool  `json:"autoConfigUrlPresent"`
	AutoDetect           *bool  `json:"autoDetect"`
	Error                string `json:"error,omitempty"`
}

type TLSRuntimeMetadata struct {
	PowerShell PowerShellRuntimeMetadata `json:"powerShell"`
	Windows    WindowsTLSMetadata        `json:"windows"`
}

type PowerShellRuntimeMetadata struct {
	Status               string `json:"status"`
	PowerShellVersion    string `json:"powerShellVersion,omitempty"`
	CLRVersion           string `json:"clrVersion,omitempty"`
	ReportedOSVersion    string `json:"reportedOsVersion,omitempty"`
	ServicePointSecurity string `json:"servicePointManagerSecurityProtocol,omitempty"`
	Error                string `json:"error,omitempty"`
}

type WindowsTLSMetadata struct {
	OSVersion              string                `json:"osVersion,omitempty"`
	OSBuild                uint32                `json:"osBuild,omitempty"`
	TLS12                  TLSProtocolCapability `json:"tls12"`
	TLS13                  TLSProtocolCapability `json:"tls13"`
	TLS13PlatformSupported bool                  `json:"tls13PlatformSupported"`
}

type TLSProtocolCapability struct {
	State             string `json:"state"`
	Enabled           *bool  `json:"enabled,omitempty"`
	DisabledByDefault *bool  `json:"disabledByDefault,omitempty"`
	Error             string `json:"error,omitempty"`
}

type EsmDiscovery struct {
	Detected             bool                `json:"detected"`
	CacheDirectoryStatus string              `json:"cacheDirectoryStatus"`
	CacheFiles           int                 `json:"cacheFiles"`
	CacheParseFailed     int                 `json:"cacheParseFailed"`
	CacheReadFailed      int                 `json:"cacheReadFailed"`
	SelectedCache        string              `json:"selectedCache,omitempty"`
	Endpoints            []CdnEndpoint       `json:"endpoints"`
	CacheEntries         []EsmCacheEntry     `json:"cacheEntries"`
	ESPLeaf              *CertificateSummary `json:"espLeaf,omitempty"`
	ESPRoot              *CertificateSummary `json:"espRoot,omitempty"`
	ESPLeafError         string              `json:"espLeafError,omitempty"`
	ESPRootError         string              `json:"espRootError,omitempty"`
	IgnoredEndpoints     int                 `json:"ignoredEndpoints"`
}

type CdnEndpoint struct {
	Host string `json:"host"`
	Port int    `json:"port"`
}
type EsmCacheEntry struct {
	Host        string     `json:"host"`
	Port        int        `json:"port"`
	LatencyMS   *float64   `json:"latencyMs,omitempty"`
	Blocked     *bool      `json:"blocked,omitempty"`
	LastChecked *time.Time `json:"lastChecked,omitempty"`
	BlockUntil  *time.Time `json:"blockUntil,omitempty"`
	CacheFile   string     `json:"-"`
	Selected    bool       `json:"selected"`
	Active      *bool      `json:"active,omitempty"`
}
type AdapterInfo struct {
	Name        string   `json:"name"`
	Description string   `json:"description"`
	Status      string   `json:"status"`
	Index       int      `json:"interfaceIndex"`
	IPv4        []string `json:"ipv4"`
	IPv6        []string `json:"ipv6"`
	Gateways    []string `json:"defaultGateways"`
	DNSServers  []string `json:"dnsServers"`
}
type DnsResult struct {
	Backend        string   `json:"backend"`
	ServersUsed    []string `json:"serversUsed"`
	TransportsUsed []string `json:"transportsUsed"`
	Status         string   `json:"status"`
	IPv4           []string `json:"ipv4"`
	IPv6           []string `json:"ipv6"`
	DurationMS     int64    `json:"durationMs"`
	Error          string   `json:"error,omitempty"`
}
type TcpResult struct {
	IP            string `json:"ip"`
	LocalAddress  string `json:"localAddress,omitempty"`
	RemoteAddress string `json:"remoteAddress,omitempty"`
	Status        string `json:"status"`
	Error         string `json:"error,omitempty"`
	DurationMS    int64  `json:"durationMs"`
}
type TlsResult struct {
	IP          string              `json:"ip"`
	Status      string              `json:"status"`
	Version     string              `json:"version,omitempty"`
	CipherSuite string              `json:"cipherSuite,omitempty"`
	ALPN        string              `json:"alpn,omitempty"`
	Error       string              `json:"error,omitempty"`
	DurationMS  int64               `json:"handshakeDurationMs"`
	Certificate *CertificateSummary `json:"certificate,omitempty"`
}

type DotNetTLSResult struct {
	IP                  string                     `json:"ip"`
	Mode                string                     `json:"mode"`
	Status              string                     `json:"status"`
	Phase               string                     `json:"phase"`
	Protocol            string                     `json:"protocol,omitempty"`
	DurationMS          int64                      `json:"handshakeDurationMs"`
	CertificateReceived bool                       `json:"certificateReceived"`
	Certificate         *CertificateSummary        `json:"certificate,omitempty"`
	PolicyErrors        []string                   `json:"policyErrors"`
	ChainStatus         []string                   `json:"chainStatus"`
	Category            string                     `json:"category,omitempty"`
	Exception           *DotNetExceptionDiagnostic `json:"exception,omitempty"`
}

type DotNetExceptionDiagnostic struct {
	ExceptionType       string   `json:"exceptionType,omitempty"`
	InnerExceptionTypes []string `json:"innerExceptionTypes"`
	HResult             string   `json:"hresult,omitempty"`
	SocketErrorCode     string   `json:"socketErrorCode,omitempty"`
	NativeErrorCode     int      `json:"nativeErrorCode,omitempty"`
	Category            string   `json:"category,omitempty"`
	SafeMessage         string   `json:"safeMessage,omitempty"`
}
type CertificateSummary struct {
	Subject    string    `json:"subject"`
	Issuer     string    `json:"issuer"`
	Thumbprint string    `json:"thumbprint,omitempty"`
	SHA256     string    `json:"sha256"`
	SPKISHA256 string    `json:"spkiSha256"`
	DNSNames   []string  `json:"dnsNames"`
	NotBefore  time.Time `json:"notBefore"`
	NotAfter   time.Time `json:"notAfter"`
}
type CdnResult struct {
	Endpoint          CdnEndpoint       `json:"endpoint"`
	Cache             *EsmCacheEntry    `json:"esmCache,omitempty"`
	WindowsDNS        DnsResult         `json:"windowsDns"`
	GoDNS             DnsResult         `json:"goDns"`
	DNSComparison     string            `json:"dnsComparison"`
	TCP               []TcpResult       `json:"goDirectTcp"`
	DotNetTCP         []TcpResult       `json:"dotNetDirectTcp"`
	TLSSystem         []TlsResult       `json:"tlsSystem"`
	TLSESP            []TlsResult       `json:"tlsSystemPlusEsp"`
	DotNetTLS         []DotNetTLSResult `json:"dotNetTls"`
	ESPLeafMatch      *bool             `json:"espLeafMatch,omitempty"`
	PowerShellDefault PowerShellResult  `json:"powershellDefault"`
	PowerShellNoProxy PowerShellResult  `json:"powershellNoProxy"`
	Correlation       EsmCorrelation    `json:"esmCorrelation"`
	Cancelled         bool              `json:"cancelled"`
}

type PowerShellResult struct {
	TransportSuccess bool   `json:"transportSuccess"`
	HTTPStatus       *int   `json:"httpStatus,omitempty"`
	ExitCode         int    `json:"exitCode"`
	DurationMS       int64  `json:"durationMs"`
	Category         string `json:"category"`
	ErrorExcerpt     string `json:"errorExcerpt,omitempty"`
}

type PowerShellComparison struct {
	Host         string           `json:"host"`
	GoTLSSuccess bool             `json:"goTlsSuccess"`
	Default      PowerShellResult `json:"default"`
	NoProxy      PowerShellResult `json:"noProxy"`
}
type EsmCorrelation struct {
	Host            string `json:"host"`
	Freshness       string `json:"freshness"`
	CacheAgeSeconds *int64 `json:"cacheAgeSeconds,omitempty"`
	Status          string `json:"status"`
	Details         string `json:"details"`
}
type Finding struct {
	Code          string            `json:"code"`
	Severity      string            `json:"severity"`
	Title         string            `json:"title"`
	Details       string            `json:"details"`
	AffectedHosts []string          `json:"affectedHosts"`
	Evidence      map[string]string `json:"evidence,omitempty"`
}
type Summary struct {
	CDNTested                int    `json:"cdnTested"`
	WindowsDNSSuccess        int    `json:"windowsDnsSuccess"`
	GoDNSSuccess             int    `json:"goDnsSuccess"`
	TCPSuccess               int    `json:"tcpSuccess"`
	TLSSystemSuccess         int    `json:"tlsSystemSuccess"`
	TLSESPSuccess            int    `json:"tlsEspSuccess"`
	DotNetTCPSuccess         int    `json:"dotNetDirectTcpSuccess"`
	DotNetTLSSuccess         int    `json:"dotNetSslStreamSuccess"`
	DotNetTLS12Success       int    `json:"dotNetTls12Success"`
	DotNetTLS13Success       int    `json:"dotNetTls13Success"`
	DotNetTLS13Unsupported   int    `json:"dotNetTls13Unsupported"`
	PowerShellDefaultSuccess int    `json:"powershellDefaultSuccess"`
	PowerShellNoProxySuccess int    `json:"powershellNoProxySuccess"`
	Reachable                int    `json:"reachable"`
	Unavailable              int    `json:"unavailable"`
	GIStransport             string `json:"gisTransport"`
	BestTCPHost              string `json:"bestTcpHost,omitempty"`
	BestTCPMS                int64  `json:"bestTcpMs,omitempty"`
	ProbeBestObserved        string `json:"probeBestObserved,omitempty"`
	ESMActive                string `json:"esmActive,omitempty"`
	ESMBestObserved          string `json:"esmBestObserved,omitempty"`
	ESMBlocked               int    `json:"esmBlocked"`
	ProxyWinHTTP             string `json:"proxyWinHttp"`
	ProxyUser                string `json:"proxyUser"`
	ProxyAutoConfig          string `json:"proxyAutoConfigWpad"`
	DirectPathDivergence     int    `json:"directPathDivergence"`
	DefaultNoProxyDivergence int    `json:"defaultVsNoProxyDivergence"`
}
