// Base64: encodes pseudo-random bytes as Base64 and decodes them again with a hand-written codec,
// checks the round trip, and prints the encoded length and a hash of the encoded text.
package main

import (
	"bufio"
	"fmt"
	"os"
	"strconv"
)

const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"

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

func encode(input []byte, output []byte) {
	full := len(input) / 3 * 3
	o := 0
	for i := 0; i < full; i += 3 {
		triple := int32(input[i])<<16 | int32(input[i+1])<<8 | int32(input[i+2])
		output[o] = alphabet[(triple>>18)&63]
		output[o+1] = alphabet[(triple>>12)&63]
		output[o+2] = alphabet[(triple>>6)&63]
		output[o+3] = alphabet[triple&63]
		o += 4
	}
	rest := len(input) - full
	if rest == 1 {
		triple := int32(input[full]) << 16
		output[o] = alphabet[(triple>>18)&63]
		output[o+1] = alphabet[(triple>>12)&63]
		output[o+2] = '='
		output[o+3] = '='
	} else if rest == 2 {
		triple := int32(input[full])<<16 | int32(input[full+1])<<8
		output[o] = alphabet[(triple>>18)&63]
		output[o+1] = alphabet[(triple>>12)&63]
		output[o+2] = alphabet[(triple>>6)&63]
		output[o+3] = '='
	}
}

// Returns the number of decoded bytes, or -1 for invalid input.
func decode(input []byte, output []byte, table *[256]int32) int {
	o := 0
	for i := 0; i < len(input); i += 4 {
		a := table[input[i]]
		b := table[input[i+1]]
		if a < 0 || b < 0 {
			return -1
		}
		output[o] = byte((a << 2) | (b >> 4))
		o++
		if input[i+2] == '=' {
			break
		}
		c := table[input[i+2]]
		if c < 0 {
			return -1
		}
		output[o] = byte(((b & 15) << 4) | (c >> 2))
		o++
		if input[i+3] == '=' {
			break
		}
		d := table[input[i+3]]
		if d < 0 {
			return -1
		}
		output[o] = byte(((c & 3) << 6) | d)
		o++
	}
	return o
}

func fail(out *bufio.Writer) {
	fmt.Fprintln(out, "round trip failed")
	out.Flush()
	os.Exit(1)
}

func main() {
	mebibytes := argument(1, 32)
	rounds := argument(2, 4)
	out := bufio.NewWriter(os.Stdout)

	length := mebibytes * 1048576
	data := make([]byte, length)
	state := uint64(4)
	for i := 0; i < length; i += 8 {
		value := splitMix(&state)
		for b := 0; b < 8; b++ {
			data[i+b] = byte(value >> (8 * b))
		}
	}

	var decodeTable [256]int32
	for i := range decodeTable {
		decodeTable[i] = -1
	}
	for i := 0; i < 64; i++ {
		decodeTable[alphabet[i]] = int32(i)
	}

	encodedLength := (length + 2) / 3 * 4
	encoded := make([]byte, encodedLength)
	decoded := make([]byte, length)
	for round := 0; round < rounds; round++ {
		encode(data, encoded)
		if decode(encoded, decoded, &decodeTable) != length {
			fail(out)
		}
		for i := 0; i < length; i++ {
			if decoded[i] != data[i] {
				fail(out)
			}
		}
	}

	hash := uint64(0xCBF29CE484222325)
	for _, b := range encoded {
		hash = (hash ^ uint64(b)) * 0x100000001B3
	}
	fmt.Fprintf(out, "%d %016x\n", encodedLength, hash)
	out.Flush()
}
