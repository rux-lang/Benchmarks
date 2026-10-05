#include "../../../Shared/Cpp/Benchmark.hpp"

Measurement RunOnce(const Parameters &p) {
    const int n = p.size;
    std::vector<double> a(n * n), b(n * n), c(n * n, 0.0);
    for (int i = 0; i < n; ++i)
        for (int j = 0; j < n; ++j) {
            a[i * n + j] = ((i + 3 * j + p.seed) % 17 - 8) / 8.0;
            b[i * n + j] = ((5 * i + j + p.seed) % 13 - 6) / 8.0;
        }
    const auto start = std::chrono::steady_clock::now();
    for (int i = 0; i < n; ++i)
        for (int k = 0; k < n; ++k) {
            const double value = a[i * n + k];
            for (int j = 0; j < n; ++j)
                c[i * n + j] += value * b[k * n + j];
        }
    const double seconds = Seconds(start);
    double sum = 0, weighted = 0;
    for (int i = 0; i < n * n; ++i) {
        sum += c[i];
        weighted += c[i] * (i % 7 + 1);
    }
    return {seconds, sum, weighted};
}

int main() { return Run("MatrixMultiply", 2048, false, RunOnce); }
