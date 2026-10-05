using System.Diagnostics;
using System.Globalization;

internal readonly record struct Parameters(int Size, int Work, int Seed, int Warmups, int Samples);
internal readonly record struct Measurement(double Seconds, double Sum, double Weighted);

internal static class Benchmark
{
    internal static int Run(string name, int maxSize, bool powerOfTwo, Func<Parameters, Measurement> runOnce)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var fields = Console.In.ReadToEnd().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 6) throw new ArgumentException("Expected six decimal integers");
            int[] v = Array.ConvertAll(fields, s => int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture));
            if (v[0] != 1 || v[1] < 2 || v[1] > maxSize || v[2] < 1 || v[2] > 1000000 ||
                v[3] < 0 || v[3] > 1000000 || v[4] < 0 || v[4] > 100 || v[5] < 1 || v[5] > 1000 ||
                (powerOfTwo && (v[1] & (v[1] - 1)) != 0))
                throw new ArgumentException("Invalid protocol version or parameter range");
            var p = new Parameters(v[1], v[2], v[3], v[4], v[5]);
            var results = new Measurement[p.Samples];
            for (int i = 0; i < p.Warmups + p.Samples; ++i)
            {
                var m = runOnce(p);
                if (!double.IsFinite(m.Sum) || !double.IsFinite(m.Weighted) ||
                    !double.IsFinite(m.Seconds) || m.Seconds < 0)
                    throw new InvalidOperationException("Nonfinite result or invalid clock");
                if (i >= p.Warmups) results[i - p.Warmups] = m;
            }
            // Explicit output avoids reflection-based serialization in Native AOT.
            Console.Write($"{{\"Protocol\":1,\"Benchmark\":\"{name}\",\"Size\":{p.Size},\"Work\":{p.Work},\"Seed\":{p.Seed},\"Warmups\":{p.Warmups},\"Samples\":[");
            for (int i = 0; i < results.Length; ++i)
            {
                if (i != 0) Console.Write(',');
                var m = results[i];
                Console.Write($"{{\"Seconds\":{m.Seconds:R},\"Sum\":{m.Sum:R},\"Weighted\":{m.Weighted:R}}}");
            }
            Console.WriteLine("]}");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
    internal static double Seconds(long start) => Stopwatch.GetElapsedTime(start).TotalSeconds;
}

