#include "../../../Shared/Cpp/Benchmark.hpp"

Measurement RunOnce(const Parameters &p) {
    const int n = p.size;
    std::vector<double> real(n), imag(n, 0.0), wr(n / 2), wi(n / 2);
    for (int i = 0; i < n; ++i)
        real[i] = ((i + p.seed) % 17 - 8) / 8.0;
    for (int i = 0; i < n / 2; ++i) {
        const double angle = -6.28318530717958647692 * i / n;
        wr[i] = std::cos(angle);
        wi[i] = std::sin(angle);
    }
    const auto start = std::chrono::steady_clock::now();
    int j = 0;
    for (int i = 1; i < n; ++i) {
        int bit = n >> 1;
        while ((j & bit) != 0) {
            j ^= bit;
            bit >>= 1;
        }
        j ^= bit;
        if (i < j) {
            std::swap(real[i], real[j]);
            std::swap(imag[i], imag[j]);
        }
    }
    for (int length = 2; length <= n; length *= 2) {
        const int half = length / 2, stride = n / length;
        for (int offset = 0; offset < n; offset += length)
            for (int k = 0; k < half; ++k) {
                const int left = offset + k, right = left + half, tw = k * stride;
                const double tr = wr[tw] * real[right] - wi[tw] * imag[right];
                const double ti = wr[tw] * imag[right] + wi[tw] * real[right];
                const double lr = real[left], li = imag[left];
                real[left] = lr + tr;
                imag[left] = li + ti;
                real[right] = lr - tr;
                imag[right] = li - ti;
            }
    }
    const double seconds = Seconds(start);
    double sum = 0, weighted = 0;
    for (int i = 0; i < n; ++i) {
        sum += real[i] + imag[i];
        weighted += real[i] * (i % 7 + 1) + imag[i] * (i % 5 + 1);
    }
    return {seconds, sum, weighted};
}

int main() { return Run("FastFourierTransform", 4194304, true, RunOnce); }
