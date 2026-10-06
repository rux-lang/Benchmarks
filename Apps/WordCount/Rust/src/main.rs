// WordCount: generates a pseudo-random text from a pseudo-random vocabulary, counts how often each
// word occurs with a hash map, and prints the 10 most frequent words.
use std::collections::HashMap;
use std::env;
use std::fmt::Write;

fn split_mix(state: &mut u64) -> u64 {
    *state = state.wrapping_add(0x9E3779B97F4A7C15);
    let mut z = *state;
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58476D1CE4E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D049BB133111EB);
    z ^ (z >> 31)
}

fn pick_word(state: &mut u64, vocabulary_size: u64) -> usize {
    let a = split_mix(state) % vocabulary_size;
    let b = split_mix(state) % vocabulary_size;
    a.min(b) as usize
}

fn before(word: &[u8], count: u32, other_word: &[u8], other_count: u32) -> bool {
    if count != other_count { count > other_count } else { word < other_word }
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let word_count: u64 = args.get(1).map_or(10000000, |a| a.parse().unwrap());
    let vocabulary_size: u64 = args.get(2).map_or(100000, |a| a.parse().unwrap());

    let mut state = 2u64;

    // Vocabulary: words of 2..12 letters, stored back to back.
    let mut vocabulary: Vec<u8> = Vec::new();
    let mut starts = vec![0usize; vocabulary_size as usize + 1];
    for k in 0..vocabulary_size as usize {
        starts[k] = vocabulary.len();
        let length = 2 + split_mix(&mut state) % 11;
        for _ in 0..length {
            vocabulary.push(b'a' + (split_mix(&mut state) % 26) as u8);
        }
    }
    starts[vocabulary_size as usize] = vocabulary.len();

    // Text: the generator runs twice, first to measure the text and then to write it.
    let text_state = state;
    let mut text_length = 0usize;
    for _ in 0..word_count {
        let word = pick_word(&mut state, vocabulary_size);
        text_length += starts[word + 1] - starts[word] + 1;
    }
    state = text_state;
    let mut text = vec![0u8; text_length];
    let mut position = 0;
    for i in 0..word_count {
        let word = pick_word(&mut state, vocabulary_size);
        for j in starts[word]..starts[word + 1] {
            text[position] = vocabulary[j];
            position += 1;
        }
        text[position] = if (i + 1) % 16 == 0 { b'\n' } else { b' ' };
        position += 1;
    }

    // Count words: maximal runs of the letters a..z.
    let mut counts: HashMap<&[u8], u32> = HashMap::new();
    let mut total = 0u64;
    let mut start = 0;
    for i in 0..=text_length {
        if i < text_length && text[i].is_ascii_lowercase() {
            continue;
        }
        if i > start {
            *counts.entry(&text[start..i]).or_insert(0) += 1;
            total += 1;
        }
        start = i + 1;
    }

    // Top 10 by count (descending), then by word (ascending).
    let mut top_words: [&[u8]; 10] = [&[]; 10];
    let mut top_counts = [0u32; 10];
    let mut filled = 0;
    for (&word, &count) in &counts {
        let mut slot = filled;
        while slot > 0 && before(word, count, top_words[slot - 1], top_counts[slot - 1]) {
            slot -= 1;
        }
        if slot >= 10 {
            continue;
        }
        let last = if filled < 10 { filled } else { 9 };
        let mut s = last;
        while s > slot {
            top_words[s] = top_words[s - 1];
            top_counts[s] = top_counts[s - 1];
            s -= 1;
        }
        top_words[slot] = word;
        top_counts[slot] = count;
        if filled < 10 {
            filled += 1;
        }
    }

    let mut output = String::new();
    writeln!(output, "words {total} distinct {}", counts.len()).unwrap();
    for s in 0..filled {
        writeln!(output, "{} {}", top_counts[s], std::str::from_utf8(top_words[s]).unwrap()).unwrap();
    }
    print!("{output}");
}
