// NBody: simulates the Sun and the four gas giants (Computer Language Benchmarks Game model) and
// prints the total energy before and after as raw IEEE-754 bit patterns.
package main

import (
	"bufio"
	"fmt"
	"math"
	"os"
	"strconv"
)

const bodies = 5

// Variables rather than constants: Go evaluates constant expressions exactly, while the other
// languages round every intermediate product to a double.
var (
	pi          = 3.141592653589793
	solarMass   = 4.0 * pi * pi
	daysPerYear = 365.24
)

var (
	x    = [bodies]float64{0.0, 4.84143144246472090e+00, 8.34336671824457987e+00, 1.28943695621391310e+01, 1.53796971148509165e+01}
	y    = [bodies]float64{0.0, -1.16032004402742839e+00, 4.12479856412430479e+00, -1.51111514016986312e+01, -2.59193146099879641e+01}
	z    = [bodies]float64{0.0, -1.03622044471123109e-01, -4.03523417114321381e-01, -2.23307578892655734e-01, 1.79258772950371181e-01}
	vx   = [bodies]float64{0.0, 1.66007664274403694e-03 * daysPerYear, -2.76742510726862411e-03 * daysPerYear, 2.96460137564761618e-03 * daysPerYear, 2.68067772490389322e-03 * daysPerYear}
	vy   = [bodies]float64{0.0, 7.69901118419740425e-03 * daysPerYear, 4.99852801234917238e-03 * daysPerYear, 2.37847173959480950e-03 * daysPerYear, 1.62824170038242295e-03 * daysPerYear}
	vz   = [bodies]float64{0.0, -6.90460016972063023e-05 * daysPerYear, 2.30417297573763929e-05 * daysPerYear, -2.96589568540237556e-05 * daysPerYear, -9.51592254519715870e-05 * daysPerYear}
	mass = [bodies]float64{solarMass, 9.54791938424326609e-04 * solarMass, 2.85885980666130812e-04 * solarMass, 4.36624404335156298e-05 * solarMass, 5.15138902046611451e-05 * solarMass}
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

func advance(dt float64) {
	for i := 0; i < bodies; i++ {
		for j := i + 1; j < bodies; j++ {
			dx := x[i] - x[j]
			dy := y[i] - y[j]
			dz := z[i] - z[j]
			distance2 := dx*dx + dy*dy + dz*dz
			magnitude := dt / (distance2 * math.Sqrt(distance2))
			vx[i] -= dx * mass[j] * magnitude
			vy[i] -= dy * mass[j] * magnitude
			vz[i] -= dz * mass[j] * magnitude
			vx[j] += dx * mass[i] * magnitude
			vy[j] += dy * mass[i] * magnitude
			vz[j] += dz * mass[i] * magnitude
		}
	}
	for i := 0; i < bodies; i++ {
		x[i] += dt * vx[i]
		y[i] += dt * vy[i]
		z[i] += dt * vz[i]
	}
}

func energy() float64 {
	energy := 0.0
	for i := 0; i < bodies; i++ {
		energy += 0.5 * mass[i] * (vx[i]*vx[i] + vy[i]*vy[i] + vz[i]*vz[i])
		for j := i + 1; j < bodies; j++ {
			dx := x[i] - x[j]
			dy := y[i] - y[j]
			dz := z[i] - z[j]
			energy -= mass[i] * mass[j] / math.Sqrt(dx*dx+dy*dy+dz*dz)
		}
	}
	return energy
}

func main() {
	steps := argument(1, 5000000)

	// Offset the Sun's momentum so the system's total momentum is zero.
	px, py, pz := 0.0, 0.0, 0.0
	for i := 0; i < bodies; i++ {
		px += vx[i] * mass[i]
		py += vy[i] * mass[i]
		pz += vz[i] * mass[i]
	}
	vx[0] = -px / solarMass
	vy[0] = -py / solarMass
	vz[0] = -pz / solarMass

	before := energy()
	for step := 0; step < steps; step++ {
		advance(0.01)
	}
	after := energy()

	out := bufio.NewWriter(os.Stdout)
	fmt.Fprintf(out, "%016x %016x\n", math.Float64bits(before), math.Float64bits(after))
	out.Flush()
}
