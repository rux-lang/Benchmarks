#include "../../../Shared/Cpp/Benchmark.hpp"

Measurement RunOnce(const Parameters &p) {
    const int n = p.size;
    std::vector<std::int32_t> counts(n * n, 0);
    const auto start = std::chrono::steady_clock::now();
    for (int y = 0; y < n; ++y)
        for (int x = 0; x < n; ++x) {
            const double cr = 3.0 * x / n - 2.0, ci = 2.0 * y / n - 1.0;
            double zr = 0, zi = 0;
            int count = 0;
            while (zr * zr + zi * zi <= 4.0 && count < p.work) {
                const double next = zr * zr - zi * zi + cr;
                zi = 2.0 * zr * zi + ci;
                zr = next;
                ++count;
            }
            counts[y * n + x] = count;
        }
    const double seconds = Seconds(start);
    std::int64_t sum = 0, weighted = 0;
    for (int i = 0; i < n * n; ++i) {
        sum += counts[i];
        weighted += std::int64_t(counts[i]) * (i % 7 + 1);
    }
    return {seconds, double(sum), double(weighted)};
}

int main() { return Run("Mandelbrot", 4096, false, RunOnce); }
