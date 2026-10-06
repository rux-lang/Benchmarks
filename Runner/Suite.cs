using System.Security.Cryptography;

namespace Runner;

/// <summary>Builds and runs the apps, validating every run, and fills in the results.</summary>
sealed class Suite(BenchConfig config, Options options, Results results, string resultsDir)
{
    readonly int timeout = options.TimeoutSeconds ?? config.Defaults.TimeoutSeconds;

    /// <summary>Untimed restore and warm-up build, then N clean builds that are timed.</summary>
    public void Build(AppSpec app, Toolchain toolchain)
    {
        var result = results.Case(app.Name, toolchain.Name);
        try
        {
            if (toolchain.Prepare(app) is Command prepare)
                ProcessRunner.RunTool(prepare, timeout);
            toolchain.Clean(app);
            ProcessRunner.RunTool(toolchain.Build(app), timeout);
            for (int i = 0; i < options.BuildRuns; i++)
            {
                toolchain.Clean(app);
                result.BuildSeconds.Add(ProcessRunner.RunTool(toolchain.Build(app), timeout).Seconds);
            }
            result.ExecutableBytes = new FileInfo(toolchain.Executable(app)).Length;
            result.DeployableBytes = toolchain.DeployFiles(app).Sum(f => new FileInfo(f).Length);
            Console.WriteLine($"  {app.Name,-15}{toolchain.Display,-8} build {string.Join("  ", result.BuildSeconds.Select(s => $"{s,6:0.00} s"))}   exe {result.ExecutableBytes / 1024,6:0} KiB");
        }
        catch (Exception e)
        {
            result.Fail("build failed: " + e.Message);
            Console.WriteLine($"  {app.Name,-15}{toolchain.Display,-8} BUILD FAILED  {FirstLine(e.Message)}");
        }
    }

    /// <summary>Builds once, untimed, when the program is missing (for `run` without `build`).</summary>
    bool EnsureBuilt(AppSpec app, Toolchain toolchain, CaseResult result)
    {
        if (File.Exists(toolchain.Executable(app)))
            return true;
        try
        {
            if (toolchain.Prepare(app) is Command prepare)
                ProcessRunner.RunTool(prepare, timeout);
            ProcessRunner.RunTool(toolchain.Build(app), timeout);
            return true;
        }
        catch (Exception e)
        {
            result.Fail("build failed: " + e.Message);
            Console.WriteLine($"  {app.Name,-15}{toolchain.Display,-8} BUILD FAILED  {FirstLine(e.Message)}");
            return false;
        }
    }

    /// <summary>
    /// Runs every language round-robin (warm-ups first, then measured runs) so slow drift in
    /// machine state affects all languages alike. Every run's output is checked.
    /// </summary>
    public void Run(AppSpec app, IReadOnlyList<Toolchain> toolchains)
    {
        var profile = app.Profiles.TryGetValue(options.Profile, out var p)
            ? p : throw new InvalidOperationException($"{app.Name} has no profile '{options.Profile}'");
        var active = new List<(Toolchain Toolchain, CaseResult Result, string WorkDir)>();
        foreach (var toolchain in toolchains)
        {
            var result = results.Case(app.Name, toolchain.Name);
            result.Args = profile.Args;
            if (!result.Ok || !EnsureBuilt(app, toolchain, result))
                continue;
            string workDir = Path.Combine(resultsDir, "work", app.Name, toolchain.Name);
            Directory.CreateDirectory(workDir);
            active.Add((toolchain, result, workDir));
        }

        for (int round = 0; round < options.Warmups + options.Runs; round++)
        {
            foreach (var (toolchain, result, workDir) in active.Where(a => a.Result.Ok))
            {
                try
                {
                    var sample = ProcessRunner.Measure(toolchain.Executable(app), profile.Args, workDir, timeout);
                    string output = Validate(app, profile, sample, workDir);
                    result.Output ??= output;
                    if (round >= options.Warmups)
                        result.Runs.Add(new RunMeasurement(sample.WallMs, sample.UserMs, sample.SysMs, sample.PeakBytes));
                }
                catch (Exception e)
                {
                    result.Fail(e.Message);
                }
            }
        }

        CheckAgreement(app, profile, active.Select(a => a.Result).ToList());
        foreach (var (toolchain, result, _) in active)
        {
            if (result.Ok)
            {
                var time = Stats.Summarize(Metric.All[0].Values(result))!;
                var memory = Stats.Summarize(Metric.All[2].Values(result))!;
                Console.WriteLine($"  {app.Name,-15}{toolchain.Display,-8} {time.Median,8:0.000} s  ±{time.CvPercent,4:0.0}%   peak {memory.Median,7:0.0} MiB");
            }
            else
            {
                Console.WriteLine($"  {app.Name,-15}{toolchain.Display,-8} FAILED  {FirstLine(result.Error ?? "")}");
            }
        }
    }

    /// <summary>Returns the normalized output, or throws when the run is not correct.</summary>
    static string Validate(AppSpec app, ProfileSpec profile, RunSample sample, string workDir)
    {
        string output = Normalize(sample.Stdout);
        if (sample.ExitCode != 0)
            throw new InvalidOperationException($"exit code {sample.ExitCode}: {FirstLine(sample.Stderr + output)}");
        if (profile.Expected.Length > 0 && output != Normalize(profile.Expected))
            throw new InvalidOperationException($"unexpected output: {FirstLine(output)}");
        if (app.OutputFile != null)
        {
            string file = Path.Combine(workDir, app.OutputFile);
            if (!File.Exists(file))
                throw new InvalidOperationException($"{app.OutputFile} was not written");
            string hash = Fnv1a(File.ReadAllBytes(file)).ToString("x16");
            File.Delete(file);
            if (!output.Contains(hash))
                throw new InvalidOperationException($"{app.OutputFile} does not match the printed hash");
        }
        return output;
    }

    /// <summary>
    /// When app.json has no expected output yet, all languages must agree with each other (and
    /// with an independent .NET reference where one exists). --update-expected stores the result.
    /// </summary>
    void CheckAgreement(AppSpec app, ProfileSpec profile, List<CaseResult> cases)
    {
        var ok = cases.Where(c => c.Ok && c.Output != null).ToList();
        if (profile.Expected.Length > 0 || ok.Count == 0)
            return;
        var majority = ok.GroupBy(c => c.Output!).OrderByDescending(g => g.Count()).First();
        foreach (var c in ok.Where(c => c.Output != majority.Key))
            c.Fail($"output differs from the other languages: {FirstLine(c.Output!)}");

        string? reference = References.Compute(app.Name, profile.Args);
        if (reference != null && reference != majority.Key)
        {
            foreach (var c in majority)
                c.Fail("output differs from the .NET reference implementation");
            Console.WriteLine($"  {app.Name}: outputs disagree with the .NET reference:\n{reference}");
            return;
        }
        if (options.UpdateExpected && majority.Count() >= 2)
        {
            Config.SaveExpected(app, options.Profile, majority.Key);
            Console.WriteLine($"  {app.Name}: saved expected output ({majority.Count()} languages agree{(reference != null ? ", matches .NET reference" : "")})");
        }
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();

    static string FirstLine(string text) => text.Trim().Split('\n')[0].Trim();

    public static ulong Fnv1a(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 0xCBF29CE484222325;
        foreach (byte b in bytes)
            hash = (hash ^ b) * 0x100000001B3;
        return hash;
    }
}

/// <summary>
/// Independent implementations from the .NET libraries (not hand-written), used to confirm that
/// the hand-written apps compute the right thing.
/// </summary>
static class References
{
    static ulong SplitMix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    static byte[] RandomBytes(ulong seed, long length)
    {
        var bytes = new byte[length];
        for (long i = 0; i < length; i += 8)
            BitConverter.TryWriteBytes(bytes.AsSpan((int)i), SplitMix(ref seed));
        return bytes;
    }

    public static string? Compute(string app, IReadOnlyList<string> args)
    {
        long Arg(int i) => long.Parse(args[i]);
        switch (app)
        {
            case "Sha512":
            {
                var buffer = RandomBytes(1, Arg(0) * 1048576);
                byte[] digest = SHA512.HashData(buffer);
                for (long r = 1; r < Arg(1); r++)
                {
                    digest.CopyTo(buffer, 0);
                    digest = SHA512.HashData(buffer);
                }
                return Convert.ToHexStringLower(digest);
            }
            case "Base64":
            {
                byte[] encoded = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(RandomBytes(4, Arg(0) * 1048576)));
                return $"{encoded.Length} {Suite.Fnv1a(encoded):x16}";
            }
            case "Sort":
            {
                ulong state = 3;
                var values = new int[Arg(0)];
                for (long i = 0; i < values.Length; i++)
                    values[i] = (int)(uint)(SplitMix(ref state) >> 32);
                Array.Sort(values);
                ulong hash = 0xCBF29CE484222325;
                foreach (int v in values)
                    hash = (hash ^ (uint)v) * 0x100000001B3;
                return $"{values.Length} {hash:x16}";
            }
            default:
                return null;
        }
    }
}
