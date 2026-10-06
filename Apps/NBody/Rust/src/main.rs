// NBody: simulates the Sun and the four gas giants (Computer Language Benchmarks Game model) and
// prints the total energy before and after as raw IEEE-754 bit patterns.
use std::env;

const PI: f64 = 3.141592653589793;
const SOLAR_MASS: f64 = 4.0 * PI * PI;
const DAYS_PER_YEAR: f64 = 365.24;
const BODIES: usize = 5;

struct System {
    x: [f64; BODIES],
    y: [f64; BODIES],
    z: [f64; BODIES],
    vx: [f64; BODIES],
    vy: [f64; BODIES],
    vz: [f64; BODIES],
    mass: [f64; BODIES],
}

impl System {
    fn advance(&mut self, dt: f64) {
        for i in 0..BODIES {
            for j in i + 1..BODIES {
                let dx = self.x[i] - self.x[j];
                let dy = self.y[i] - self.y[j];
                let dz = self.z[i] - self.z[j];
                let distance2 = dx * dx + dy * dy + dz * dz;
                let magnitude = dt / (distance2 * distance2.sqrt());
                self.vx[i] -= dx * self.mass[j] * magnitude;
                self.vy[i] -= dy * self.mass[j] * magnitude;
                self.vz[i] -= dz * self.mass[j] * magnitude;
                self.vx[j] += dx * self.mass[i] * magnitude;
                self.vy[j] += dy * self.mass[i] * magnitude;
                self.vz[j] += dz * self.mass[i] * magnitude;
            }
        }
        for i in 0..BODIES {
            self.x[i] += dt * self.vx[i];
            self.y[i] += dt * self.vy[i];
            self.z[i] += dt * self.vz[i];
        }
    }

    fn energy(&self) -> f64 {
        let mut energy = 0.0;
        for i in 0..BODIES {
            energy += 0.5 * self.mass[i] * (self.vx[i] * self.vx[i] + self.vy[i] * self.vy[i] + self.vz[i] * self.vz[i]);
            for j in i + 1..BODIES {
                let dx = self.x[i] - self.x[j];
                let dy = self.y[i] - self.y[j];
                let dz = self.z[i] - self.z[j];
                energy -= self.mass[i] * self.mass[j] / (dx * dx + dy * dy + dz * dz).sqrt();
            }
        }
        energy
    }
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let steps: u64 = args.get(1).map_or(5000000, |a| a.parse().unwrap());

    let mut system = System {
        x: [0.0, 4.84143144246472090e+00, 8.34336671824457987e+00, 1.28943695621391310e+01, 1.53796971148509165e+01],
        y: [0.0, -1.16032004402742839e+00, 4.12479856412430479e+00, -1.51111514016986312e+01, -2.59193146099879641e+01],
        z: [0.0, -1.03622044471123109e-01, -4.03523417114321381e-01, -2.23307578892655734e-01, 1.79258772950371181e-01],
        vx: [0.0, 1.66007664274403694e-03 * DAYS_PER_YEAR, -2.76742510726862411e-03 * DAYS_PER_YEAR, 2.96460137564761618e-03 * DAYS_PER_YEAR, 2.68067772490389322e-03 * DAYS_PER_YEAR],
        vy: [0.0, 7.69901118419740425e-03 * DAYS_PER_YEAR, 4.99852801234917238e-03 * DAYS_PER_YEAR, 2.37847173959480950e-03 * DAYS_PER_YEAR, 1.62824170038242295e-03 * DAYS_PER_YEAR],
        vz: [0.0, -6.90460016972063023e-05 * DAYS_PER_YEAR, 2.30417297573763929e-05 * DAYS_PER_YEAR, -2.96589568540237556e-05 * DAYS_PER_YEAR, -9.51592254519715870e-05 * DAYS_PER_YEAR],
        mass: [SOLAR_MASS, 9.54791938424326609e-04 * SOLAR_MASS, 2.85885980666130812e-04 * SOLAR_MASS, 4.36624404335156298e-05 * SOLAR_MASS, 5.15138902046611451e-05 * SOLAR_MASS],
    };

    // Offset the Sun's momentum so the system's total momentum is zero.
    let (mut px, mut py, mut pz) = (0.0f64, 0.0f64, 0.0f64);
    for i in 0..BODIES {
        px += system.vx[i] * system.mass[i];
        py += system.vy[i] * system.mass[i];
        pz += system.vz[i] * system.mass[i];
    }
    system.vx[0] = -px / SOLAR_MASS;
    system.vy[0] = -py / SOLAR_MASS;
    system.vz[0] = -pz / SOLAR_MASS;

    let before = system.energy();
    for _ in 0..steps {
        system.advance(0.01);
    }
    let after = system.energy();
    println!("{:016x} {:016x}", before.to_bits(), after.to_bits());
}
