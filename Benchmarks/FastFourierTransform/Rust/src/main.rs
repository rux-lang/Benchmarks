#[path = "../../../../Shared/Rust/benchmark.rs"]
mod benchmark;
use benchmark::{Measurement, Parameters, elapsed};
use std::time::Instant;

fn run_once(p: Parameters) -> Measurement {
    let n = p.size;
    let mut real = vec![0.0; n];
    let mut imag = vec![0.0; n];
    let mut wr = vec![0.0; n / 2];
    let mut wi = vec![0.0; n / 2];
    for i in 0..n {
        real[i] = (((i + p.seed) % 17) as i32 - 8) as f64 / 8.0;
    }
    for i in 0..n / 2 {
        let angle = -6.28318530717958647692 * i as f64 / n as f64;
        wr[i] = angle.cos();
        wi[i] = angle.sin();
    }
    let start = Instant::now();
    let mut j = 0;
    for i in 1..n {
        let mut bit = n >> 1;
        while (j & bit) != 0 {
            j ^= bit;
            bit >>= 1;
        }
        j ^= bit;
        if i < j {
            real.swap(i, j);
            imag.swap(i, j);
        }
    }
    let mut length = 2;
    while length <= n {
        let half = length / 2;
        let stride = n / length;
        for offset in (0..n).step_by(length) {
            for k in 0..half {
                let left = offset + k;
                let right = left + half;
                let tw = k * stride;
                let tr = wr[tw] * real[right] - wi[tw] * imag[right];
                let ti = wr[tw] * imag[right] + wi[tw] * real[right];
                let lr = real[left];
                let li = imag[left];
                real[left] = lr + tr;
                imag[left] = li + ti;
                real[right] = lr - tr;
                imag[right] = li - ti;
            }
        }
        length *= 2;
    }
    let seconds = elapsed(start);
    let (mut sum, mut weighted) = (0.0, 0.0);
    for i in 0..n {
        sum += real[i] + imag[i];
        weighted += real[i] * (i % 7 + 1) as f64 + imag[i] * (i % 5 + 1) as f64;
    }
    Measurement {
        seconds,
        sum,
        weighted,
    }
}

fn main() {
    benchmark::run("FastFourierTransform", 4194304, true, run_once);
}
