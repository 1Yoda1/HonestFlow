package main

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"runtime"
	"syscall"
	"time"
)

const probeVersion = "1.3.0"

const overallRunTimeout = 10 * time.Minute

func main() {
	signalCtx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	ctx, cancelRun := context.WithTimeout(signalCtx, overallRunTimeout)
	defer cancelRun()
	started := time.Now()
	fmt.Println("GoNetworkProbe\nESM / GIS MT transport diagnostics")
	fmt.Printf("\nOS: %s\nArchitecture: %s\nAdmin: %s\nVersion: %s\n", runtime.GOOS, runtime.GOARCH, yesNo(isAdministrator()), probeVersion)
	fmt.Println("\nESM discovery...")
	discovery := discoverESM()
	printDiscovery(discovery)
	fmt.Println("\nNetwork adapters...")
	adapters := activeAdapters()
	printAdapters(adapters)
	fmt.Println("\nWindows proxy configuration...")
	proxy := collectProxyConfiguration()
	printProxyConfiguration(proxy)
	fmt.Println("\nWindows TLS runtime metadata...")
	tlsRuntime := collectTLSRuntimeMetadata(ctx)
	printTLSRuntimeMetadata(tlsRuntime)
	run := ProbeRun{SchemaVersion: "3", ProbeVersion: probeVersion, StartedAt: started, OS: OSInfo{OS: runtime.GOOS, Architecture: runtime.GOARCH, Admin: isAdministrator()}, Proxy: proxy, TLSRuntime: tlsRuntime, ESM: discovery, Adapters: adapters}
	if len(discovery.Endpoints) == 0 {
		fmt.Println("\nESM data not found or no allowed CDN endpoints in cache. Limited mode.")
	} else {
		fmt.Println("\nTesting...")
		run.CDNResults = probeAll(ctx, discovery)
	}
	run.Cancelled = ctx.Err() != nil
	run.CompletedAt = time.Now()
	analyzeRun(&run, run.CompletedAt)
	printResults(run.CDNResults)
	if run.Cancelled {
		if signalCtx.Err() != nil {
			fmt.Println("\nCancelled by user")
		} else {
			fmt.Println("\nOverall probe timeout reached")
		}
	}
	printSummary(run)
	txt, json, err := writeReports(run)
	if err != nil {
		fmt.Fprintln(os.Stderr, "Report error:", safeError(err))
		return
	}
	fmt.Printf("\nReports:\n%s\n%s\n", txt, json)
}

func yesNo(v bool) string {
	if v {
		return "yes"
	}
	return "no"
}
