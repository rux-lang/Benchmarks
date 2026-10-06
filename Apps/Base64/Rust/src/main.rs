// Base64: encodes pseudo-random bytes as Base64 and decodes them again with a hand-written codec,
// checks the round trip, and prints the encoded length and a hash of the encoded text.
use std::env;
use std::process::ExitCode;

const ALPHABET: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

fn split_mix(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E3779B97F4A7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58476D1CE4E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D049BB133111EB);
    z ^ (z >> 31)
}

fn encode(input: &[u8], output: &mut [u8]) {
    let full = input.len() / 3 * 3;
    let mut o = 0;
    let mut i = 0;
    while i < full {
        let triple = (input[i] as u32) << 16 | (input[i + 1] as u32) << 8 | input[i + 2] as u32;
        output[o] = ALPHABET[(triple >> 18) as usize & 63];
        output[o + 1] = ALPHABET[(triple >> 12) as usize & 63];
        output[o + 2] = ALPHABET[(triple >> 6) as usize & 63];
        output[o + 3] = ALPHABET[triple as usize & 63];
        o += 4;
        i += 3;
    }
    let rest = input.len() - full;
    if rest == 1 {
        let triple = (input[full] as u32) << 16;
        output[o] = ALPHABET[(triple >> 18) as usize & 63];
        output[o + 1] = ALPHABET[(triple >> 12) as usize & 63];
        output[o + 2] = b'=';
        output[o + 3] = b'=';
    } else if rest == 2 {
        let triple = (input[full] as u32) << 16 | (input[full + 1] as u32) << 8;
        output[o] = ALPHABET[(triple >> 18) as usize & 63];
        output[o + 1] = ALPHABET[(triple >> 12) as usize & 63];
        output[o + 2] = ALPHABET[(triple >> 6) as usize & 63];
        output[o + 3] = b'=';
    }
}

// Returns the number of decoded bytes, or None for invalid input.
fn decode(input: &[u8], output: &mut [u8], table: &[i32; 256]) -> Option<usize> {
    let mut o = 0;
    let mut i = 0;
    while i < input.len() {
        let a = table[input[i] as usize];
        let b = table[input[i + 1] as usize];
        if a < 0 || b < 0 {
            return None;
        }
        output[o] = ((a << 2) | (b >> 4)) as u8;
        o += 1;
        if input[i + 2] == b'=' {
            break;
        }
        let c = table[input[i + 2] as usize];
        if c < 0 {
            return None;
        }
        output[o] = (((b & 15) << 4) | (c >> 2)) as u8;
        o += 1;
        if input[i + 3] == b'=' {
            break;
        }
        let d = table[input[i + 3] as usize];
        if d < 0 {
            return None;
        }
        output[o] = (((c & 3) << 6) | d) as u8;
        o += 1;
        i += 4;
    }
    Some(o)
}

fn main() -> ExitCode {
    let args: Vec<String> = env::args().collect();
    let mebibytes: usize = args.get(1).map_or(32, |a| a.parse().unwrap());
    let rounds: u64 = args.get(2).map_or(4, |a| a.parse().unwrap());

    let length = mebibytes * 1048576;
    let mut data = vec![0u8; length];
    let mut state = 4u64;
    let mut i = 0;
    while i < length {
        let value = split_mix(&mut state);
        for b in 0..8 {
            data[i + b] = (value >> (8 * b)) as u8;
        }
        i += 8;
    }

    let mut decode_table = [-1i32; 256];
    for (i, &symbol) in ALPHABET.iter().enumerate() {
        decode_table[symbol as usize] = i as i32;
    }

    let encoded_length = (length + 2) / 3 * 4;
    let mut encoded = vec![0u8; encoded_length];
    let mut decoded = vec![0u8; length];
    for _ in 0..rounds {
        encode(&data, &mut encoded);
        let mut same = decode(&encoded, &mut decoded, &decode_table) == Some(length);
        let mut i = 0;
        while same && i < length {
            same = decoded[i] == data[i];
            i += 1;
        }
        if !same {
            println!("round trip failed");
            return ExitCode::FAILURE;
        }
    }

    let mut hash = 0xCBF29CE484222325u64;
    for &b in &encoded {
        hash = (hash ^ b as u64).wrapping_mul(0x100000001B3);
    }
    println!("{encoded_length} {hash:016x}");
    ExitCode::SUCCESS
}
