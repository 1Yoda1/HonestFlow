//go:build !windows

package main

import "context"

func collectTLSRuntimeMetadata(context.Context) TLSRuntimeMetadata {
	return TLSRuntimeMetadata{
		PowerShell: PowerShellRuntimeMetadata{Status: "unsupported", Error: "windows_only"},
		Windows: WindowsTLSMetadata{
			TLS12: TLSProtocolCapability{State: "unknown", Error: "windows_only"},
			TLS13: TLSProtocolCapability{State: "unknown", Error: "windows_only"},
		},
	}
}
