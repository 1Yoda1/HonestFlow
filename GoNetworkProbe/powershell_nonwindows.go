//go:build !windows

package main

import "context"

func runPowerShellHTTPS(context.Context, string) PowerShellResult {
	return PowerShellResult{ExitCode: -1, Category: "powershell_other", ErrorExcerpt: "Windows PowerShell unavailable"}
}

func runPowerShellHTTPSDefault(ctx context.Context, host string) PowerShellResult {
	return runPowerShellHTTPS(ctx, host)
}

func runPowerShellHTTPSNoProxy(ctx context.Context, host string) PowerShellResult {
	return runPowerShellHTTPS(ctx, host)
}
