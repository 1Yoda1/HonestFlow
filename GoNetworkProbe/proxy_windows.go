//go:build windows

package main

import (
	"encoding/binary"
	"errors"
	"strings"
	"syscall"
	"unsafe"
)

const (
	hkeyCurrentUser       = uintptr(0x80000001)
	keyQueryValue         = uintptr(0x0001)
	regSZ                 = uint32(1)
	regExpandSZ           = uint32(2)
	regDWORD              = uint32(4)
	winHTTPDefaultProxy   = uint32(0)
	winHTTPNoProxy        = uint32(1)
	winHTTPNamedProxy     = uint32(3)
	winHTTPAutomaticProxy = uint32(4)
)

type winHTTPProxyInfo struct {
	AccessType  uint32
	Proxy       *uint16
	ProxyBypass *uint16
}

var winHTTPProxyReader = readWinHTTPProxyConfiguration
var userProxyReader = readUserProxyConfiguration

func collectProxyConfiguration() ProxyConfiguration {
	configuration := ProxyConfiguration{}
	winHTTP, err := winHTTPProxyReader()
	if err != nil {
		configuration.WinHTTP = WinHTTPProxyConfiguration{Status: "unavailable", Error: "proxy_collection_failed"}
	} else {
		configuration.WinHTTP = winHTTP
	}
	user, err := userProxyReader()
	if err != nil {
		configuration.User = UserProxyConfiguration{Status: "unavailable", Error: "proxy_collection_failed"}
	} else {
		configuration.User = user
	}
	return configuration
}

func readWinHTTPProxyConfiguration() (WinHTTPProxyConfiguration, error) {
	proc := syscall.NewLazyDLL("winhttp.dll").NewProc("WinHttpGetDefaultProxyConfiguration")
	var info winHTTPProxyInfo
	ok, _, _ := proc.Call(uintptr(unsafe.Pointer(&info)))
	if ok == 0 {
		return WinHTTPProxyConfiguration{}, errors.New("WinHTTP proxy query failed")
	}
	globalFree := syscall.NewLazyDLL("kernel32.dll").NewProc("GlobalFree")
	if info.Proxy != nil {
		defer globalFree.Call(uintptr(unsafe.Pointer(info.Proxy)))
	}
	if info.ProxyBypass != nil {
		defer globalFree.Call(uintptr(unsafe.Pointer(info.ProxyBypass)))
	}
	result := WinHTTPProxyConfiguration{Status: "ok", AccessType: winHTTPAccessType(info.AccessType)}
	if info.Proxy != nil {
		result.Proxy = redactProxyValue(utf16PointerString(info.Proxy))
	}
	return result, nil
}

func winHTTPAccessType(value uint32) string {
	switch value {
	case winHTTPDefaultProxy:
		return "default_proxy"
	case winHTTPNoProxy:
		return "direct"
	case winHTTPNamedProxy:
		return "named_proxy"
	case winHTTPAutomaticProxy:
		return "automatic_proxy"
	default:
		return "unknown"
	}
}

func readUserProxyConfiguration() (UserProxyConfiguration, error) {
	advapi := syscall.NewLazyDLL("advapi32.dll")
	openKey := advapi.NewProc("RegOpenKeyExW")
	closeKey := advapi.NewProc("RegCloseKey")
	queryValue := advapi.NewProc("RegQueryValueExW")
	path, err := syscall.UTF16PtrFromString(`Software\Microsoft\Windows\CurrentVersion\Internet Settings`)
	if err != nil {
		return UserProxyConfiguration{}, err
	}
	var key uintptr
	code, _, _ := openKey.Call(hkeyCurrentUser, uintptr(unsafe.Pointer(path)), 0, keyQueryValue, uintptr(unsafe.Pointer(&key)))
	if code != 0 {
		return UserProxyConfiguration{}, errors.New("Internet Settings key unavailable")
	}
	defer closeKey.Call(key)
	result := UserProxyConfiguration{Status: "ok"}
	if value, found := readRegistryDWORD(queryValue, key, "ProxyEnable"); found {
		enabled := value != 0
		result.ProxyEnabled = &enabled
	}
	if value, found := readRegistryString(queryValue, key, "ProxyServer"); found {
		result.ProxyServer = redactProxyValue(value)
	}
	if value, found := readRegistryString(queryValue, key, "AutoConfigURL"); found {
		present := strings.TrimSpace(value) != ""
		result.AutoConfigURLPresent = &present
	} else {
		present := false
		result.AutoConfigURLPresent = &present
	}
	if value, found := readRegistryDWORD(queryValue, key, "AutoDetect"); found {
		enabled := value != 0
		result.AutoDetect = &enabled
	}
	return result, nil
}

func readRegistryDWORD(query *syscall.LazyProc, key uintptr, name string) (uint32, bool) {
	dataType, data, ok := readRegistryValue(query, key, name)
	if !ok || dataType != regDWORD || len(data) < 4 {
		return 0, false
	}
	return binary.LittleEndian.Uint32(data[:4]), true
}

func readRegistryString(query *syscall.LazyProc, key uintptr, name string) (string, bool) {
	dataType, data, ok := readRegistryValue(query, key, name)
	if !ok || (dataType != regSZ && dataType != regExpandSZ) || len(data)%2 != 0 {
		return "", false
	}
	units := make([]uint16, len(data)/2)
	for i := range units {
		units[i] = binary.LittleEndian.Uint16(data[i*2:])
	}
	return syscall.UTF16ToString(units), true
}

func readRegistryValue(query *syscall.LazyProc, key uintptr, name string) (uint32, []byte, bool) {
	valueName, err := syscall.UTF16PtrFromString(name)
	if err != nil {
		return 0, nil, false
	}
	var dataType, size uint32
	code, _, _ := query.Call(key, uintptr(unsafe.Pointer(valueName)), 0, uintptr(unsafe.Pointer(&dataType)), 0, uintptr(unsafe.Pointer(&size)))
	if code != 0 || size == 0 || size > 1<<20 {
		return 0, nil, false
	}
	data := make([]byte, size)
	code, _, _ = query.Call(key, uintptr(unsafe.Pointer(valueName)), 0, uintptr(unsafe.Pointer(&dataType)), uintptr(unsafe.Pointer(&data[0])), uintptr(unsafe.Pointer(&size)))
	if code != 0 {
		return 0, nil, false
	}
	return dataType, data[:size], true
}
