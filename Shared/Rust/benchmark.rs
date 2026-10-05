use std::io::{self, Read};
use std::time::Instant;

#[derive(Clone, Copy)]
pub struct Parameters {
    pub size: usize,
    pub work: usize,
    pub seed: usize,
    pub warmups: usize,
    pub samples: usize,
}
pub struct Measurement {
    pub seconds: f64,
    pub sum: f64,
    pub weighted: f64,
}
pub fn elapsed(start: Instant) -> f64 {
    start.elapsed().as_secs_f64()
}

pub fn run(
    name: &str,
    max_size: usize,
    power_of_two: bool,
    run_once: fn(Parameters) -> Measurement,
) {
    if let Err(e) = execute(name, max_size, power_of_two, run_once) {
        eprintln!("{e}");
        std::process::exit(1);
    }
}
fn execute(
    name: &str,
    max_size: usize,
    power_of_two: bool,
    run_once: fn(Parameters) -> Measurement,
) -> Result<(), String> {
    let mut input = String::new();
    io::stdin()
        .read_to_string(&mut input)
        .map_err(|e| e.to_string())?;
    let values: Vec<usize> = input
        .split_whitespace()
        .map(|s| {
            if s.is_empty() || !s.bytes().all(|b| b.is_ascii_digit()) {
                return Err("Invalid integer".to_owned());
            }
            s.parse::<usize>().map_err(|e| e.to_string())
        })
        .collect::<Result<_, _>>()?;
    if values.len() != 6
        || values.iter().any(|&v| v > 2147483647)
        || values[0] != 1
        || values[1] < 2
        || values[1] > max_size
        || values[2] < 1
        || values[2] > 1000000
        || values[3] > 1000000
        || values[4] > 100
        || values[5] < 1
        || values[5] > 1000
        || (power_of_two && !values[1].is_power_of_two())
    {
        return Err("Invalid protocol version or parameter range".to_owned());
    }
    let p = Parameters {
        size: values[1],
        work: values[2],
        seed: values[3],
        warmups: values[4],
        samples: values[5],
    };
    let mut results = Vec::with_capacity(p.samples);
    for i in 0..p.warmups + p.samples {
        let m = run_once(p);
        if !m.sum.is_finite()
            || !m.weighted.is_finite()
            || !m.seconds.is_finite()
            || m.seconds < 0.0
        {
            return Err("Nonfinite result or invalid clock".to_owned());
        }
        if i >= p.warmups {
            results.push(m);
        }
    }
    print!(
        "{{\"Protocol\":1,\"Benchmark\":\"{name}\",\"Size\":{},\"Work\":{},\"Seed\":{},\"Warmups\":{},\"Samples\":[",
        p.size, p.work, p.seed, p.warmups
    );
    for (i, m) in results.iter().enumerate() {
        if i != 0 {
            print!(",");
        }
        print!(
            "{{\"Seconds\":{},\"Sum\":{},\"Weighted\":{}}}",
            m.seconds, m.sum, m.weighted
        );
    }
    println!("]}}");
    Ok(())
}
