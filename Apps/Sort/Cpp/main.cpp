// Sort: sorts pseudo-random 32-bit integers with a hand-written quicksort, checks the order and
// prints a hash of the sorted array.
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <utility>
#include <vector>

static uint64_t SplitMix(uint64_t& state) {
    state += 0x9E3779B97F4A7C15ULL;
    uint64_t z = state;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    return z ^ (z >> 31);
}

// Sorts values[low..=high]: median-of-three quicksort that recurses into the smaller part and
// leaves ranges of fewer than 16 elements to insertion sort.
static void QuickSort(std::vector<int32_t>& values, int64_t low, int64_t high) {
    while (high - low >= 16) {
        int64_t middle = low + (high - low) / 2;
        if (values[middle] < values[low]) std::swap(values[low], values[middle]);
        if (values[high] < values[low]) std::swap(values[low], values[high]);
        if (values[high] < values[middle]) std::swap(values[middle], values[high]);
        int32_t pivot = values[middle];
        int64_t i = low, j = high;
        while (i <= j) {
            while (values[i] < pivot) i++;
            while (values[j] > pivot) j--;
            if (i <= j) {
                std::swap(values[i], values[j]);
                i++;
                j--;
            }
        }
        if (j - low < high - i) {
            QuickSort(values, low, j);
            low = i;
        } else {
            QuickSort(values, i, high);
            high = j;
        }
    }
    for (int64_t i = low + 1; i <= high; i++) {
        int32_t value = values[i];
        int64_t j = i - 1;
        while (j >= low && values[j] > value) {
            values[j + 1] = values[j];
            j--;
        }
        values[j + 1] = value;
    }
}

int main(int argc, char** argv) {
    int64_t count = argc > 1 ? std::atoll(argv[1]) : 10000000;

    uint64_t state = 3;
    std::vector<int32_t> values(static_cast<size_t>(count));
    for (int64_t i = 0; i < count; i++) values[i] = static_cast<int32_t>(static_cast<uint32_t>(SplitMix(state) >> 32));

    QuickSort(values, 0, count - 1);

    uint64_t hash = 0xCBF29CE484222325ULL;
    for (int64_t i = 0; i < count; i++) {
        if (i > 0 && values[i - 1] > values[i]) {
            std::printf("unsorted\n");
            return 1;
        }
        hash = (hash ^ static_cast<uint32_t>(values[i])) * 0x100000001B3ULL;
    }
    std::printf("%lld %016llx\n", static_cast<long long>(count), static_cast<unsigned long long>(hash));
    return 0;
}
