// MatrixMultiply: multiplies two generated n×n matrices of doubles with the cache-friendly i-k-j
// loop order and prints a hash of the result's bit patterns.
use std::env;

fn main() {
    let args: Vec<String> = env::args().collect();
    let n: usize = args.get(1).map_or(1024, |a| a.parse().unwrap());

    let mut a = vec![0.0f64; n * n];
    let mut b = vec![0.0f64; n * n];
    let mut c = vec![0.0f64; n * n];
    for i in 0..n {
        for j in 0..n {
            a[i * n + j] = (((i + 3 * j) % 17) as f64 - 8.0) / 8.0;
            b[i * n + j] = (((2 * i + j) % 13) as f64 - 6.0) / 6.0;
        }
    }

    for i in 0..n {
        for k in 0..n {
            let aik = a[i * n + k];
            for j in 0..n {
                c[i * n + j] += aik * b[k * n + j];
            }
        }
    }

    let mut hash = 0xCBF29CE484222325u64;
    for value in &c {
        hash = (hash ^ value.to_bits()).wrapping_mul(0x100000001B3);
    }
    println!("{n} {hash:016x}");
}
