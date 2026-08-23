//go:build !windows

package main

func collectProxyConfiguration() ProxyConfiguration {
	return ProxyConfiguration{
		WinHTTP: WinHTTPProxyConfiguration{Status: "unsupported", Error: "windows_only"},
		User:    UserProxyConfiguration{Status: "unsupported", Error: "windows_only"},
	}
}
