#[path = "../../../../Shared/Rust/benchmark.rs"]
mod benchmark;
use benchmark::{Measurement, Parameters, elapsed};
use std::time::Instant;

fn run_once(p: Parameters) -> Measurement {
    let n = p.size;
    let mut composite = vec![0u8; n + 1];
    let start = Instant::now();
    let mut i = 2;
    while i <= n / i {
        if composite[i] == 0 {
            for j in (i * i..=n).step_by(i) {
                composite[j] = 1;
            }
        }
        i += 1;
    }
    let seconds = elapsed(start);
    let (mut count, mut sum) = (0i64, 0i64);
    for i in 2..=n {
        if composite[i] == 0 {
            count += 1;
            sum += i as i64;
        }
    }
    Measurement {
        seconds,
        sum: count as f64,
        weighted: sum as f64,
    }
}

fn main() {
    benchmark::run("PrimeSieve", 200000000, false, run_once);
}
