//go:build windows

package main

import (
	"net"
	"sort"
	"syscall"
	"unsafe"
)

const (
	winBuiltinAdministratorsSID = 26
	gaaIncludeGateways          = 0x0080
	gaaSkipAnycast              = 0x0002
	gaaSkipMulticast            = 0x0004
	ifTypeSoftwareLoopback      = 24
	ifOperStatusUp              = 1
	errorBufferOverflow         = 111
)

type socketAddress struct {
	Sockaddr       *syscall.RawSockaddrAny
	SockaddrLength int32
}
type ipAdapterUnicastAddress struct {
	Length             uint32
	Flags              uint32
	Next               *ipAdapterUnicastAddress
	Address            socketAddress
	PrefixOrigin       int32
	SuffixOrigin       int32
	DadState           int32
	ValidLifetime      uint32
	PreferredLifetime  uint32
	LeaseLifetime      uint32
	OnLinkPrefixLength uint8
}
type ipAdapterDNSServerAddress struct {
	Length   uint32
	Reserved uint32
	Next     *ipAdapterDNSServerAddress
	Address  socketAddress
}
type ipAdapterGatewayAddress struct {
	Length   uint32
	Reserved uint32
	Next     *ipAdapterGatewayAddress
	Address  socketAddress
}
type ipAdapterAddresses struct {
	Length                 uint32
	IfIndex                uint32
	Next                   *ipAdapterAddresses
	AdapterName            *byte
	FirstUnicastAddress    *ipAdapterUnicastAddress
	FirstAnycastAddress    uintptr
	FirstMulticastAddress  uintptr
	FirstDNSServerAddress  *ipAdapterDNSServerAddress
	DNSSuffix              *uint16
	Description            *uint16
	FriendlyName           *uint16
	PhysicalAddress        [syscall.MAX_ADAPTER_ADDRESS_LENGTH]byte
	PhysicalAddressLength  uint32
	Flags                  uint32
	MTU                    uint32
	IfType                 uint32
	OperStatus             uint32
	IPv6IfIndex            uint32
	ZoneIndices            [16]uint32
	FirstPrefix            uintptr
	TransmitLinkSpeed      uint64
	ReceiveLinkSpeed       uint64
	FirstWINSServerAddress uintptr
	FirstGatewayAddress    *ipAdapterGatewayAddress
}

func isAdministrator() bool {
	advapi := syscall.NewLazyDLL("advapi32.dll")
	create := advapi.NewProc("CreateWellKnownSid")
	check := advapi.NewProc("CheckTokenMembership")
	sid := make([]byte, 68)
	size := uint32(len(sid))
	result, _, _ := create.Call(winBuiltinAdministratorsSID, 0, uintptr(unsafe.Pointer(&sid[0])), uintptr(unsafe.Pointer(&size)))
	if result == 0 {
		return false
	}
	var member int32
	result, _, _ = check.Call(0, uintptr(unsafe.Pointer(&sid[0])), uintptr(unsafe.Pointer(&member)))
	return result != 0 && member != 0
}

// activeAdapters calls GetAdaptersAddresses directly. It reads interface,
// gateway and DNS configuration without invoking PowerShell or collecting MACs.
func activeAdapters() []AdapterInfo {
	iphlpapi := syscall.NewLazyDLL("iphlpapi.dll")
	getAdapters := iphlpapi.NewProc("GetAdaptersAddresses")
	size := uint32(15 * 1024)
	var buffer []byte
	var head *ipAdapterAddresses
	for attempt := 0; attempt < 3; attempt++ {
		buffer = make([]byte, size)
		head = (*ipAdapterAddresses)(unsafe.Pointer(&buffer[0]))
		code, _, _ := getAdapters.Call(syscall.AF_UNSPEC, gaaIncludeGateways|gaaSkipAnycast|gaaSkipMulticast, 0, uintptr(unsafe.Pointer(head)), uintptr(unsafe.Pointer(&size)))
		if code == 0 {
			break
		}
		if code != errorBufferOverflow {
			return nil
		}
		head = nil
	}
	if head == nil {
		return nil
	}
	var adapters []AdapterInfo
	for current := head; current != nil; current = current.Next {
		if current.OperStatus != ifOperStatusUp || current.IfType == ifTypeSoftwareLoopback {
			continue
		}
		index := current.IfIndex
		if index == 0 {
			index = current.IPv6IfIndex
		}
		adapter := AdapterInfo{Name: utf16PointerString(current.FriendlyName), Description: utf16PointerString(current.Description), Status: "up", Index: int(index)}
		if adapter.Name == "" {
			adapter.Name = adapter.Description
		}
		for item := current.FirstUnicastAddress; item != nil; item = item.Next {
			if ip := socketAddressIP(item.Address); ip != nil {
				if ip.To4() != nil {
					adapter.IPv4 = append(adapter.IPv4, ip.String())
				} else {
					adapter.IPv6 = append(adapter.IPv6, ip.String())
				}
			}
		}
		for item := current.FirstGatewayAddress; item != nil; item = item.Next {
			if ip := socketAddressIP(item.Address); ip != nil && !ip.IsUnspecified() {
				adapter.Gateways = append(adapter.Gateways, ip.String())
			}
		}
		for item := current.FirstDNSServerAddress; item != nil; item = item.Next {
			if ip := socketAddressIP(item.Address); ip != nil && !ip.IsUnspecified() {
				adapter.DNSServers = append(adapter.DNSServers, ip.String())
			}
		}
		adapter.IPv4 = sortedUnique(adapter.IPv4)
		adapter.IPv6 = sortedUnique(adapter.IPv6)
		adapter.Gateways = sortedUnique(adapter.Gateways)
		adapter.DNSServers = sortedUnique(adapter.DNSServers)
		adapters = append(adapters, adapter)
	}
	sort.Slice(adapters, func(i, j int) bool { return adapters[i].Index < adapters[j].Index })
	return adapters
}

func socketAddressIP(address socketAddress) net.IP {
	if address.Sockaddr == nil {
		return nil
	}
	switch address.Sockaddr.Addr.Family {
	case syscall.AF_INET:
		raw := (*syscall.RawSockaddrInet4)(unsafe.Pointer(address.Sockaddr))
		return net.IPv4(raw.Addr[0], raw.Addr[1], raw.Addr[2], raw.Addr[3])
	case syscall.AF_INET6:
		raw := (*syscall.RawSockaddrInet6)(unsafe.Pointer(address.Sockaddr))
		ip := make(net.IP, net.IPv6len)
		copy(ip, raw.Addr[:])
		return ip
	default:
		return nil
	}
}

func utf16PointerString(value *uint16) string {
	if value == nil {
		return ""
	}
	chars := unsafe.Slice(value, 32768)
	for i, char := range chars {
		if char == 0 {
			return syscall.UTF16ToString(chars[:i])
		}
	}
	return ""
}
