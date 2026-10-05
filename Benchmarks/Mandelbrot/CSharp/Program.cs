using System.Diagnostics;

internal static class Program
{
    private static Measurement RunOnce(Parameters p)
    {
        int n = p.Size;
        int[] counts = new int[n * n];
        long start = Stopwatch.GetTimestamp();
        for (int y = 0; y < n; ++y)
            for (int x = 0; x < n; ++x) {
                double cr = 3.0 * x / n - 2.0, ci = 2.0 * y / n - 1.0;
                double zr = 0, zi = 0;
                int count = 0;
                while (zr * zr + zi * zi <= 4.0 && count < p.Work) {
                    double next = zr * zr - zi * zi + cr;
                    zi = 2.0 * zr * zi + ci;
                    zr = next;
                    ++count;
                }
                counts[y * n + x] = count;
            }
        double seconds = Benchmark.Seconds(start);
        long sum = 0, weighted = 0;
        for (int i = 0; i < n * n; ++i) {
            sum += counts[i];
            weighted += (long)counts[i] * (i % 7 + 1);
        }
        return new Measurement(seconds, (double)sum, (double)weighted);
    }

    private static int Main() => Benchmark.Run("Mandelbrot", 4096, false, RunOnce);
}

