namespace Runner;

/// <summary>Everything one runner invocation measured; saved as results.json.</summary>
sealed class Results
{
    public int SchemaVersion { get; set; } = 1;
    public string Command { get; set; } = "";
    public string Profile { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public Settings Settings { get; set; } = new();
    public MachineInfo Machine { get; set; } = new();
    public List<CaseResult> Cases { get; set; } = [];

    public CaseResult Case(string app, string language)
    {
        var found = Cases.Find(c => c.App == app && c.Language == language);
        if (found != null)
            return found;
        var created = new CaseResult { App = app, Language = language };
        Cases.Add(created);
        return created;
    }
}

sealed class Settings
{
    public string Baseline { get; set; } = "";
    public List<string> Apps { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public int BuildRuns { get; set; }
    public int Runs { get; set; }
    public int Warmups { get; set; }
    public int? Cpu { get; set; }
}

sealed class MachineInfo
{
    public string Os { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int LogicalCores { get; set; }
    public long MemoryBytes { get; set; }
    public string? Commit { get; set; }
    public Dictionary<string, string?> Toolchains { get; set; } = [];
    public List<string> RemovedVariables { get; set; } = [];
}

/// <summary>Measurements of one app built and run in one language.</summary>
sealed class CaseResult
{
    public string App { get; set; } = "";
    public string Language { get; set; } = "";
    public List<string> Args { get; set; } = [];
    public string Status { get; set; } = "ok";
    public string? Error { get; set; }
    public List<double> BuildSeconds { get; set; } = [];
    /// <summary>Peak memory of each timed clean build (see ProcessRunner.RunBuild).</summary>
    public List<long> BuildPeakBytes { get; set; } = [];
    /// <summary>Compile benchmark: rebuilds after editing one source file, without cleaning.</summary>
    public List<double> RebuildSeconds { get; set; } = [];
    public List<long> RebuildPeakBytes { get; set; } = [];
    /// <summary>Compile benchmark: the project's source files and non-blank lines in this language.</summary>
    public int? SourceFiles { get; set; }
    public int? SourceLines { get; set; }
    public long? ExecutableBytes { get; set; }
    public long? DeployableBytes { get; set; }
    /// <summary>Operating-system libraries the deployed native files load.</summary>
    public List<string> Libraries { get; set; } = [];
    public List<RunMeasurement> Runs { get; set; } = [];
    public string? Output { get; set; }

    public bool Ok => Status == "ok";

    public void Fail(string message)
    {
        Status = "failed";
        Error ??= message;
    }
}

sealed record RunMeasurement(double WallMs, double UserMs, double SysMs, long PeakBytes);

/// <summary>
/// One column of the comparison: how to read it from a case and how to print it. When
/// RuntimeHostedNote is set, runtime-hosted languages are shown but not ranked on the metric, and
/// the note says why.
/// </summary>
sealed record Metric(string Key, string Title, string Unit, string Description, Func<CaseResult, IReadOnlyList<double>> Values, string Format,
    string? RuntimeHostedNote = null)
{
    public static readonly IReadOnlyList<Metric> All =
    [
        new("execution", "Execution time", "s", "Wall-clock time from process start to exit (median of runs).",
            c => c.Runs.Select(r => r.WallMs / 1000).ToList(), "0.000"),
        new("cpu", "CPU time", "s", "User plus kernel CPU time of the process (median of runs).",
            c => c.Runs.Select(r => (r.UserMs + r.SysMs) / 1000).ToList(), "0.000"),
        new("memory", "Peak memory", "MiB", "Peak working set (Windows) or maximum resident set size (Linux), median of runs.",
            c => c.Runs.Select(r => r.PeakBytes / 1048576.0).ToList(), "0.0"),
        new("compile", "Compile time", "s", "Clean release build with the language's own build tool (median of builds).",
            c => c.BuildSeconds, "0.00"),
        new("compileMemory", "Compile memory", "MiB", "Peak memory of the clean build: on Windows the memory committed by all build processes together, on Linux the peak resident set of the largest one (median of builds).",
            c => c.BuildPeakBytes.Where(b => b > 0).Select(b => b / 1048576.0).ToList(), "0"),
        new("executable", "Executable size", "KiB", "Size of the program file.",
            c => c.ExecutableBytes is long b ? [b / 1024.0] : [], "0",
            "the program file needs its runtime to run; see Deployable size"),
        new("deployable", "Deployable size", "KiB", "All files needed to run the program without an SDK.",
            c => c.DeployableBytes is long b ? [b / 1024.0] : [], "0"),
    ];
}
