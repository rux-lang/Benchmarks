#[path = "../../../../Shared/Rust/benchmark.rs"]
mod benchmark;
use benchmark::{Measurement, Parameters, elapsed};
use std::time::Instant;

fn run_once(p: Parameters) -> Measurement {
    let n = p.size;
    let mut counts = vec![0i32; n * n];
    let start = Instant::now();
    for y in 0..n {
        for x in 0..n {
            let cr = 3.0 * x as f64 / n as f64 - 2.0;
            let ci = 2.0 * y as f64 / n as f64 - 1.0;
            let (mut zr, mut zi) = (0.0, 0.0);
            let mut count = 0;
            while zr * zr + zi * zi <= 4.0 && count < p.work as i32 {
                let next = zr * zr - zi * zi + cr;
                zi = 2.0 * zr * zi + ci;
                zr = next;
                count += 1;
            }
            counts[y * n + x] = count;
        }
    }
    let seconds = elapsed(start);
    let (mut sum, mut weighted) = (0i64, 0i64);
    for i in 0..n * n {
        sum += counts[i] as i64;
        weighted += counts[i] as i64 * (i % 7 + 1) as i64;
    }
    Measurement {
        seconds,
        sum: sum as f64,
        weighted: weighted as f64,
    }
}

fn main() {
    benchmark::run("Mandelbrot", 4096, false, run_once);
}
