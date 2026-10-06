// Sort: sorts pseudo-random 32-bit integers with a hand-written quicksort, checks the order and
// prints a hash of the sorted array.
use std::env;
use std::process::ExitCode;

fn split_mix(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E3779B97F4A7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58476D1CE4E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D049BB133111EB);
    z ^ (z >> 31)
}

// Sorts values[low..=high]: median-of-three quicksort that recurses into the smaller part and
// leaves ranges of fewer than 16 elements to insertion sort.
fn quick_sort(values: &mut [i32], mut low: i64, mut high: i64) {
    while high - low >= 16 {
        let middle = low + (high - low) / 2;
        if values[middle as usize] < values[low as usize] {
            values.swap(low as usize, middle as usize);
        }
        if values[high as usize] < values[low as usize] {
            values.swap(low as usize, high as usize);
        }
        if values[high as usize] < values[middle as usize] {
            values.swap(middle as usize, high as usize);
        }
        let pivot = values[middle as usize];
        let (mut i, mut j) = (low, high);
        while i <= j {
            while values[i as usize] < pivot {
                i += 1;
            }
            while values[j as usize] > pivot {
                j -= 1;
            }
            if i <= j {
                values.swap(i as usize, j as usize);
                i += 1;
                j -= 1;
            }
        }
        if j - low < high - i {
            quick_sort(values, low, j);
            low = i;
        } else {
            quick_sort(values, i, high);
            high = j;
        }
    }
    let mut i = low + 1;
    while i <= high {
        let value = values[i as usize];
        let mut j = i - 1;
        while j >= low && values[j as usize] > value {
            values[(j + 1) as usize] = values[j as usize];
            j -= 1;
        }
        values[(j + 1) as usize] = value;
        i += 1;
    }
}

fn main() -> ExitCode {
    let args: Vec<String> = env::args().collect();
    let count: usize = args.get(1).map_or(10000000, |a| a.parse().unwrap());

    let mut state = 3u64;
    let mut values = vec![0i32; count];
    for value in values.iter_mut() {
        *value = (split_mix(&mut state) >> 32) as u32 as i32;
    }

    quick_sort(&mut values, 0, count as i64 - 1);

    let mut hash = 0xCBF29CE484222325u64;
    for i in 0..count {
        if i > 0 && values[i - 1] > values[i] {
            println!("unsorted");
            return ExitCode::FAILURE;
        }
        hash = (hash ^ values[i] as u32 as u64).wrapping_mul(0x100000001B3);
    }
    println!("{count} {hash:016x}");
    ExitCode::SUCCESS
}
