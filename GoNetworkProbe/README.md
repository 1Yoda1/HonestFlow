# GoNetworkProbe

Standalone Windows transport diagnostics for GIS MT CDN cache entries. It reads only
`*_cdn_cache.json`, `gismt.crt`, and `gismt_base.crt` from the installed ESM data.
It never invokes the local ESM API, authenticates, installs certificates, or changes
Windows/ESM configuration.

It diagnoses these transport layers independently:

- native Windows DNS and pure-Go DNS;
- Go and Windows/.NET direct-IP TCP connectivity to port 19101;
- Go TLS with system roots and with a private system-plus-ESP trust pool;
- Windows/.NET `SslStream` over fresh literal-IP connections in system-default,
  explicit TLS 1.2, and explicit TLS 1.3 modes;
- Windows PowerShell/.NET HTTPS with default routing and with `Proxy = null`;
- read-only PowerShell, CLR, Windows build, ServicePoint security-protocol, and
  Schannel TLS 1.2/TLS 1.3 client-policy metadata;
- read-only WinHTTP and current-user Internet Settings proxy state;
- observational state from the ESM CDN cache.

Transport boundaries:

- Windows DNS calls `ws2_32!GetAddrInfoW` directly.
- Go DNS uses `net.Resolver{PreferGo:true}` and permits only literal DNS-server
  socket addresses in its dial callback.
- TCP and TLS dial only validated literal IP addresses. TLS retains the original
  CDN hostname in `tls.Config.ServerName` for SNI and hostname verification.
- The .NET direct control parses a literal IP with `IPAddress.Parse`. Its TCP
  control and each TLS mode use a fresh `TcpClient` connection to port 19101,
  without hostname resolution. Every `SslStream` handshake uses the original CDN
  hostname for SNI and certificate-name verification.
- The `SslStream` certificate callback is observational only: it records whether
  a certificate was presented, certificate metadata, policy errors, and chain
  status, then accepts only `SslPolicyErrors.None`. It is not a certificate bypass.
- .NET TLS exceptions are reduced to safe structured diagnostics: phase,
  exception types, HRESULT/native/socket codes, category, and a redacted message.
  No stack traces or raw certificate bytes are written.
- The system-only and system-plus-ESP certificate pools are distinct in-process
  copies. Only `gismt_base.crt`, after CA validation, is appended to the latter.
  `gismt.crt` is parsed only for informational fingerprint comparison.
- Both Windows PowerShell comparisons perform only an unauthenticated GET to
  `/api/v4/cdn/health/check`. The default mode retains normal .NET proxy/WPAD
  behavior; the second request explicitly sets only that request's `Proxy` to
  `null`. Any HTTP response, including 4xx, proves that the HTTP transport was
  reached and is not classified as a network failure.
- Proxy discovery is read-only. It queries WinHTTP state and the current user's
  Internet Settings without changing registry, proxy, WPAD, or certificate-store
  configuration. Proxy credentials are redacted and PAC URLs are not reported.
- Schannel discovery is read-only and checks only the exact TLS 1.2/TLS 1.3
  client policy keys. Missing keys are reported as system defaults, not as an
  explicit disablement.
- Explicit .NET TLS 1.3 runs only when both the runtime enum and the documented
  Schannel platform baseline are present (Windows Server 2022/Windows 11 or
  later); otherwise it is `unsupported_by_runtime`, not a failed handshake.

Scope limits:

- No GIS authentication, challenge, controlled channel, X-FN-SID, real
  `codes/check`, marks, or business validation is performed.
- Go TLS success does not mean GIS business operations succeed.
- A PowerShell failure does not by itself mean GIS is unavailable.
- Failure of one CDN does not mean the entire GIS transport pool is unavailable.
- PowerShell `ReceiveFailure` is recorded as a receive-phase failure without
  claiming that Schannel, Windows trust, the provider, or the CDN is broken.
- A default-vs-no-proxy difference can suggest a proxy or Windows automatic HTTP
  routing path, but does not prove that a proxy caused the difference.

Build on a Windows host with Go installed:

```powershell
go test ./...
go vet ./...
$env:GOOS = "windows"
$env:GOARCH = "amd64"
go build -trimpath -ldflags="-s -w" -o publish\GoNetworkProbe.exe .
```
