#pragma once
#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <iomanip>
#include <iostream>
#include <locale>
#include <stdexcept>
#include <string>
#include <vector>

struct Parameters {
    int size, work, seed, warmups, samples;
};
struct Measurement {
    double seconds, sum, weighted;
};
inline Parameters ReadParameters() {
    std::array<long long, 6> v{};
    for (auto &x : v) {
        std::string token;
        if (!(std::cin >> token) || token.empty() ||
            token.find_first_not_of("0123456789") != std::string::npos)
            throw std::runtime_error("Expected six nonnegative decimal integers");
        x = std::stoll(token);
        if (x > 2147483647)
            throw std::runtime_error("Integer out of range");
    }
    std::string extra;
    if (std::cin >> extra)
        throw std::runtime_error("Unexpected input after parameters");
    if (v[0] != 1 || v[1] < 2 || v[2] < 1 || v[2] > 1000000 || v[3] > 1000000 || v[4] > 100 || v[5] < 1 || v[5] > 1000)
        throw std::runtime_error("Invalid protocol version or parameter range");
    return {int(v[1]), int(v[2]), int(v[3]), int(v[4]), int(v[5])};
}
inline double Seconds(std::chrono::steady_clock::time_point start) {
    return std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
}
template <class Function> int Run(const char *name, int maxSize, bool powerOfTwo, Function runOnce) {
    try {
        std::locale::global(std::locale::classic());
        const auto p = ReadParameters();
        if (p.size > maxSize || (powerOfTwo && (p.size & (p.size - 1))))
            throw std::runtime_error("Unsupported workload size");
        std::vector<Measurement> results;
        for (int i = 0; i < p.warmups + p.samples; ++i) {
            const auto m = runOnce(p);
            if (!std::isfinite(m.sum) || !std::isfinite(m.weighted) || !std::isfinite(m.seconds) || m.seconds < 0)
                throw std::runtime_error("Nonfinite result or invalid clock");
            if (i >= p.warmups)
                results.push_back(m);
        }
        std::cout << std::setprecision(17) << "{\"Protocol\":1,\"Benchmark\":\"" << name << "\",\"Size\":" << p.size
                  << ",\"Work\":" << p.work << ",\"Seed\":" << p.seed << ",\"Warmups\":" << p.warmups
                  << ",\"Samples\":[";
        for (std::size_t i = 0; i < results.size(); ++i) {
            if (i)
                std::cout << ',';
            const auto &m = results[i];
            std::cout << "{\"Seconds\":" << m.seconds << ",\"Sum\":" << m.sum << ",\"Weighted\":" << m.weighted << '}';
        }
        std::cout << "]}\n";
        return 0;
    } catch (const std::exception &e) {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
