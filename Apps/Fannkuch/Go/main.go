// Fannkuch: for every permutation of 0..n-1, counts how many prefix reversals ("pancake flips")
// it takes until 0 comes first (single-threaded fannkuch-redux from the Benchmarks Game).
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
	n := argument(1, 10)

	permutation := make([]int, n)
	current := make([]int, n)
	counters := make([]int, n)
	for i := 0; i < n; i++ {
		current[i] = i
	}

	maxFlips := 0
	checksum := 0
	permutationIndex := 0
	r := n
	for {
		for r != 1 {
			counters[r-1] = r
			r--
		}

		copy(permutation, current)
		flips := 0
		first := permutation[0]
		for first != 0 {
			for low, high := 0, first; low < high; low, high = low+1, high-1 {
				permutation[low], permutation[high] = permutation[high], permutation[low]
			}
			flips++
			first = permutation[0]
		}
		if flips > maxFlips {
			maxFlips = flips
		}
		if permutationIndex%2 == 0 {
			checksum += flips
		} else {
			checksum -= flips
		}

		// Next permutation in the order used by the reference program.
		for {
			if r == n {
				out := bufio.NewWriter(os.Stdout)
				fmt.Fprintf(out, "%d\nPfannkuchen(%d) = %d\n", checksum, n, maxFlips)
				out.Flush()
				return
			}
			rotated := current[0]
			for i := 0; i < r; i++ {
				current[i] = current[i+1]
			}
			current[r] = rotated
			counters[r]--
			if counters[r] > 0 {
				break
			}
			r++
		}
		permutationIndex++
	}
}
