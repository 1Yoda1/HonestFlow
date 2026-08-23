//go:build windows

package main

import (
	"context"
	"net"
	"testing"
)

func TestWindowsDNSResolverUsesNativeGetAddrInfoPath(t *testing.T) {
	original := windowsNativeLookup
	defer func() { windowsNativeLookup = original }()
	called := false
	windowsNativeLookup = func(host string) ([]net.IP, error) {
		called = true
		if host != "cdn01.crpt.ru" {
			t.Fatalf("host=%q", host)
		}
		return []net.IP{net.ParseIP("192.0.2.10")}, nil
	}
	result := (WindowsDNSResolver{}).Lookup(context.Background(), "cdn01.crpt.ru")
	if !called || result.Backend != "GetAddrInfoW" || result.Status != "ok" || len(result.IPv4) != 1 || result.IPv4[0] != "192.0.2.10" {
		t.Fatalf("called=%v result=%+v", called, result)
	}
}
