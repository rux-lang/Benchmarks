#[path = "../../../../Shared/Rust/benchmark.rs"]
mod benchmark;
use benchmark::{Measurement, Parameters, elapsed};
use std::time::Instant;

fn run_once(p: Parameters) -> Measurement {
    let n = p.size;
    let mut x = vec![0.0; n];
    let mut y = vec![0.0; n];
    let mut z = vec![0.0; n];
    let mut vx = vec![0.0; n];
    let mut vy = vec![0.0; n];
    let mut vz = vec![0.0; n];
    for i in 0..n {
        x[i] = (i % 16) as f64 - 8.0 + p.seed as f64 / 1024.0;
        y[i] = ((i / 16) % 16) as f64 - 8.0;
        z[i] = (i / 256) as f64 - 4.0;
    }
    let start = Instant::now();
    for _ in 0..p.work {
        for i in 0..n {
            for j in i + 1..n {
                let dx = x[j] - x[i];
                let dy = y[j] - y[i];
                let dz = z[j] - z[i];
                let distance = dx * dx + dy * dy + dz * dz + 0.01;
                let inverse = 1.0 / distance.sqrt();
                let scale = 0.001 * inverse * inverse * inverse / n as f64;
                vx[i] += dx * scale;
                vy[i] += dy * scale;
                vz[i] += dz * scale;
                vx[j] -= dx * scale;
                vy[j] -= dy * scale;
                vz[j] -= dz * scale;
            }
        }
        for i in 0..n {
            x[i] += 0.001 * vx[i];
            y[i] += 0.001 * vy[i];
            z[i] += 0.001 * vz[i];
        }
    }
    let seconds = elapsed(start);
    let (mut sum, mut weighted) = (0.0, 0.0);
    for i in 0..n {
        let value = x[i] + 2.0 * y[i] + 3.0 * z[i] + 4.0 * vx[i] + 5.0 * vy[i] + 6.0 * vz[i];
        sum += value;
        weighted += value * (i % 7 + 1) as f64;
    }
    Measurement {
        seconds,
        sum,
        weighted,
    }
}

fn main() {
    benchmark::run("NBody", 8192, false, run_once);
}
