// WordCount: generates a pseudo-random text from a pseudo-random vocabulary, counts how often each
// word occurs with a hash map, and prints the 10 most frequent words.
package main

import (
	"bufio"
	"fmt"
	"os"
	"strconv"
)

func argument(index int, fallback int) int {
	if len(os.Args) <= index {
		return fallback
	}
	value, err := strconv.Atoi(os.Args[index])
	if err != nil {
		panic(err)
	}
	return value
}

func splitMix(state *uint64) uint64 {
	*state += 0x9E3779B97F4A7C15
	z := *state
	z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9
	z = (z ^ (z >> 27)) * 0x94D049BB133111EB
	return z ^ (z >> 31)
}

func pickWord(state *uint64, vocabularySize int) int {
	a := splitMix(state) % uint64(vocabularySize)
	b := splitMix(state) % uint64(vocabularySize)
	return int(min(a, b))
}

func before(word string, count int, otherWord string, otherCount int) bool {
	if count != otherCount {
		return count > otherCount
	}
	return word < otherWord
}

func main() {
	wordCount := argument(1, 10000000)
	vocabularySize := argument(2, 100000)

	state := uint64(2)

	// Vocabulary: words of 2..12 letters, stored back to back.
	var vocabulary []byte
	starts := make([]int, vocabularySize+1)
	for k := 0; k < vocabularySize; k++ {
		starts[k] = len(vocabulary)
		length := 2 + splitMix(&state)%11
		for i := uint64(0); i < length; i++ {
			vocabulary = append(vocabulary, byte('a'+splitMix(&state)%26))
		}
	}
	starts[vocabularySize] = len(vocabulary)

	// Text: the generator runs twice, first to measure the text and then to write it.
	textState := state
	textLength := 0
	for i := 0; i < wordCount; i++ {
		word := pickWord(&state, vocabularySize)
		textLength += starts[word+1] - starts[word] + 1
	}
	state = textState
	text := make([]byte, textLength)
	position := 0
	for i := 0; i < wordCount; i++ {
		word := pickWord(&state, vocabularySize)
		for j := starts[word]; j < starts[word+1]; j++ {
			text[position] = vocabulary[j]
			position++
		}
		if (i+1)%16 == 0 {
			text[position] = '\n'
		} else {
			text[position] = ' '
		}
		position++
	}

	// Count words: maximal runs of the letters a..z.
	counts := make(map[string]int)
	total := 0
	start := 0
	for i := 0; i <= textLength; i++ {
		if i < textLength && text[i] >= 'a' && text[i] <= 'z' {
			continue
		}
		if i > start {
			counts[string(text[start:i])]++
			total++
		}
		start = i + 1
	}

	// Top 10 by count (descending), then by word (ascending).
	var topWords [10]string
	var topCounts [10]int
	filled := 0
	for word, count := range counts {
		slot := filled
		for slot > 0 && before(word, count, topWords[slot-1], topCounts[slot-1]) {
			slot--
		}
		if slot >= 10 {
			continue
		}
		last := 9
		if filled < 10 {
			last = filled
		}
		for s := last; s > slot; s-- {
			topWords[s] = topWords[s-1]
			topCounts[s] = topCounts[s-1]
		}
		topWords[slot] = word
		topCounts[slot] = count
		if filled < 10 {
			filled++
		}
	}

	out := bufio.NewWriter(os.Stdout)
	fmt.Fprintf(out, "words %d distinct %d\n", total, len(counts))
	for s := 0; s < filled; s++ {
		fmt.Fprintf(out, "%d %s\n", topCounts[s], topWords[s])
	}
	out.Flush()
}
