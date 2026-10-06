// Fannkuch: for every permutation of 0..n-1, counts how many prefix reversals ("pancake flips")
// it takes until 0 comes first (single-threaded fannkuch-redux from the Benchmarks Game).
use std::env;

fn main() {
    let args: Vec<String> = env::args().collect();
    let n: usize = args.get(1).map_or(10, |a| a.parse().unwrap());

    let mut permutation = vec![0usize; n];
    let mut current: Vec<usize> = (0..n).collect();
    let mut counters = vec![0usize; n];

    let mut max_flips = 0;
    let mut checksum = 0i64;
    let mut permutation_index = 0u64;
    let mut r = n;
    loop {
        while r != 1 {
            counters[r - 1] = r;
            r -= 1;
        }

        permutation.copy_from_slice(&current);
        let mut flips = 0;
        let mut first = permutation[0];
        while first != 0 {
            let (mut low, mut high) = (0, first);
            while low < high {
                permutation.swap(low, high);
                low += 1;
                high -= 1;
            }
            flips += 1;
            first = permutation[0];
        }
        if flips > max_flips {
            max_flips = flips;
        }
        checksum += if permutation_index % 2 == 0 { flips } else { -flips };

        // Next permutation in the order used by the reference program.
        loop {
            if r == n {
                print!("{checksum}\nPfannkuchen({n}) = {max_flips}\n");
                return;
            }
            let rotated = current[0];
            for i in 0..r {
                current[i] = current[i + 1];
            }
            current[r] = rotated;
            counters[r] -= 1;
            if counters[r] > 0 {
                break;
            }
            r += 1;
        }
        permutation_index += 1;
    }
}
