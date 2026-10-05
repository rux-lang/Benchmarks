#include "../../../Shared/Cpp/Benchmark.hpp"

Measurement RunOnce(const Parameters &p) {
    const int n = p.size;
    std::vector<std::uint8_t> composite(n + 1, 0);
    const auto start = std::chrono::steady_clock::now();
    for (int i = 2; i <= n / i; ++i)
        if (composite[i] == 0)
            for (int j = i * i; j <= n; j += i)
                composite[j] = 1;
    const double seconds = Seconds(start);
    std::int64_t count = 0, sum = 0;
    for (int i = 2; i <= n; ++i)
        if (composite[i] == 0) {
            ++count;
            sum += i;
        }
    return {seconds, double(count), double(sum)};
}

int main() { return Run("PrimeSieve", 200000000, false, RunOnce); }
