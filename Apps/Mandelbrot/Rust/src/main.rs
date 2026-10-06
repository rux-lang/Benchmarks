// Mandelbrot: renders the Mandelbrot set into a binary PPM image, writes it to Mandelbrot.ppm and
// prints the image size and the FNV-1a hash of the file contents.
use std::env;
use std::fs;

fn main() {
    let args: Vec<String> = env::args().collect();
    let size: usize = args.get(1).map_or(2000, |a| a.parse().unwrap());
    let max_iterations: u32 = args.get(2).map_or(500, |a| a.parse().unwrap());

    let header = format!("P6\n{size} {size}\n255\n");
    let mut image = vec![0u8; header.len() + 3 * size * size];
    image[..header.len()].copy_from_slice(header.as_bytes());

    let mut offset = header.len();
    for py in 0..size {
        let ci = 3.0 * py as f64 / size as f64 - 1.5;
        for px in 0..size {
            let cr = 3.0 * px as f64 / size as f64 - 2.0;
            let (mut zr, mut zi) = (0.0f64, 0.0f64);
            let mut iteration = 0u32;
            while iteration < max_iterations {
                let zr2 = zr * zr;
                let zi2 = zi * zi;
                if zr2 + zi2 > 4.0 {
                    break;
                }
                zi = 2.0 * zr * zi + ci;
                zr = zr2 - zi2 + cr;
                iteration += 1;
            }
            if iteration < max_iterations {
                image[offset] = iteration.wrapping_mul(7) as u8;
                image[offset + 1] = iteration.wrapping_mul(13) as u8;
                image[offset + 2] = iteration.wrapping_mul(29) as u8;
            }
            offset += 3;
        }
    }

    fs::write("Mandelbrot.ppm", &image).expect("cannot write Mandelbrot.ppm");

    let mut hash = 0xCBF29CE484222325u64;
    for &b in &image {
        hash = (hash ^ b as u64).wrapping_mul(0x100000001B3);
    }
    println!("{size}x{size} {hash:016x}");
}
