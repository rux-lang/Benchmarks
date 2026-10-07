// Mandelbrot: renders the Mandelbrot set into a binary PPM image, writes it to Mandelbrot.ppm and
// prints the image size and the FNV-1a hash of the file contents.
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
	size := argument(1, 2000)
	maxIterations := argument(2, 500)

	header := fmt.Sprintf("P6\n%d %d\n255\n", size, size)
	image := make([]byte, len(header)+3*size*size)
	copy(image, header)

	offset := len(header)
	for py := 0; py < size; py++ {
		ci := 3.0*float64(py)/float64(size) - 1.5
		for px := 0; px < size; px++ {
			cr := 3.0*float64(px)/float64(size) - 2.0
			zr, zi := 0.0, 0.0
			iteration := 0
			for iteration < maxIterations {
				zr2 := zr * zr
				zi2 := zi * zi
				if zr2+zi2 > 4.0 {
					break
				}
				zi = 2.0*zr*zi + ci
				zr = zr2 - zi2 + cr
				iteration++
			}
			if iteration < maxIterations {
				image[offset] = byte(iteration * 7)
				image[offset+1] = byte(iteration * 13)
				image[offset+2] = byte(iteration * 29)
			}
			offset += 3
		}
	}

	if err := os.WriteFile("Mandelbrot.ppm", image, 0o644); err != nil {
		panic(err)
	}

	hash := uint64(0xCBF29CE484222325)
	for _, b := range image {
		hash = (hash ^ uint64(b)) * 0x100000001B3
	}
	out := bufio.NewWriter(os.Stdout)
	fmt.Fprintf(out, "%dx%d %016x\n", size, size, hash)
	out.Flush()
}
