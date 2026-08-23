package main

import (
	"strings"
	"testing"
)

func TestProxyCredentialsRedaction(t *testing.T) {
	tests := map[string]string{
		"http://user:password@proxy.local:8080":                "http://[redacted]@proxy.local:8080",
		"user:password@proxy.local:8080":                       "[redacted]@proxy.local:8080",
		"http=user:password@proxy.local:8080; https=proxy:443": "http=[redacted]@proxy.local:8080; https=proxy:443",
	}
	for input, want := range tests {
		if got := redactProxyValue(input); got != want {
			t.Fatalf("input=%q got=%q want=%q", input, got, want)
		}
	}
	for _, value := range tests {
		if strings.Contains(value, "password") {
			t.Fatal("test expectation contains credentials")
		}
	}
}
