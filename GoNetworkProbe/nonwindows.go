//go:build !windows

package main

import "net"

func isAdministrator() bool { return false }
func activeAdapters() []AdapterInfo {
	ifs, _ := net.Interfaces()
	var r []AdapterInfo
	for _, i := range ifs {
		if i.Flags&net.FlagUp != 0 {
			r = append(r, AdapterInfo{Name: i.Name, Description: i.Name, Status: "up", Index: i.Index})
		}
	}
	return r
}
