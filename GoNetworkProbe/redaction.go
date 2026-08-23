package main

import (
	"regexp"
	"strings"
)

var windowsUserPathPattern = regexp.MustCompile(`(?i)[a-z]:\\users\\[^\\\s]+(?:\\[^\s]*)?`)
var pathPattern = regexp.MustCompile(`(?i)[a-z]:\\[^\s]+`)
var authorizationPattern = regexp.MustCompile(`(?i)authorization\s*:\s*\S+(?:\s+\S+)?`)
var jwtPattern = regexp.MustCompile(`(?i)\beyJ[a-z0-9_-]{5,}\.[a-z0-9_-]{5,}(?:\.[a-z0-9_-]{5,})?\b`)
var secretPattern = regexp.MustCompile(`(?i)(bearer|token|password|secret|fn(?:serial)?|inn|cis|mark(?:code)?)\s*[:=]\s*\S+`)
var pemPattern = regexp.MustCompile(`(?is)-----BEGIN [^-]+-----.*?-----END [^-]+-----`)

func sanitizePowerShellExcerpt(value string) string {
	value = pemPattern.ReplaceAllString(value, "[pem-redacted]")
	value = windowsUserPathPattern.ReplaceAllString(value, "[user-path]")
	value = pathPattern.ReplaceAllString(value, "[path]")
	value = authorizationPattern.ReplaceAllString(value, "Authorization: [redacted]")
	value = jwtPattern.ReplaceAllString(value, "[jwt-redacted]")
	value = secretPattern.ReplaceAllString(value, "$1=[redacted]")
	value = strings.Join(strings.Fields(value), " ")
	if len(value) > 240 {
		value = value[:240] + "..."
	}
	return value
}

func sanitizeRunForReport(run ProbeRun) ProbeRun {
	copyRun := run
	copyRun.Proxy.WinHTTP.Proxy = redactProxyValue(copyRun.Proxy.WinHTTP.Proxy)
	copyRun.Proxy.User.ProxyServer = redactProxyValue(copyRun.Proxy.User.ProxyServer)
	copyRun.Summary.ProxyWinHTTP = redactProxyValue(copyRun.Summary.ProxyWinHTTP)
	copyRun.Summary.ProxyUser = redactProxyValue(copyRun.Summary.ProxyUser)
	copyRun.TLSRuntime.PowerShell.PowerShellVersion = sanitizePowerShellExcerpt(copyRun.TLSRuntime.PowerShell.PowerShellVersion)
	copyRun.TLSRuntime.PowerShell.CLRVersion = sanitizePowerShellExcerpt(copyRun.TLSRuntime.PowerShell.CLRVersion)
	copyRun.TLSRuntime.PowerShell.ReportedOSVersion = sanitizePowerShellExcerpt(copyRun.TLSRuntime.PowerShell.ReportedOSVersion)
	copyRun.TLSRuntime.PowerShell.ServicePointSecurity = sanitizePowerShellExcerpt(copyRun.TLSRuntime.PowerShell.ServicePointSecurity)
	copyRun.CDNResults = append([]CdnResult(nil), run.CDNResults...)
	for i := range copyRun.CDNResults {
		copyRun.CDNResults[i].PowerShellDefault.ErrorExcerpt = sanitizePowerShellExcerpt(copyRun.CDNResults[i].PowerShellDefault.ErrorExcerpt)
		copyRun.CDNResults[i].PowerShellNoProxy.ErrorExcerpt = sanitizePowerShellExcerpt(copyRun.CDNResults[i].PowerShellNoProxy.ErrorExcerpt)
		copyRun.CDNResults[i].DotNetTLS = append([]DotNetTLSResult(nil), run.CDNResults[i].DotNetTLS...)
		for j := range copyRun.CDNResults[i].DotNetTLS {
			original := run.CDNResults[i].DotNetTLS[j].Exception
			if original == nil {
				continue
			}
			exception := *original
			exception.InnerExceptionTypes = append([]string(nil), original.InnerExceptionTypes...)
			exception.ExceptionType = sanitizePowerShellExcerpt(exception.ExceptionType)
			for k := range exception.InnerExceptionTypes {
				exception.InnerExceptionTypes[k] = sanitizePowerShellExcerpt(exception.InnerExceptionTypes[k])
			}
			exception.SafeMessage = sanitizePowerShellExcerpt(exception.SafeMessage)
			exception.SocketErrorCode = sanitizePowerShellExcerpt(exception.SocketErrorCode)
			exception.Category = sanitizePowerShellExcerpt(exception.Category)
			copyRun.CDNResults[i].DotNetTLS[j].Exception = &exception
		}
	}
	copyRun.PowerShellComparison = append([]PowerShellComparison(nil), run.PowerShellComparison...)
	for i := range copyRun.PowerShellComparison {
		copyRun.PowerShellComparison[i].Default.ErrorExcerpt = sanitizePowerShellExcerpt(copyRun.PowerShellComparison[i].Default.ErrorExcerpt)
		copyRun.PowerShellComparison[i].NoProxy.ErrorExcerpt = sanitizePowerShellExcerpt(copyRun.PowerShellComparison[i].NoProxy.ErrorExcerpt)
	}
	copyRun.Findings = append([]Finding(nil), run.Findings...)
	for i := range copyRun.Findings {
		if run.Findings[i].Evidence == nil {
			continue
		}
		evidence := make(map[string]string, len(run.Findings[i].Evidence))
		for key, value := range run.Findings[i].Evidence {
			evidence[key] = sanitizePowerShellExcerpt(value)
		}
		copyRun.Findings[i].Evidence = evidence
	}
	return copyRun
}
