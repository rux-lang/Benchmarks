// PrimeSieve: finds every prime up to n with the sieve of Eratosthenes over a byte array and prints
// how many there are and their sum.
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <vector>

int main(int argc, char** argv) {
    int64_t limit = argc > 1 ? std::atoll(argv[1]) : 100000000;

    std::vector<uint8_t> composite(static_cast<size_t>(limit) + 1);
    for (int64_t i = 2; i * i <= limit; i++) {
        if (composite[i] != 0) continue;
        for (int64_t j = i * i; j <= limit; j += i) composite[j] = 1;
    }

    int64_t count = 0;
    int64_t sum = 0;
    for (int64_t k = 2; k <= limit; k++) {
        if (composite[k] == 0) {
            count++;
            sum += k;
        }
    }
    std::printf("%lld %lld\n", static_cast<long long>(count), static_cast<long long>(sum));
    return 0;
}
