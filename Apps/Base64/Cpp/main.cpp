// Base64: encodes pseudo-random bytes as Base64 and decodes them again with a hand-written codec,
// checks the round trip, and prints the encoded length and a hash of the encoded text.
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <vector>

static const char Alphabet[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

static uint64_t SplitMix(uint64_t& state) {
    state += 0x9E3779B97F4A7C15ULL;
    uint64_t z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    return z ^ (z >> 31);
}

static void Encode(const std::vector<uint8_t>& input, std::vector<uint8_t>& output) {
    size_t full = input.size() / 3 * 3;
    size_t o = 0;
    for (size_t i = 0; i < full; i += 3) {
        uint32_t triple = (uint32_t{input[i]} << 16) | (uint32_t{input[i + 1]} << 8) | input[i + 2];
        output[o] = Alphabet[(triple >> 18) & 63];
        output[o + 1] = Alphabet[(triple >> 12) & 63];
        output[o + 2] = Alphabet[(triple >> 6) & 63];
        output[o + 3] = Alphabet[triple & 63];
        o += 4;
    }
    size_t rest = input.size() - full;
    if (rest == 1) {
        uint32_t triple = uint32_t{input[full]} << 16;
        output[o] = Alphabet[(triple >> 18) & 63];
        output[o + 1] = Alphabet[(triple >> 12) & 63];
        output[o + 2] = '=';
        output[o + 3] = '=';
    } else if (rest == 2) {
        uint32_t triple = (uint32_t{input[full]} << 16) | (uint32_t{input[full + 1]} << 8);
        output[o] = Alphabet[(triple >> 18) & 63];
        output[o + 1] = Alphabet[(triple >> 12) & 63];
        output[o + 2] = Alphabet[(triple >> 6) & 63];
        output[o + 3] = '=';
    }
}

// Returns the number of decoded bytes, or -1 for invalid input.
static int64_t Decode(const std::vector<uint8_t>& input, std::vector<uint8_t>& output, const int* table) {
    int64_t o = 0;
    for (size_t i = 0; i < input.size(); i += 4) {
        int a = table[input[i]];
        int b = table[input[i + 1]];
        if (a < 0 || b < 0) return -1;
        output[o++] = static_cast<uint8_t>((a << 2) | (b >> 4));
        if (input[i + 2] == '=') break;
        int c = table[input[i + 2]];
        if (c < 0) return -1;
        output[o++] = static_cast<uint8_t>(((b & 15) << 4) | (c >> 2));
        if (input[i + 3] == '=') break;
        int d = table[input[i + 3]];
        if (d < 0) return -1;
        output[o++] = static_cast<uint8_t>(((c & 3) << 6) | d);
    }
    return o;
}

int main(int argc, char** argv) {
    int64_t mebibytes = argc > 1 ? std::atoll(argv[1]) : 32;
    int64_t rounds = argc > 2 ? std::atoll(argv[2]) : 4;

    size_t length = static_cast<size_t>(mebibytes) * 1048576;
    std::vector<uint8_t> data(length);
    uint64_t state = 4;
    for (size_t i = 0; i < length; i += 8) {
        uint64_t value = SplitMix(state);
        for (int b = 0; b < 8; b++) data[i + b] = static_cast<uint8_t>(value >> (8 * b));
    }

    int decodeTable[256];
    for (int& entry : decodeTable) entry = -1;
    for (int i = 0; i < 64; i++) decodeTable[static_cast<uint8_t>(Alphabet[i])] = i;

    size_t encodedLength = (length + 2) / 3 * 4;
    std::vector<uint8_t> encoded(encodedLength), decoded(length);
    for (int64_t round = 0; round < rounds; round++) {
        Encode(data, encoded);
        bool same = Decode(encoded, decoded, decodeTable) == static_cast<int64_t>(length);
        for (size_t i = 0; same && i < length; i++) same = decoded[i] == data[i];
        if (!same) {
            std::printf("round trip failed\n");
            return 1;
        }
    }

    uint64_t hash = 0xCBF29CE484222325ULL;
    for (uint8_t b : encoded) hash = (hash ^ b) * 0x100000001B3ULL;
    std::printf("%zu %016llx\n", encodedLength, static_cast<unsigned long long>(hash));
    return 0;
}
