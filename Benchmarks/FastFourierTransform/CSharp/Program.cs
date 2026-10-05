using System.Diagnostics;

internal static class Program
{
    private static Measurement RunOnce(Parameters p)
    {
        int n = p.Size;
        double[] real = new double[n], imag = new double[n], wr = new double[n / 2], wi = new double[n / 2];
        for (int i = 0; i < n; ++i) real[i] = ((i + p.Seed) % 17 - 8) / 8.0;
        for (int i = 0; i < n / 2; ++i) {
            double angle = -6.28318530717958647692 * i / n;
            wr[i] = Math.Cos(angle);
            wi[i] = Math.Sin(angle);
        }
        long start = Stopwatch.GetTimestamp();
        int j = 0;
        for (int i = 1; i < n; ++i) {
            int bit = n >> 1;
            while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
            j ^= bit;
            if (i < j) {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }
        for (int length = 2; length <= n; length *= 2) {
            int half = length / 2, stride = n / length;
            for (int offset = 0; offset < n; offset += length)
                for (int k = 0; k < half; ++k) {
                    int left = offset + k, right = left + half, tw = k * stride;
                    double tr = wr[tw] * real[right] - wi[tw] * imag[right];
                    double ti = wr[tw] * imag[right] + wi[tw] * real[right];
                    double lr = real[left], li = imag[left];
                    real[left] = lr + tr; imag[left] = li + ti;
                    real[right] = lr - tr; imag[right] = li - ti;
                }
        }
        double seconds = Benchmark.Seconds(start);
        double sum = 0, weighted = 0;
        for (int i = 0; i < n; ++i) {
            sum += real[i] + imag[i];
            weighted += real[i] * (i % 7 + 1) + imag[i] * (i % 5 + 1);
        }
        return new Measurement(seconds, sum, weighted);
    }

    private static int Main() => Benchmark.Run("FastFourierTransform", 4194304, true, RunOnce);
}

