// WordCount: generates a pseudo-random text from a pseudo-random vocabulary, counts how often each
// word occurs with a hash map, and prints the 10 most frequent words.
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

static uint64_t SplitMix(uint64_t& state) {
    state += 0x9E3779B97F4A7C15ULL;
    uint64_t z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    return z ^ (z >> 31);
}

static uint64_t PickWord(uint64_t& state, uint64_t vocabularySize) {
    uint64_t a = SplitMix(state) % vocabularySize;
    uint64_t b = SplitMix(state) % vocabularySize;
    return a < b ? a : b;
}

static bool Before(std::string_view word, uint32_t count, std::string_view otherWord, uint32_t otherCount) {
    return count != otherCount ? count > otherCount : word < otherWord;
}

int main(int argc, char** argv) {
    uint64_t wordCount = argc > 1 ? std::strtoull(argv[1], nullptr, 10) : 10000000;
    uint64_t vocabularySize = argc > 2 ? std::strtoull(argv[2], nullptr, 10) : 100000;

    uint64_t state = 2;

    // Vocabulary: words of 2..12 letters, stored back to back.
    std::vector<char> vocabulary;
    std::vector<size_t> starts(vocabularySize + 1);
    for (uint64_t k = 0; k < vocabularySize; k++) {
        starts[k] = vocabulary.size();
        uint64_t length = 2 + SplitMix(state) % 11;
        for (uint64_t i = 0; i < length; i++) vocabulary.push_back(static_cast<char>('a' + SplitMix(state) % 26));
    }
    starts[vocabularySize] = vocabulary.size();

    // Text: the generator runs twice, first to measure the text and then to write it.
    uint64_t textState = state;
    size_t textLength = 0;
    for (uint64_t i = 0; i < wordCount; i++) {
        uint64_t word = PickWord(state, vocabularySize);
        textLength += starts[word + 1] - starts[word] + 1;
    }
    state = textState;
    std::string text(textLength, ' ');
    size_t position = 0;
    for (uint64_t i = 0; i < wordCount; i++) {
        uint64_t word = PickWord(state, vocabularySize);
        for (size_t j = starts[word]; j < starts[word + 1]; j++) text[position++] = vocabulary[j];
        text[position++] = (i + 1) % 16 == 0 ? '\n' : ' ';
    }

    // Count words: maximal runs of the letters a..z.
    std::unordered_map<std::string_view, uint32_t> counts;
    uint64_t total = 0;
    size_t start = 0;
    for (size_t i = 0; i <= textLength; i++) {
        if (i < textLength && text[i] >= 'a' && text[i] <= 'z') continue;
        if (i > start) {
            ++counts[std::string_view(text.data() + start, i - start)];
            total++;
        }
        start = i + 1;
    }

    // Top 10 by count (descending), then by word (ascending).
    std::string_view topWords[10];
    uint32_t topCounts[10] = {};
    int filled = 0;
    for (const auto& [word, count] : counts) {
        int slot = filled;
        while (slot > 0 && Before(word, count, topWords[slot - 1], topCounts[slot - 1])) slot--;
        if (slot >= 10) continue;
        int last = filled < 10 ? filled : 9;
        for (int s = last; s > slot; s--) {
            topWords[s] = topWords[s - 1];
            topCounts[s] = topCounts[s - 1];
        }
        topWords[slot] = word;
        topCounts[slot] = count;
        if (filled < 10) filled++;
    }

    std::printf("words %llu distinct %zu\n", static_cast<unsigned long long>(total), counts.size());
    for (int s = 0; s < filled; s++)
        std::printf("%u %.*s\n", topCounts[s], static_cast<int>(topWords[s].size()), topWords[s].data());
    return 0;
}
