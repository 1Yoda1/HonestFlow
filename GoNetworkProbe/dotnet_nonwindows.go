//go:build !windows

package main

import "context"

func runDotNetDirect(context.Context, string, CdnEndpoint) (TcpResult, []DotNetTLSResult) {
	return TcpResult{Status: "fail", Error: "windows_only"}, []DotNetTLSResult{}
}
