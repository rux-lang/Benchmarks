using System.Diagnostics;

internal static class Program
{
    private static Measurement RunOnce(Parameters p)
    {
        int n = p.Size;
        double[] x = new double[n], y = new double[n], z = new double[n], vx = new double[n], vy = new double[n], vz = new double[n];
        for (int i = 0; i < n; ++i) {
            x[i] = (i % 16 - 8) + p.Seed / 1024.0;
            y[i] = ((i / 16) % 16 - 8);
            z[i] = (i / 256 - 4);
        }
        long start = Stopwatch.GetTimestamp();
        for (int step = 0; step < p.Work; ++step) {
            for (int i = 0; i < n; ++i)
                for (int j = i + 1; j < n; ++j) {
                    double dx = x[j] - x[i], dy = y[j] - y[i], dz = z[j] - z[i];
                    double distance = dx * dx + dy * dy + dz * dz + 0.01;
                    double inverse = 1.0 / Math.Sqrt(distance);
                    double scale = 0.001 * inverse * inverse * inverse / n;
                    vx[i] += dx * scale; vy[i] += dy * scale; vz[i] += dz * scale;
                    vx[j] -= dx * scale; vy[j] -= dy * scale; vz[j] -= dz * scale;
                }
            for (int i = 0; i < n; ++i) {
                x[i] += 0.001 * vx[i]; y[i] += 0.001 * vy[i]; z[i] += 0.001 * vz[i];
            }
        }
        double seconds = Benchmark.Seconds(start);
        double sum = 0, weighted = 0;
        for (int i = 0; i < n; ++i) {
            double value = x[i] + 2 * y[i] + 3 * z[i] + 4 * vx[i] + 5 * vy[i] + 6 * vz[i];
            sum += value;
            weighted += value * (i % 7 + 1);
        }
        return new Measurement(seconds, sum, weighted);
    }

    private static int Main() => Benchmark.Run("NBody", 8192, false, RunOnce);
}

