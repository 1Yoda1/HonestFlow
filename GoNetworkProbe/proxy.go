package main

import "strings"

func redactProxyValue(value string) string {
	parts := strings.Split(value, ";")
	for i, part := range parts {
		parts[i] = redactProxyPart(strings.TrimSpace(part))
	}
	return strings.Join(parts, "; ")
}

func redactProxyPart(part string) string {
	assignment := ""
	endpoint := part
	if equals := strings.Index(endpoint, "="); equals >= 0 {
		assignment, endpoint = endpoint[:equals+1], endpoint[equals+1:]
	}
	at := strings.LastIndex(endpoint, "@")
	if at < 0 {
		return part
	}
	scheme := ""
	if marker := strings.Index(endpoint[:at], "://"); marker >= 0 {
		scheme = endpoint[:marker+3]
	}
	return assignment + scheme + "[redacted]@" + endpoint[at+1:]
}

func proxySummary(configuration ProxyConfiguration) (string, string, string) {
	winHTTP := configuration.WinHTTP.Status
	if configuration.WinHTTP.AccessType != "" {
		winHTTP = configuration.WinHTTP.AccessType
	}
	if configuration.WinHTTP.Proxy != "" {
		winHTTP += " (" + redactProxyValue(configuration.WinHTTP.Proxy) + ")"
	}
	user := configuration.User.Status
	if configuration.User.ProxyEnabled != nil {
		if *configuration.User.ProxyEnabled {
			user = "enabled"
		} else {
			user = "disabled"
		}
	}
	if configuration.User.ProxyServer != "" {
		user += " (" + redactProxyValue(configuration.User.ProxyServer) + ")"
	}
	auto := "unknown"
	if configuration.User.AutoConfigURLPresent != nil || configuration.User.AutoDetect != nil {
		auto = "PAC=" + boolState(configuration.User.AutoConfigURLPresent) + ", WPAD=" + boolState(configuration.User.AutoDetect)
	}
	return winHTTP, user, auto
}

func boolState(value *bool) string {
	if value == nil {
		return "unknown"
	}
	if *value {
		return "enabled"
	}
	return "disabled"
}
