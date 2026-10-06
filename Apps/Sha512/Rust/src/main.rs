// Sha512: fills a buffer with pseudo-random bytes and hashes it with a hand-written SHA-512.
// Each further round writes the previous digest into the first 64 bytes and hashes again.
use std::env;

const K: [u64; 80] = [
    0x428A2F98D728AE22, 0x7137449123EF65CD, 0xB5C0FBCFEC4D3B2F, 0xE9B5DBA58189DBBC,
    0x3956C25BF348B538, 0x59F111F1B605D019, 0x923F82A4AF194F9B, 0xAB1C5ED5DA6D8118,
    0xD807AA98A3030242, 0x12835B0145706FBE, 0x243185BE4EE4B28C, 0x550C7DC3D5FFB4E2,
    0x72BE5D74F27B896F, 0x80DEB1FE3B1696B1, 0x9BDC06A725C71235, 0xC19BF174CF692694,
    0xE49B69C19EF14AD2, 0xEFBE4786384F25E3, 0x0FC19DC68B8CD5B5, 0x240CA1CC77AC9C65,
    0x2DE92C6F592B0275, 0x4A7484AA6EA6E483, 0x5CB0A9DCBD41FBD4, 0x76F988DA831153B5,
    0x983E5152EE66DFAB, 0xA831C66D2DB43210, 0xB00327C898FB213F, 0xBF597FC7BEEF0EE4,
    0xC6E00BF33DA88FC2, 0xD5A79147930AA725, 0x06CA6351E003826F, 0x142929670A0E6E70,
    0x27B70A8546D22FFC, 0x2E1B21385C26C926, 0x4D2C6DFC5AC42AED, 0x53380D139D95B3DF,
    0x650A73548BAF63DE, 0x766A0ABB3C77B2A8, 0x81C2C92E47EDAEE6, 0x92722C851482353B,
    0xA2BFE8A14CF10364, 0xA81A664BBC423001, 0xC24B8B70D0F89791, 0xC76C51A30654BE30,
    0xD192E819D6EF5218, 0xD69906245565A910, 0xF40E35855771202A, 0x106AA07032BBD1B8,
    0x19A4C116B8D2D0C8, 0x1E376C085141AB53, 0x2748774CDF8EEB99, 0x34B0BCB5E19B48A8,
    0x391C0CB3C5C95A63, 0x4ED8AA4AE3418ACB, 0x5B9CCA4F7763E373, 0x682E6FF3D6B2B8A3,
    0x748F82EE5DEFB2FC, 0x78A5636F43172F60, 0x84C87814A1F0AB72, 0x8CC702081A6439EC,
    0x90BEFFFA23631E28, 0xA4506CEBDE82BDE9, 0xBEF9A3F7B2C67915, 0xC67178F2E372532B,
    0xCA273ECEEA26619C, 0xD186B8C721C0C207, 0xEADA7DD6CDE0EB1E, 0xF57D4F7FEE6ED178,
    0x06F067AA72176FBA, 0x0A637DC5A2C898A6, 0x113F9804BEF90DAE, 0x1B710B35131C471B,
    0x28DB77F523047D84, 0x32CAAB7B40C72493, 0x3C9EBE0A15C9BEBC, 0x431D67C49C100D4C,
    0x4CC5D4BECB3E42B6, 0x597F299CFC657E2A, 0x5FCB6FAB3AD6FAEC, 0x6C44198C4A475817
];

fn split_mix(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E3779B97F4A7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58476D1CE4E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D049BB133111EB);
    z ^ (z >> 31)
}

fn rotate_right(x: u64, n: u32) -> u64 {
    (x >> n) | (x << (64 - n))
}

fn load_big_endian(data: &[u8]) -> u64 {
    let mut value = 0u64;
    for b in 0..8 {
        value = (value << 8) | data[b] as u64;
    }
    value
}

fn compress(h: &mut [u64; 8], w: &mut [u64; 80], block: &[u8]) {
    for t in 0..16 {
        w[t] = load_big_endian(&block[8 * t..]);
    }
    for t in 16..80 {
        let s0 = rotate_right(w[t - 15], 1) ^ rotate_right(w[t - 15], 8) ^ (w[t - 15] >> 7);
        let s1 = rotate_right(w[t - 2], 19) ^ rotate_right(w[t - 2], 61) ^ (w[t - 2] >> 6);
        w[t] = w[t - 16].wrapping_add(s0).wrapping_add(w[t - 7]).wrapping_add(s1);
    }
    let (mut a, mut b, mut c, mut d, mut e, mut f, mut g, mut hh) = (h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7]);
    for t in 0..80 {
        let s1 = rotate_right(e, 14) ^ rotate_right(e, 18) ^ rotate_right(e, 41);
        let ch = (e & f) ^ (!e & g);
        let t1 = hh.wrapping_add(s1).wrapping_add(ch).wrapping_add(K[t]).wrapping_add(w[t]);
        let s0 = rotate_right(a, 28) ^ rotate_right(a, 34) ^ rotate_right(a, 39);
        let maj = (a & b) ^ (a & c) ^ (b & c);
        let t2 = s0.wrapping_add(maj);
        hh = g; g = f; f = e; e = d.wrapping_add(t1); d = c; c = b; b = a; a = t1.wrapping_add(t2);
    }
    let words = [a, b, c, d, e, f, g, hh];
    for i in 0..8 {
        h[i] = h[i].wrapping_add(words[i]);
    }
}

fn hash(data: &[u8]) -> [u8; 64] {
    let mut h: [u64; 8] = [
        0x6A09E667F3BCC908, 0xBB67AE8584CAA73B, 0x3C6EF372FE94F82B, 0xA54FF53A5F1D36F1,
        0x510E527FADE682D1, 0x9B05688C2B3E6C1F, 0x1F83D9ABFB41BD6B, 0x5BE0CD19137E2179
    ];
    let mut w = [0u64; 80];
    let blocks = data.len() / 128;
    for i in 0..blocks {
        compress(&mut h, &mut w, &data[i * 128..i * 128 + 128]);
    }

    // Padding: 0x80, zeros, then the message length in bits as a 128-bit big-endian number.
    let mut tail = [0u8; 256];
    let rest = data.len() - blocks * 128;
    for i in 0..rest {
        tail[i] = data[blocks * 128 + i];
    }
    tail[rest] = 0x80;
    let tail_length = if rest < 112 { 128 } else { 256 };
    let bits = data.len() as u64 * 8;
    for b in 0..8 {
        tail[tail_length - 1 - b] = (bits >> (8 * b)) as u8;
    }
    let mut offset = 0;
    while offset < tail_length {
        compress(&mut h, &mut w, &tail[offset..offset + 128]);
        offset += 128;
    }

    let mut digest = [0u8; 64];
    for i in 0..8 {
        for b in 0..8 {
            digest[8 * i + b] = (h[i] >> (56 - 8 * b)) as u8;
        }
    }
    digest
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let mebibytes: usize = args.get(1).map_or(16, |a| a.parse().unwrap());
    let rounds: u64 = args.get(2).map_or(16, |a| a.parse().unwrap());

    let mut buffer = vec![0u8; mebibytes * 1048576];
    let mut state = 1u64;
    let mut i = 0;
    while i < buffer.len() {
        let value = split_mix(&mut state);
        for b in 0..8 {
            buffer[i + b] = (value >> (8 * b)) as u8;
        }
        i += 8;
    }

    let mut digest = hash(&buffer);
    for _ in 1..rounds {
        buffer[..64].copy_from_slice(&digest);
        digest = hash(&buffer);
    }
    let hex: String = digest.iter().map(|b| format!("{b:02x}")).collect();
    println!("{hex}");
}
