#include "../../../Shared/Cpp/Benchmark.hpp"

Measurement RunOnce(const Parameters &p) {
    const int n = p.size;
    std::vector<double> x(n), y(n), z(n), vx(n, 0.0), vy(n, 0.0), vz(n, 0.0);
    for (int i = 0; i < n; ++i) {
        x[i] = (i % 16 - 8) + p.seed / 1024.0;
        y[i] = ((i / 16) % 16 - 8);
        z[i] = (i / 256 - 4);
    }
    const auto start = std::chrono::steady_clock::now();
    for (int step = 0; step < p.work; ++step) {
        for (int i = 0; i < n; ++i)
            for (int j = i + 1; j < n; ++j) {
                const double dx = x[j] - x[i], dy = y[j] - y[i], dz = z[j] - z[i];
                const double distance = dx * dx + dy * dy + dz * dz + 0.01;
                const double inverse = 1.0 / std::sqrt(distance);
                const double scale = 0.001 * inverse * inverse * inverse / n;
                vx[i] += dx * scale;
                vy[i] += dy * scale;
                vz[i] += dz * scale;
                vx[j] -= dx * scale;
                vy[j] -= dy * scale;
                vz[j] -= dz * scale;
            }
        for (int i = 0; i < n; ++i) {
            x[i] += 0.001 * vx[i];
            y[i] += 0.001 * vy[i];
            z[i] += 0.001 * vz[i];
        }
    }
    const double seconds = Seconds(start);
    double sum = 0, weighted = 0;
    for (int i = 0; i < n; ++i) {
        const double value = x[i] + 2 * y[i] + 3 * z[i] + 4 * vx[i] + 5 * vy[i] + 6 * vz[i];
        sum += value;
        weighted += value * (i % 7 + 1);
    }
    return {seconds, sum, weighted};
}

int main() { return Run("NBody", 8192, false, RunOnce); }
