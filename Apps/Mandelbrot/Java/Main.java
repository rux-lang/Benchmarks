// Mandelbrot: renders the Mandelbrot set into a binary PPM image, writes it to Mandelbrot.ppm and
// prints the image size and the FNV-1a hash of the file contents.
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

public final class Main {
    public static void main(String[] args) throws IOException {
        int size = args.length > 0 ? Integer.parseInt(args[0]) : 2000;
        int maxIterations = args.length > 1 ? Integer.parseInt(args[1]) : 500;

        byte[] header = ("P6\n" + size + " " + size + "\n255\n").getBytes(StandardCharsets.US_ASCII);
        byte[] image = new byte[Math.toIntExact(header.length + 3L * size * size)];
        System.arraycopy(header, 0, image, 0, header.length);

        int offset = header.length;
        for (int py = 0; py < size; py++) {
            double ci = 3.0 * py / size - 1.5;
            for (int px = 0; px < size; px++) {
                double cr = 3.0 * px / size - 2.0;
                double zr = 0.0, zi = 0.0;
                int iteration = 0;
                while (iteration < maxIterations) {
                    double zr2 = zr * zr;
                    double zi2 = zi * zi;
                    if (zr2 + zi2 > 4.0)
                        break;
                    zi = 2.0 * zr * zi + ci;
                    zr = zr2 - zi2 + cr;
                    iteration++;
                }
                if (iteration < maxIterations) {
                    image[offset] = (byte) (iteration * 7);
                    image[offset + 1] = (byte) (iteration * 13);
                    image[offset + 2] = (byte) (iteration * 29);
                }
                offset += 3;
            }
        }

        try (FileOutputStream file = new FileOutputStream("Mandelbrot.ppm")) {
            file.write(image);
        }

        long hash = 0xCBF29CE484222325L;
        for (byte b : image)
            hash = (hash ^ (b & 0xFF)) * 0x100000001B3L;
        System.out.print(size + "x" + size + " " + hex16(hash) + "\n");
        System.out.flush();
    }

    // Lower-case hex, zero-padded to 16 digits.
    static String hex16(long value) {
        String digits = Long.toHexString(value);
        return "0".repeat(16 - digits.length()) + digits;
    }
}
