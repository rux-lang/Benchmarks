// Mandelbrot: renders the Mandelbrot set into a binary PPM image, writes it to Mandelbrot.ppm and
// prints the image size and the FNV-1a hash of the file contents.
#define _CRT_SECURE_NO_WARNINGS  // fopen is standard C; MSVC CRT marks it deprecated
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <vector>

int main(int argc, char** argv) {
    int size = argc > 1 ? std::atoi(argv[1]) : 2000;
    int maxIterations = argc > 2 ? std::atoi(argv[2]) : 500;

    std::string header = "P6\n" + std::to_string(size) + " " + std::to_string(size) + "\n255\n";
    std::vector<uint8_t> image(header.size() + 3 * static_cast<size_t>(size) * size);
    for (size_t i = 0; i < header.size(); i++) image[i] = static_cast<uint8_t>(header[i]);

    size_t offset = header.size();
    for (int py = 0; py < size; py++) {
        double ci = 3.0 * py / size - 1.5;
        for (int px = 0; px < size; px++) {
            double cr = 3.0 * px / size - 2.0;
            double zr = 0.0, zi = 0.0;
            int iteration = 0;
            while (iteration < maxIterations) {
                double zr2 = zr * zr;
                double zi2 = zi * zi;
                if (zr2 + zi2 > 4.0) break;
                zi = 2.0 * zr * zi + ci;
                zr = zr2 - zi2 + cr;
                iteration++;
            }
            if (iteration < maxIterations) {
                image[offset] = static_cast<uint8_t>(iteration * 7);
                image[offset + 1] = static_cast<uint8_t>(iteration * 13);
                image[offset + 2] = static_cast<uint8_t>(iteration * 29);
            }
            offset += 3;
        }
    }

    FILE* file = std::fopen("Mandelbrot.ppm", "wb");
    if (file == nullptr || std::fwrite(image.data(), 1, image.size(), file) != image.size()) {
        std::fprintf(stderr, "cannot write Mandelbrot.ppm\n");
        return 1;
    }
    std::fclose(file);

    uint64_t hash = 0xCBF29CE484222325ULL;
    for (uint8_t b : image) hash = (hash ^ b) * 0x100000001B3ULL;
    std::printf("%dx%d %016llx\n", size, size, static_cast<unsigned long long>(hash));
    return 0;
}
