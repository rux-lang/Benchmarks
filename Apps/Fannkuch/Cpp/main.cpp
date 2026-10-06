// Fannkuch: for every permutation of 0..n-1, counts how many prefix reversals ("pancake flips")
// it takes until 0 comes first (single-threaded fannkuch-redux from the Benchmarks Game).
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <utility>
#include <vector>

int main(int argc, char** argv) {
    int n = argc > 1 ? std::atoi(argv[1]) : 10;

    std::vector<int> permutation(n), current(n), counters(n);
    for (int i = 0; i < n; i++) current[i] = i;

    int maxFlips = 0;
    int64_t checksum = 0;
    int64_t permutationIndex = 0;
    int r = n;
    while (true) {
        while (r != 1) {
            counters[r - 1] = r;
            r--;
        }

        for (int i = 0; i < n; i++) permutation[i] = current[i];
        int flips = 0;
        int first = permutation[0];
        while (first != 0) {
            for (int low = 0, high = first; low < high; low++, high--) std::swap(permutation[low], permutation[high]);
            flips++;
            first = permutation[0];
        }
        if (flips > maxFlips) maxFlips = flips;
        checksum += permutationIndex % 2 == 0 ? flips : -flips;

        // Next permutation in the order used by the reference program.
        while (true) {
            if (r == n) {
                std::printf("%lld\nPfannkuchen(%d) = %d\n", static_cast<long long>(checksum), n, maxFlips);
                return 0;
            }
            int rotated = current[0];
            for (int i = 0; i < r; i++) current[i] = current[i + 1];
            current[r] = rotated;
            counters[r]--;
            if (counters[r] > 0) break;
            r++;
        }
        permutationIndex++;
    }
}
