using System.Diagnostics;

internal static class Program
{
    private static Measurement RunOnce(Parameters p)
    {
        int n = p.Size;
        double[] a = new double[n * n], b = new double[n * n], c = new double[n * n];
        for (int i = 0; i < n; ++i)
            for (int j = 0; j < n; ++j) {
                a[i * n + j] = ((i + 3 * j + p.Seed) % 17 - 8) / 8.0;
                b[i * n + j] = ((5 * i + j + p.Seed) % 13 - 6) / 8.0;
            }
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; ++i)
            for (int k = 0; k < n; ++k) {
                double value = a[i * n + k];
                for (int j = 0; j < n; ++j) c[i * n + j] += value * b[k * n + j];
            }
        double seconds = Benchmark.Seconds(start);
        double sum = 0, weighted = 0;
        for (int i = 0; i < n * n; ++i) {
            sum += c[i];
            weighted += c[i] * (i % 7 + 1);
        }
        return new Measurement(seconds, sum, weighted);
    }

    private static int Main() => Benchmark.Run("MatrixMultiply", 2048, false, RunOnce);
}

