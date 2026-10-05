#[path = "../../../../Shared/Rust/benchmark.rs"]
mod benchmark;
use benchmark::{Measurement, Parameters, elapsed};
use std::time::Instant;

fn run_once(p: Parameters) -> Measurement {
    let n = p.size;
    let mut a = vec![0.0; n * n];
    let mut b = vec![0.0; n * n];
    let mut c = vec![0.0; n * n];
    for i in 0..n {
        for j in 0..n {
            a[i * n + j] = (((i + 3 * j + p.seed) % 17) as i32 - 8) as f64 / 8.0;
            b[i * n + j] = (((5 * i + j + p.seed) % 13) as i32 - 6) as f64 / 8.0;
        }
    }
    let start = Instant::now();
    for i in 0..n {
        for k in 0..n {
            let value = a[i * n + k];
            for j in 0..n {
                c[i * n + j] += value * b[k * n + j];
            }
        }
    }
    let seconds = elapsed(start);
    let (mut sum, mut weighted) = (0.0, 0.0);
    for i in 0..n * n {
        sum += c[i];
        weighted += c[i] * (i % 7 + 1) as f64;
    }
    Measurement {
        seconds,
        sum,
        weighted,
    }
}

fn main() {
    benchmark::run("MatrixMultiply", 2048, false, run_once);
}
