using System.Diagnostics;

internal static class Program
{
    private static Measurement RunOnce(Parameters p)
    {
        int n = p.Size;
        byte[] composite = new byte[n + 1];
        long start = Stopwatch.GetTimestamp();
        for (int i = 2; i <= n / i; ++i)
            if (composite[i] == 0)
                for (int j = i * i; j <= n; j += i) composite[j] = 1;
        double seconds = Benchmark.Seconds(start);
        long count = 0, sum = 0;
        for (int i = 2; i <= n; ++i)
            if (composite[i] == 0) { ++count; sum += i; }
        return new Measurement(seconds, (double)count, (double)sum);
    }

    private static int Main() => Benchmark.Run("PrimeSieve", 200000000, false, RunOnce);
}

