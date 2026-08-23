//go:build windows

package main

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

const hkeyLocalMachine = uintptr(0x80000002)

const powerShellRuntimeMetadataScript = `$ErrorActionPreference='Stop'
$value=[ordered]@{
  status='ok'
  powerShellVersion=$PSVersionTable.PSVersion.ToString()
  clrVersion=[Environment]::Version.ToString()
  reportedOsVersion=[Environment]::OSVersion.Version.ToString()
  servicePointManagerSecurityProtocol=[System.Net.ServicePointManager]::SecurityProtocol.ToString()
  error=''
}
$json=$value | ConvertTo-Json -Compress
$encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
[Console]::Out.WriteLine(('GNP|TLSMETA|{0}' -f $encoded))`

type rtlOSVersionInfo struct {
	Size             uint32
	MajorVersion     uint32
	MinorVersion     uint32
	BuildNumber      uint32
	PlatformID       uint32
	CSDVersion       [128]uint16
	ServicePackMajor uint16
	ServicePackMinor uint16
	SuiteMask        uint16
	ProductType      byte
	Reserved         byte
}

func collectTLSRuntimeMetadata(ctx context.Context) TLSRuntimeMetadata {
	return TLSRuntimeMetadata{
		PowerShell: collectPowerShellRuntimeMetadata(ctx),
		Windows: WindowsTLSMetadata{
			OSVersion:              windowsVersionString(),
			OSBuild:                windowsBuildNumber(),
			TLS12:                  readSchannelClientCapability("TLS 1.2"),
			TLS13:                  readSchannelClientCapability("TLS 1.3"),
			TLS13PlatformSupported: windowsTLS13PlatformSupported(),
		},
	}
}

func collectPowerShellRuntimeMetadata(ctx context.Context) PowerShellRuntimeMetadata {
	invocation := powerShellInvocation{
		path: windowsPowerShellPath,
		args: []string{"-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encodePowerShellCommand(powerShellRuntimeMetadataScript)},
	}
	execution := executePowerShell(ctx, invocation, 5*time.Second)
	if execution.timedOut {
		return PowerShellRuntimeMetadata{Status: "unavailable", Error: "metadata_timeout"}
	}
	if execution.unavailable {
		return PowerShellRuntimeMetadata{Status: "unavailable", Error: "powershell_unavailable"}
	}
	for _, line := range strings.Split(strings.ReplaceAll(execution.output, "\r", ""), "\n") {
		if !strings.HasPrefix(line, "GNP|TLSMETA|") {
			continue
		}
		decoded, err := base64.StdEncoding.DecodeString(strings.TrimPrefix(line, "GNP|TLSMETA|"))
		if err != nil {
			continue
		}
		var metadata PowerShellRuntimeMetadata
		if json.Unmarshal(decoded, &metadata) == nil && metadata.Status == "ok" {
			metadata.PowerShellVersion = sanitizePowerShellExcerpt(metadata.PowerShellVersion)
			metadata.CLRVersion = sanitizePowerShellExcerpt(metadata.CLRVersion)
			metadata.ReportedOSVersion = sanitizePowerShellExcerpt(metadata.ReportedOSVersion)
			metadata.ServicePointSecurity = sanitizePowerShellExcerpt(metadata.ServicePointSecurity)
			return metadata
		}
	}
	return PowerShellRuntimeMetadata{Status: "unavailable", Error: "metadata_parse_failed"}
}

func readSchannelClientCapability(protocol string) TLSProtocolCapability {
	advapi := syscall.NewLazyDLL("advapi32.dll")
	openKey := advapi.NewProc("RegOpenKeyExW")
	closeKey := advapi.NewProc("RegCloseKey")
	queryValue := advapi.NewProc("RegQueryValueExW")
	path, err := syscall.UTF16PtrFromString(`SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\` + protocol + `\Client`)
	if err != nil {
		return TLSProtocolCapability{State: "unknown", Error: "registry_path_invalid"}
	}
	var key uintptr
	code, _, _ := openKey.Call(hkeyLocalMachine, uintptr(unsafe.Pointer(path)), 0, keyQueryValue, uintptr(unsafe.Pointer(&key)))
	if code == 2 {
		return TLSProtocolCapability{State: "systemDefault"}
	}
	if code != 0 {
		return TLSProtocolCapability{State: "unknown", Error: "registry_read_failed"}
	}
	defer closeKey.Call(key)
	capability := TLSProtocolCapability{State: "systemDefault"}
	if value, found := readRegistryDWORD(queryValue, key, "Enabled"); found {
		enabled := value != 0
		capability.Enabled = &enabled
	}
	if value, found := readRegistryDWORD(queryValue, key, "DisabledByDefault"); found {
		disabled := value != 0
		capability.DisabledByDefault = &disabled
	}
	if (capability.Enabled != nil && !*capability.Enabled) || (capability.DisabledByDefault != nil && *capability.DisabledByDefault) {
		capability.State = "explicitDisabled"
	} else if capability.Enabled != nil && *capability.Enabled && (capability.DisabledByDefault == nil || !*capability.DisabledByDefault) {
		capability.State = "explicitEnabled"
	}
	return capability
}

func windowsVersionString() string {
	info, err := rtlGetVersion()
	if err != nil {
		return ""
	}
	return fmt.Sprintf("%d.%d.%d", info.MajorVersion, info.MinorVersion, info.BuildNumber)
}

func windowsBuildNumber() uint32 {
	info, err := rtlGetVersion()
	if err != nil {
		return 0
	}
	return info.BuildNumber
}

func windowsTLS13PlatformSupported() bool {
	return tls13SupportedOnWindowsBuild(windowsBuildNumber())
}

func tls13SupportedOnWindowsBuild(build uint32) bool {
	// Microsoft documents Schannel TLS 1.3 support beginning with Windows
	// Server 2022 (build 20348) and Windows 11 (build 22000).
	return build >= 20348
}

func rtlGetVersion() (rtlOSVersionInfo, error) {
	var info rtlOSVersionInfo
	info.Size = uint32(unsafe.Sizeof(info))
	proc := syscall.NewLazyDLL("ntdll.dll").NewProc("RtlGetVersion")
	status, _, _ := proc.Call(uintptr(unsafe.Pointer(&info)))
	if status != 0 {
		return rtlOSVersionInfo{}, errors.New("RtlGetVersion failed")
	}
	return info, nil
}
