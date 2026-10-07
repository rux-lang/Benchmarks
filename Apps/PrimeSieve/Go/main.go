// PrimeSieve: finds every prime up to n with the sieve of Eratosthenes over a byte array and prints
// how many there are and their sum.
package main

import (
	"bufio"
	"fmt"
	"os"
	"strconv"
)

func argument(index int, fallback int) int {
	if len(os.Args) <= index {
		return fallback
	}
	value, err := strconv.Atoi(os.Args[index])
	if err != nil {
		panic(err)
	}
	return value
}

func main() {
	limit := argument(1, 100000000)

	composite := make([]byte, limit+1)
	for i := 2; i*i <= limit; i++ {
		if composite[i] != 0 {
			continue
		}
		for j := i * i; j <= limit; j += i {
			composite[j] = 1
		}
	}

	count := 0
	sum := 0
	for k := 2; k <= limit; k++ {
		if composite[k] == 0 {
			count++
			sum += k
		}
	}
	out := bufio.NewWriter(os.Stdout)
	fmt.Fprintf(out, "%d %d\n", count, sum)
	out.Flush()
}
