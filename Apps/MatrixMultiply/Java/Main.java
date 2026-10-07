// MatrixMultiply: multiplies two generated n×n matrices of doubles with the cache-friendly i-k-j
// loop order and prints a hash of the result's bit patterns.
public final class Main {
    public static void main(String[] args) {
        int n = args.length > 0 ? Integer.parseInt(args[0]) : 1024;

        double[] a = new double[n * n];
        double[] b = new double[n * n];
        double[] c = new double[n * n];
        for (int i = 0; i < n; i++) {
            for (int j = 0; j < n; j++) {
                a[i * n + j] = ((i + 3 * j) % 17 - 8) / 8.0;
                b[i * n + j] = ((2 * i + j) % 13 - 6) / 6.0;
            }
        }

        for (int i = 0; i < n; i++) {
            for (int k = 0; k < n; k++) {
                double aik = a[i * n + k];
                for (int j = 0; j < n; j++)
                    c[i * n + j] += aik * b[k * n + j];
            }
        }

        long hash = 0xCBF29CE484222325L;
        for (double value : c)
            hash = (hash ^ Double.doubleToRawLongBits(value)) * 0x100000001B3L;
        System.out.print(n + " " + hex16(hash) + "\n");
        System.out.flush();
    }

    // Lower-case hex, zero-padded to 16 digits.
    static String hex16(long value) {
        String digits = Long.toHexString(value);
        return "0".repeat(16 - digits.length()) + digits;
    }
}
