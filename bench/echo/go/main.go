// M8b's I/O measurement, 'echo': the Go twin of echo.lyr — a listener, a goroutine that echoes,
// one that writes 256 MiB in 64 KiB writes, the main goroutine reading them back.
package main

import (
	"fmt"
	"io"
	"net"
)

func must(err error) {
	if err != nil {
		panic(err)
	}
}

func main() {
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	must(err)
	done := make(chan struct{})
	go func() {
		conn, err := listener.Accept()
		must(err)
		buf := make([]byte, 65536)
		for {
			n, err := conn.Read(buf)
			if n > 0 {
				_, werr := conn.Write(buf[:n])
				must(werr)
			}
			if err == io.EOF {
				break
			}
			must(err)
		}
		must(conn.Close())
		close(done)
	}()
	client, err := net.Dial("tcp", listener.Addr().String())
	must(err)
	total := 268435456
	go func() {
		chunk := make([]byte, 65536)
		for i := range chunk {
			chunk[i] = byte((i * 31) % 251)
		}
		for sent := 0; sent < total; sent += len(chunk) {
			_, err := client.Write(chunk)
			must(err)
		}
		must(client.(*net.TCPConn).CloseWrite())
	}()
	buf := make([]byte, 65536)
	got, sum := 0, 0
	for {
		n, err := client.Read(buf)
		for k := 0; k < n; k++ {
			sum += int(buf[k])
		}
		got += n
		if err == io.EOF {
			break
		}
		must(err)
	}
	<-done
	must(client.Close())
	must(listener.Close())
	fmt.Printf("%d %d\n", got, sum)
}
