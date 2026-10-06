namespace Runner;

sealed record Summary(int Count, double Median, double Min, double Mean, double StdDev)
{
    /// <summary>Coefficient of variation in percent: how noisy the measurement was.</summary>
    public double CvPercent => Mean == 0 ? 0 : StdDev / Mean * 100;
}

static class Stats
{
    public static Summary? Summarize(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return null;
        var sorted = values.Order().ToArray();
        int n = sorted.Length;
        double median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
        double mean = sorted.Average();
        double stdDev = n > 1 ? Math.Sqrt(sorted.Sum(v => (v - mean) * (v - mean)) / (n - 1)) : 0;
        return new Summary(n, median, sorted[0], mean, stdDev);
    }

    /// <summary>Geometric mean, the right average for ratios: 2x faster and 2x slower cancel out.</summary>
    public static double? GeometricMean(IReadOnlyList<double> ratios) =>
        ratios.Count == 0 || ratios.Any(r => r <= 0) ? null : Math.Exp(ratios.Average(Math.Log));
}
