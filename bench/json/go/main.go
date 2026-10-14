// Measurement point 4, 'json': the Go twin of json.lyr — encoding/json over the same six
// thousand records, twenty rounds.
package main

import (
	"encoding/json"
	"fmt"
)

type Address struct {
	Street string `json:"street"`
	City   string `json:"city"`
	Zip    int    `json:"zip"`
}

type Record struct {
	ID      int      `json:"id"`
	Name    string   `json:"name"`
	Score   float64  `json:"score"`
	Active  bool     `json:"active"`
	Tags    []string `json:"tags"`
	Address Address  `json:"address"`
}

func main() {
	records := make([]Record, 0, 6000)
	var x uint64 = 88172645463325252
	for i := 0; i < 6000; i++ {
		x ^= x << 13
		x ^= x >> 7
		x ^= x << 17
		records = append(records, Record{
			ID:      i,
			Name:    fmt.Sprintf("user%d", x%100000),
			Score:   float64(x%10000) / 100.0,
			Active:  x%2 == 0,
			Tags:    []string{fmt.Sprintf("t%d", x%7), fmt.Sprintf("t%d", x%11), fmt.Sprintf("t%d", x%13)},
			Address: Address{Street: fmt.Sprintf("%d Main St", x%999), City: fmt.Sprintf("city%d", x%50), Zip: int(x%90000) + 10000},
		})
	}
	count, ids, zips := 0, 0, 0
	for round := 0; round < 20; round++ {
		text, err := json.Marshal(records)
		if err != nil {
			panic(err)
		}
		var back []Record
		if err := json.Unmarshal(text, &back); err != nil {
			panic(err)
		}
		count += len(back)
		for _, r := range back {
			ids += r.ID
			zips += r.Address.Zip
		}
	}
	fmt.Printf("%d %d %d\n", count, ids, zips)
}
