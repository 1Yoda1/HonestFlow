//go:build windows

package main

import (
	"errors"
	"testing"
)

func TestProxyCollectionFailureDoesNotCrash(t *testing.T) {
	originalWinHTTP, originalUser := winHTTPProxyReader, userProxyReader
	defer func() { winHTTPProxyReader, userProxyReader = originalWinHTTP, originalUser }()
	winHTTPProxyReader = func() (WinHTTPProxyConfiguration, error) { return WinHTTPProxyConfiguration{}, errors.New("failed") }
	userProxyReader = func() (UserProxyConfiguration, error) { return UserProxyConfiguration{}, errors.New("failed") }
	configuration := collectProxyConfiguration()
	if configuration.WinHTTP.Status != "unavailable" || configuration.User.Status != "unavailable" {
		t.Fatalf("configuration=%+v", configuration)
	}
}
