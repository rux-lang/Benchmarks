// Sha512: fills a buffer with pseudo-random bytes and hashes it with a hand-written SHA-512.
// Each further round writes the previous digest into the first 64 bytes and hashes again.
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

static const uint64_t K[80] = {
    0x428A2F98D728AE22ULL, 0x7137449123EF65CDULL, 0xB5C0FBCFEC4D3B2FULL, 0xE9B5DBA58189DBBCULL,
    0x3956C25BF348B538ULL, 0x59F111F1B605D019ULL, 0x923F82A4AF194F9BULL, 0xAB1C5ED5DA6D8118ULL,
    0xD807AA98A3030242ULL, 0x12835B0145706FBEULL, 0x243185BE4EE4B28CULL, 0x550C7DC3D5FFB4E2ULL,
    0x72BE5D74F27B896FULL, 0x80DEB1FE3B1696B1ULL, 0x9BDC06A725C71235ULL, 0xC19BF174CF692694ULL,
    0xE49B69C19EF14AD2ULL, 0xEFBE4786384F25E3ULL, 0x0FC19DC68B8CD5B5ULL, 0x240CA1CC77AC9C65ULL,
    0x2DE92C6F592B0275ULL, 0x4A7484AA6EA6E483ULL, 0x5CB0A9DCBD41FBD4ULL, 0x76F988DA831153B5ULL,
    0x983E5152EE66DFABULL, 0xA831C66D2DB43210ULL, 0xB00327C898FB213FULL, 0xBF597FC7BEEF0EE4ULL,
    0xC6E00BF33DA88FC2ULL, 0xD5A79147930AA725ULL, 0x06CA6351E003826FULL, 0x142929670A0E6E70ULL,
    0x27B70A8546D22FFCULL, 0x2E1B21385C26C926ULL, 0x4D2C6DFC5AC42AEDULL, 0x53380D139D95B3DFULL,
    0x650A73548BAF63DEULL, 0x766A0ABB3C77B2A8ULL, 0x81C2C92E47EDAEE6ULL, 0x92722C851482353BULL,
    0xA2BFE8A14CF10364ULL, 0xA81A664BBC423001ULL, 0xC24B8B70D0F89791ULL, 0xC76C51A30654BE30ULL,
    0xD192E819D6EF5218ULL, 0xD69906245565A910ULL, 0xF40E35855771202AULL, 0x106AA07032BBD1B8ULL,
    0x19A4C116B8D2D0C8ULL, 0x1E376C085141AB53ULL, 0x2748774CDF8EEB99ULL, 0x34B0BCB5E19B48A8ULL,
    0x391C0CB3C5C95A63ULL, 0x4ED8AA4AE3418ACBULL, 0x5B9CCA4F7763E373ULL, 0x682E6FF3D6B2B8A3ULL,
    0x748F82EE5DEFB2FCULL, 0x78A5636F43172F60ULL, 0x84C87814A1F0AB72ULL, 0x8CC702081A6439ECULL,
    0x90BEFFFA23631E28ULL, 0xA4506CEBDE82BDE9ULL, 0xBEF9A3F7B2C67915ULL, 0xC67178F2E372532BULL,
    0xCA273ECEEA26619CULL, 0xD186B8C721C0C207ULL, 0xEADA7DD6CDE0EB1EULL, 0xF57D4F7FEE6ED178ULL,
    0x06F067AA72176FBAULL, 0x0A637DC5A2C898A6ULL, 0x113F9804BEF90DAEULL, 0x1B710B35131C471BULL,
    0x28DB77F523047D84ULL, 0x32CAAB7B40C72493ULL, 0x3C9EBE0A15C9BEBCULL, 0x431D67C49C100D4CULL,
    0x4CC5D4BECB3E42B6ULL, 0x597F299CFC657E2AULL, 0x5FCB6FAB3AD6FAECULL, 0x6C44198C4A475817ULL
};

static uint64_t SplitMix(uint64_t& state) {
    state += 0x9E3779B97F4A7C15ULL;
    uint64_t z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    return z ^ (z >> 31);
}

static uint64_t RotateRight(uint64_t x, int n) { return (x >> n) | (x << (64 - n)); }

static uint64_t LoadBigEndian(const uint8_t* data) {
    uint64_t value = 0;
    for (int b = 0; b < 8; b++) value = (value << 8) | data[b];
    return value;
}

static void Compress(uint64_t* h, uint64_t* w, const uint8_t* block) {
    for (int t = 0; t < 16; t++) w[t] = LoadBigEndian(block + 8 * t);
    for (int t = 16; t < 80; t++) {
        uint64_t s0 = RotateRight(w[t - 15], 1) ^ RotateRight(w[t - 15], 8) ^ (w[t - 15] >> 7);
        uint64_t s1 = RotateRight(w[t - 2], 19) ^ RotateRight(w[t - 2], 61) ^ (w[t - 2] >> 6);
        w[t] = w[t - 16] + s0 + w[t - 7] + s1;
    }
    uint64_t a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
    for (int t = 0; t < 80; t++) {
        uint64_t s1 = RotateRight(e, 14) ^ RotateRight(e, 18) ^ RotateRight(e, 41);
        uint64_t ch = (e & f) ^ (~e & g);
        uint64_t t1 = hh + s1 + ch + K[t] + w[t];
        uint64_t s0 = RotateRight(a, 28) ^ RotateRight(a, 34) ^ RotateRight(a, 39);
        uint64_t maj = (a & b) ^ (a & c) ^ (b & c);
        uint64_t t2 = s0 + maj;
        hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
    }
    h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
}

static std::array<uint8_t, 64> Hash(const std::vector<uint8_t>& data) {
    uint64_t h[8] = {
        0x6A09E667F3BCC908ULL, 0xBB67AE8584CAA73BULL, 0x3C6EF372FE94F82BULL, 0xA54FF53A5F1D36F1ULL,
        0x510E527FADE682D1ULL, 0x9B05688C2B3E6C1FULL, 0x1F83D9ABFB41BD6BULL, 0x5BE0CD19137E2179ULL
    };
    uint64_t w[80];
    size_t blocks = data.size() / 128;
    for (size_t i = 0; i < blocks; i++) Compress(h, w, data.data() + i * 128);

    // Padding: 0x80, zeros, then the message length in bits as a 128-bit big-endian number.
    uint8_t tail[256] = {};
    size_t rest = data.size() - blocks * 128;
    for (size_t i = 0; i < rest; i++) tail[i] = data[blocks * 128 + i];
    tail[rest] = 0x80;
    size_t tailLength = rest < 112 ? 128 : 256;
    uint64_t bits = static_cast<uint64_t>(data.size()) * 8;
    for (int b = 0; b < 8; b++) tail[tailLength - 1 - b] = static_cast<uint8_t>(bits >> (8 * b));
    for (size_t offset = 0; offset < tailLength; offset += 128) Compress(h, w, tail + offset);

    std::array<uint8_t, 64> digest;
    for (int i = 0; i < 8; i++)
        for (int b = 0; b < 8; b++) digest[8 * i + b] = static_cast<uint8_t>(h[i] >> (56 - 8 * b));
    return digest;
}

int main(int argc, char** argv) {
    long long mebibytes = argc > 1 ? std::atoll(argv[1]) : 16;
    long long rounds = argc > 2 ? std::atoll(argv[2]) : 16;

    std::vector<uint8_t> buffer(static_cast<size_t>(mebibytes) * 1048576);
    uint64_t state = 1;
    for (size_t i = 0; i < buffer.size(); i += 8) {
        uint64_t value = SplitMix(state);
        for (int b = 0; b < 8; b++) buffer[i + b] = static_cast<uint8_t>(value >> (8 * b));
    }

    std::array<uint8_t, 64> digest = Hash(buffer);
    for (long long r = 1; r < rounds; r++) {
        std::memcpy(buffer.data(), digest.data(), 64);
        digest = Hash(buffer);
    }
    for (uint8_t b : digest) std::printf("%02x", b);
    std::printf("\n");
    return 0;
}
