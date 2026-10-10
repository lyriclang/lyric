// M8b's I/O measurement, 'io_copy': the Go twin of io_copy.lyr — os.Create, io.Copy (which Go
// hands to copy_file_range on Linux), a read back in MiB chunks.
package main

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
)

func must(err error) {
	if err != nil {
		panic(err)
	}
}

func main() {
	dir, err := os.MkdirTemp("", "io_copy")
	must(err)
	original := filepath.Join(dir, "original.bin")
	copied := filepath.Join(dir, "copy.bin")
	chunk := make([]byte, 1048576)
	for i := range chunk {
		chunk[i] = byte((i * 31) % 251)
	}
	f, err := os.Create(original)
	must(err)
	for i := 0; i < 128; i++ {
		_, err = f.Write(chunk)
		must(err)
	}
	must(f.Close())
	from, err := os.Open(original)
	must(err)
	to, err := os.Create(copied)
	must(err)
	n, err := io.Copy(to, from)
	must(err)
	must(from.Close())
	must(to.Close())
	sum := 0
	r, err := os.Open(copied)
	must(err)
	for {
		got, err := r.Read(chunk)
		for k := 0; k < got; k++ {
			sum += int(chunk[k])
		}
		if err == io.EOF {
			break
		}
		must(err)
	}
	must(r.Close())
	must(os.RemoveAll(dir))
	fmt.Printf("%d %d\n", n, sum)
}
