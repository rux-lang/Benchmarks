// MatrixMultiply: multiplies two generated n×n matrices of doubles with the cache-friendly i-k-j
// loop order and prints a hash of the result's bit patterns.
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

int main(int argc, char** argv) {
    int n = argc > 1 ? std::atoi(argv[1]) : 1024;
    size_t cells = static_cast<size_t>(n) * n;

    std::vector<double> a(cells), b(cells), c(cells);
    for (int i = 0; i < n; i++) {
        for (int j = 0; j < n; j++) {
            a[static_cast<size_t>(i) * n + j] = ((i + 3 * j) % 17 - 8) / 8.0;
            b[static_cast<size_t>(i) * n + j] = ((2 * i + j) % 13 - 6) / 6.0;
        }
    }

    for (int i = 0; i < n; i++) {
        for (int k = 0; k < n; k++) {
            double aik = a[static_cast<size_t>(i) * n + k];
            for (int j = 0; j < n; j++) c[static_cast<size_t>(i) * n + j] += aik * b[static_cast<size_t>(k) * n + j];
        }
    }

    uint64_t hash = 0xCBF29CE484222325ULL;
    for (double value : c) {
        uint64_t bits;
        std::memcpy(&bits, &value, sizeof bits);
        hash = (hash ^ bits) * 0x100000001B3ULL;
    }
    std::printf("%d %016llx\n", n, static_cast<unsigned long long>(hash));
    return 0;
}
