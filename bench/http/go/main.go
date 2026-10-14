// Measurement point 4, 'http': the Go twin of http.lyr — net/http's server and client in one
// process, sixty-four goroutines, five hundred requests each over keep-alive connections.
package main

import (
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"sync"
)

type Item struct {
	ID    int      `json:"id"`
	Name  string   `json:"name"`
	Price float64  `json:"price"`
	Tags  []string `json:"tags"`
}

func main() {
	items := make([]Item, 18)
	for i := range items {
		items[i] = Item{ID: i, Name: fmt.Sprintf("item%d", i), Price: float64(i) + 0.25, Tags: []string{"new", "sale"}}
	}
	listener, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		panic(err)
	}
	server := &http.Server{Handler: http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := json.Marshal(items)
		w.Header().Set("Content-Type", "application/json")
		w.Write(body)
	})}
	go server.Serve(listener)
	target := fmt.Sprintf("http://%s/items", listener.Addr())
	client := &http.Client{Transport: &http.Transport{MaxIdleConnsPerHost: 64}}
	var mu sync.Mutex
	var wg sync.WaitGroup
	total := 0
	for c := 0; c < 64; c++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			sum := 0
			for i := 0; i < 500; i++ {
				response, err := client.Get(target)
				if err != nil {
					panic(err)
				}
				body, _ := io.ReadAll(response.Body)
				response.Body.Close()
				sum += len(body)
			}
			mu.Lock()
			total += sum
			mu.Unlock()
		}()
	}
	wg.Wait()
	server.Close()
	fmt.Println(total)
}
