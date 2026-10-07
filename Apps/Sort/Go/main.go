// Sort: sorts pseudo-random 32-bit integers with a hand-written quicksort, checks the order and
// prints a hash of the sorted array.
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

func splitMix(state *uint64) uint64 {
	*state += 0x9E3779B97F4A7C15
	z := *state
	z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
	z = (z ^ (z >> 27)) * 0x94D049BB133111EB
	return z ^ (z >> 31)
}

// Sorts values[low..=high]: median-of-three quicksort that recurses into the smaller part and
// leaves ranges of fewer than 16 elements to insertion sort.
func quickSort(values []int32, low int, high int) {
	for high-low >= 16 {
		middle := low + (high-low)/2
		if values[middle] < values[low] {
			values[low], values[middle] = values[middle], values[low]
		}
		if values[high] < values[low] {
			values[low], values[high] = values[high], values[low]
		}
		if values[high] < values[middle] {
			values[middle], values[high] = values[high], values[middle]
		}
		pivot := values[middle]
		i, j := low, high
		for i <= j {
			for values[i] < pivot {
				i++
			}
			for values[j] > pivot {
				j--
			}
			if i <= j {
				values[i], values[j] = values[j], values[i]
				i++
				j--
			}
		}
		if j-low < high-i {
			quickSort(values, low, j)
			low = i
		} else {
			quickSort(values, i, high)
			high = j
		}
	}
	for i := low + 1; i <= high; i++ {
		value := values[i]
		j := i - 1
		for j >= low && values[j] > value {
			values[j+1] = values[j]
			j--
		}
		values[j+1] = value
	}
}

func main() {
	count := argument(1, 10000000)

	state := uint64(3)
	values := make([]int32, count)
	for i := 0; i < count; i++ {
		values[i] = int32(uint32(splitMix(&state) >> 32))
	}

	quickSort(values, 0, count-1)

	out := bufio.NewWriter(os.Stdout)
	hash := uint64(0xCBF29CE484222325)
	for i := 0; i < count; i++ {
		if i > 0 && values[i-1] > values[i] {
			fmt.Fprintln(out, "unsorted")
			out.Flush()
			os.Exit(1)
		}
		hash = (hash ^ uint64(uint32(values[i]))) * 0x100000001B3
	}
	fmt.Fprintf(out, "%d %016x\n", count, hash)
	out.Flush()
}
