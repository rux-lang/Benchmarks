// MatrixMultiply: multiplies two generated n×n matrices of doubles with the cache-friendly i-k-j
// loop order and prints a hash of the result's bit patterns.
package main

import (
	"bufio"
	"fmt"
	"math"
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
	n := argument(1, 1024)

	a := make([]float64, n*n)
	b := make([]float64, n*n)
	c := make([]float64, n*n)
	for i := 0; i < n; i++ {
		for j := 0; j < n; j++ {
			a[i*n+j] = float64((i+3*j)%17-8) / 8.0
			b[i*n+j] = float64((2*i+j)%13-6) / 6.0
		}
	}

	for i := 0; i < n; i++ {
		for k := 0; k < n; k++ {
			aik := a[i*n+k]
			for j := 0; j < n; j++ {
				c[i*n+j] += aik * b[k*n+j]
			}
		}
	}

	hash := uint64(0xCBF29CE484222325)
	for _, value := range c {
		hash = (hash ^ math.Float64bits(value)) * 0x100000001B3
	}
	out := bufio.NewWriter(os.Stdout)
	fmt.Fprintf(out, "%d %016x\n", n, hash)
	out.Flush()
}
