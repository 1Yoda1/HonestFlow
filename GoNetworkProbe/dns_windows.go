//go:build windows

package main

import (
	"context"
	"net"
	"syscall"
	"time"
	"unsafe"
)

type WindowsDNSResolver struct{}

var windowsNativeLookup = getAddrInfoW
var windowsDNSCalls = make(chan struct{}, workers)

func (WindowsDNSResolver) Lookup(ctx context.Context, host string) DnsResult {
	started := time.Now()
	lookupCtx, cancel := context.WithTimeout(ctx, dnsTimeout)
	defer cancel()
	type response struct {
		ips []net.IP
		err error
	}
	completed := make(chan response, 1)
	go func() {
		select {
		case windowsDNSCalls <- struct{}{}:
			defer func() { <-windowsDNSCalls }()
		case <-lookupCtx.Done():
			return
		}
		ips, err := windowsNativeLookup(host)
		select {
		case completed <- response{ips: ips, err: err}:
		case <-lookupCtx.Done():
		}
	}()

	var ips []net.IP
	var err error
	select {
	case result := <-completed:
		ips, err = result.ips, result.err
	case <-lookupCtx.Done():
		err = lookupCtx.Err()
	}
	result := DnsResult{Backend: "GetAddrInfoW", ServersUsed: []string{"not directly observable"}, TransportsUsed: []string{"managed by Windows resolver"}, DurationMS: time.Since(started).Milliseconds()}
	if err != nil {
		result.Status, result.Error = "fail", classifyDNS(err)
		return result
	}
	result.Status = "ok"
	for _, ip := range ips {
		if ip4 := ip.To4(); ip4 != nil {
			result.IPv4 = append(result.IPv4, ip4.String())
		} else if ip16 := ip.To16(); ip16 != nil {
			result.IPv6 = append(result.IPv6, ip16.String())
		}
	}
	result.IPv4 = sortedUnique(result.IPv4)
	result.IPv6 = sortedUnique(result.IPv6)
	return result
}

// getAddrInfoW calls ws2_32!GetAddrInfoW directly. It does not pass through
// net.Resolver and therefore cannot be switched to Go DNS by GODEBUG=netdns=go.
func getAddrInfoW(host string) ([]net.IP, error) {
	name, err := syscall.UTF16PtrFromString(host)
	if err != nil {
		return nil, err
	}
	hints := syscall.AddrinfoW{Family: syscall.AF_UNSPEC, Socktype: syscall.SOCK_STREAM, Protocol: syscall.IPPROTO_IP}
	var head *syscall.AddrinfoW
	if err := syscall.GetAddrInfoW(name, nil, &hints, &head); err != nil {
		return nil, &net.DNSError{Err: err.Error(), Name: host, IsNotFound: err == syscall.Errno(11001), IsTemporary: err == syscall.Errno(11002)}
	}
	defer syscall.FreeAddrInfoW(head)
	var ips []net.IP
	for item := head; item != nil; item = item.Next {
		switch item.Family {
		case syscall.AF_INET:
			raw := (*syscall.RawSockaddrInet4)(unsafe.Pointer(item.Addr))
			ips = append(ips, net.IPv4(raw.Addr[0], raw.Addr[1], raw.Addr[2], raw.Addr[3]))
		case syscall.AF_INET6:
			raw := (*syscall.RawSockaddrInet6)(unsafe.Pointer(item.Addr))
			ip := make(net.IP, net.IPv6len)
			copy(ip, raw.Addr[:])
			ips = append(ips, ip)
		}
	}
	return ips, nil
}
