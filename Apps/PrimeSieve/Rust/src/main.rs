// PrimeSieve: finds every prime up to n with the sieve of Eratosthenes over a byte array and prints
// how many there are and their sum.
use std::env;

fn main() {
    let args: Vec<String> = env::args().collect();
    let limit: usize = args.get(1).map_or(100000000, |a| a.parse().unwrap());

    let mut composite = vec![0u8; limit + 1];
    let mut i = 2;
    while i * i <= limit {
        if composite[i] == 0 {
            let mut j = i * i;
            while j <= limit {
                composite[j] = 1;
                j += i;
            }
        }
        i += 1;
    }

    let mut count = 0u64;
    let mut sum = 0u64;
    for k in 2..=limit {
        if composite[k] == 0 {
            count += 1;
            sum += k as u64;
        }
    }
    println!("{count} {sum}");
}
