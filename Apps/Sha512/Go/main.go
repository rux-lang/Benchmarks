// Sha512: fills a buffer with pseudo-random bytes and hashes it with a hand-written SHA-512.
// Each further round writes the previous digest into the first 64 bytes and hashes again.
package main

import (
	"bufio"
	"fmt"
	"os"
	"strconv"
)

var k = [80]uint64{
	0x428A2F98D728AE22, 0x7137449123EF65CD, 0xB5C0FBCFEC4D3B2F, 0xE9B5DBA58189DBBC,
	0x3956C25BF348B538, 0x59F111F1B605D019, 0x923F82A4AF194F9B, 0xAB1C5ED5DA6D8118,
	0xD807AA98A3030242, 0x12835B0145706FBE, 0x243185BE4EE4B28C, 0x550C7DC3D5FFB4E2,
	0x72BE5D74F27B896F, 0x80DEB1FE3B1696B1, 0x9BDC06A725C71235, 0xC19BF174CF692694,
	0xE49B69C19EF14AD2, 0xEFBE4786384F25E3, 0x0FC19DC68B8CD5B5, 0x240CA1CC77AC9C65,
	0x2DE92C6F592B0275, 0x4A7484AA6EA6E483, 0x5CB0A9DCBD41FBD4, 0x76F988DA831153B5,
	0x983E5152EE66DFAB, 0xA831C66D2DB43210, 0xB00327C898FB213F, 0xBF597FC7BEEF0EE4,
	0xC6E00BF33DA88FC2, 0xD5A79147930AA725, 0x06CA6351E003826F, 0x142929670A0E6E70,
	0x27B70A8546D22FFC, 0x2E1B21385C26C926, 0x4D2C6DFC5AC42AED, 0x53380D139D95B3DF,
	0x650A73548BAF63DE, 0x766A0ABB3C77B2A8, 0x81C2C92E47EDAEE6, 0x92722C851482353B,
	0xA2BFE8A14CF10364, 0xA81A664BBC423001, 0xC24B8B70D0F89791, 0xC76C51A30654BE30,
	0xD192E819D6EF5218, 0xD69906245565A910, 0xF40E35855771202A, 0x106AA07032BBD1B8,
	0x19A4C116B8D2D0C8, 0x1E376C085141AB53, 0x2748774CDF8EEB99, 0x34B0BCB5E19B48A8,
	0x391C0CB3C5C95A63, 0x4ED8AA4AE3418ACB, 0x5B9CCA4F7763E373, 0x682E6FF3D6B2B8A3,
	0x748F82EE5DEFB2FC, 0x78A5636F43172F60, 0x84C87814A1F0AB72, 0x8CC702081A6439EC,
	0x90BEFFFA23631E28, 0xA4506CEBDE82BDE9, 0xBEF9A3F7B2C67915, 0xC67178F2E372532B,
	0xCA273ECEEA26619C, 0xD186B8C721C0C207, 0xEADA7DD6CDE0EB1E, 0xF57D4F7FEE6ED178,
	0x06F067AA72176FBA, 0x0A637DC5A2C898A6, 0x113F9804BEF90DAE, 0x1B710B35131C471B,
	0x28DB77F523047D84, 0x32CAAB7B40C72493, 0x3C9EBE0A15C9BEBC, 0x431D67C49C100D4C,
	0x4CC5D4BECB3E42B6, 0x597F299CFC657E2A, 0x5FCB6FAB3AD6FAEC, 0x6C44198C4A475817,
}

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

func rotateRight(x uint64, n uint) uint64 {
	return (x >> n) | (x << (64 - n))
}

func loadBigEndian(data []byte, offset int) uint64 {
	var value uint64
	for b := 0; b < 8; b++ {
		value = (value << 8) | uint64(data[offset+b])
	}
	return value
}

func compress(h *[8]uint64, w *[80]uint64, data []byte, offset int) {
	for t := 0; t < 16; t++ {
		w[t] = loadBigEndian(data, offset+8*t)
	}
	for t := 16; t < 80; t++ {
		s0 := rotateRight(w[t-15], 1) ^ rotateRight(w[t-15], 8) ^ (w[t-15] >> 7)
		s1 := rotateRight(w[t-2], 19) ^ rotateRight(w[t-2], 61) ^ (w[t-2] >> 6)
		w[t] = w[t-16] + s0 + w[t-7] + s1
	}
	a, b, c, d, e, f, g, hh := h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7]
	for t := 0; t < 80; t++ {
		s1 := rotateRight(e, 14) ^ rotateRight(e, 18) ^ rotateRight(e, 41)
		ch := (e & f) ^ (^e & g)
		t1 := hh + s1 + ch + k[t] + w[t]
		s0 := rotateRight(a, 28) ^ rotateRight(a, 34) ^ rotateRight(a, 39)
		maj := (a & b) ^ (a & c) ^ (b & c)
		t2 := s0 + maj
		hh = g
		g = f
		f = e
		e = d + t1
		d = c
		c = b
		b = a
		a = t1 + t2
	}
	h[0] += a
	h[1] += b
	h[2] += c
	h[3] += d
	h[4] += e
	h[5] += f
	h[6] += g
	h[7] += hh
}

func hash(data []byte) []byte {
	h := [8]uint64{
		0x6A09E667F3BCC908, 0xBB67AE8584CAA73B, 0x3C6EF372FE94F82B, 0xA54FF53A5F1D36F1,
		0x510E527FADE682D1, 0x9B05688C2B3E6C1F, 0x1F83D9ABFB41BD6B, 0x5BE0CD19137E2179,
	}
	var w [80]uint64
	blocks := len(data) / 128
	for i := 0; i < blocks; i++ {
		compress(&h, &w, data, i*128)
	}

	// Padding: 0x80, zeros, then the message length in bits as a 128-bit big-endian number.
	tail := make([]byte, 256)
	rest := len(data) - blocks*128
	for i := 0; i < rest; i++ {
		tail[i] = data[blocks*128+i]
	}
	tail[rest] = 0x80
	tailLength := 256
	if rest < 112 {
		tailLength = 128
	}
	bits := uint64(len(data)) * 8
	for b := 0; b < 8; b++ {
		tail[tailLength-1-b] = byte(bits >> (8 * b))
	}
	for offset := 0; offset < tailLength; offset += 128 {
		compress(&h, &w, tail, offset)
	}

	digest := make([]byte, 64)
	for i := 0; i < 8; i++ {
		for b := 0; b < 8; b++ {
			digest[8*i+b] = byte(h[i] >> (56 - 8*b))
		}
	}
	return digest
}

func main() {
	mebibytes := argument(1, 16)
	rounds := argument(2, 16)

	buffer := make([]byte, mebibytes*1048576)
	state := uint64(1)
	for i := 0; i < len(buffer); i += 8 {
		value := splitMix(&state)
		for b := 0; b < 8; b++ {
			buffer[i+b] = byte(value >> (8 * b))
		}
	}

	digest := hash(buffer)
	for r := 1; r < rounds; r++ {
		copy(buffer[:64], digest)
		digest = hash(buffer)
	}

	out := bufio.NewWriter(os.Stdout)
	fmt.Fprintf(out, "%x\n", digest)
	out.Flush()
}
