//go:build !windows

package main

import "context"

type WindowsDNSResolver struct{}

func (WindowsDNSResolver) Lookup(context.Context, string) DnsResult {
	return DnsResult{Backend: "unavailable", ServersUsed: []string{}, TransportsUsed: []string{}, Status: "fail", Error: "windows_dns_unavailable"}
}
