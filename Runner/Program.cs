using System.Globalization;
using System.Runtime.Versioning;
using Runner;

const string Usage = """
    Usage: dotnet run --project Runner -c Release -- <command> [options]

    Commands:
      doctor            Show the machine and toolchain versions and check the setup
      build             Measure compile time and program size
      run               Measure execution time, CPU time and peak memory
      all               build + run, then write the report (the usual choice)
      report [dir]      Rewrite the report files of a results folder (default: the latest)

    Options:
      --app A,B         Only these apps                 (default: all in bench.json)
      --lang X,Y        Only these languages            (Rux, Rust, Cpp, Go, CSharpAot, CSharpJit, JavaAot, JavaJit)
      --profile NAME    small | standard                (default: standard)
      --runs N          Measured runs per program       (default: bench.json)
      --warmups N       Unmeasured runs before those    (default: bench.json)
      --build-runs N    Timed clean builds per program  (default: bench.json)
      --cpu K           Pin the measured programs to logical CPU K
      --timeout S       Seconds before a build or run is stopped
      --update-expected Store agreed outputs in app.json when none are stored yet
    """;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(Usage);
    return 2;
}

string root = Config.FindRoot();
var config = Json.Read<BenchConfig>(Path.Combine(root, "bench.json"));
try
{
    options.ApplyDefaults(config);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
var toolchains = Toolchain.All(config).Where(t => options.Languages.Contains(t.Name)).ToList();
var apps = options.Apps.Select(a => Config.LoadApp(root, a)).ToList();

switch (options.Command)
{
    case "doctor":
        return Doctor();
    case "build" or "run" or "all":
        return Measure();
    case "report":
        string dir = options.ReportDir ?? LatestResults()
            ?? throw new InvalidOperationException("no results yet; run `all` first");
        var saved = Json.Read<Results>(Path.Combine(dir, "results.json"));
        Report.Write(saved, Toolchain.All(config), dir);
        Console.WriteLine($"Report written to {Path.Combine(dir, "report.md")}");
        return 0;
    default:
        Console.Error.WriteLine(Usage);
        return 2;
}

int Measure()
{
    string resultsDir = Path.Combine(root, "Results",
        $"{DateTime.Now:yyyyMMdd-HHmmss}-{(OperatingSystem.IsWindows() ? "windows" : "linux")}");
    Directory.CreateDirectory(resultsDir);
    var results = new Results
    {
        Command = options.Command,
        Profile = options.Profile,
        StartedAt = DateTimeOffset.Now,
        Machine = Machine(),
        Settings = new Settings
        {
            Baseline = config.Baseline,
            Apps = options.Apps,
            Languages = options.Languages,
            BuildRuns = options.BuildRuns,
            Runs = options.Runs,
            Warmups = options.Warmups,
            Cpu = options.Cpu,
        },
    };
    var suite = new Suite(config, options, results, resultsDir);
    void Save() => Json.Write(Path.Combine(resultsDir, "results.json"), results);

    if (options.Command is "build" or "all")
    {
        Console.WriteLine($"Building ({options.BuildRuns} timed clean builds each)");
        foreach (var app in apps)
            foreach (var toolchain in toolchains)
            {
                suite.Build(app, toolchain);
                Save();
            }
    }
    if (options.Command is "run" or "all")
    {
        Console.WriteLine($"Running profile '{options.Profile}' ({options.Warmups} warm-up + {options.Runs} measured runs each)");
        if (options.Cpu is int cpu)
            PinTo(cpu);
        foreach (var app in apps)
        {
            suite.Run(app, toolchains);
            Save();
        }
    }

    results.FinishedAt = DateTimeOffset.Now;
    Save();
    Directory.Delete(Path.Combine(resultsDir, "work"), recursive: true);
    Report.Write(results, Toolchain.All(config), resultsDir);
    Console.WriteLine();
    Console.WriteLine(Report.ConsoleSummary(results, Toolchain.All(config)));
    Console.WriteLine($"Results: {resultsDir}");
    Console.WriteLine($"Report:  {Path.Combine(resultsDir, "report.html")}");
    return results.Cases.All(c => c.Ok) ? 0 : 1;
}

int Doctor()
{
    var machine = Machine();
    Console.WriteLine($"OS        {machine.Os}");
    Console.WriteLine($"CPU       {machine.Cpu} ({machine.LogicalCores} logical cores)");
    Console.WriteLine($"Memory    {machine.MemoryBytes / 1073741824.0:0.0} GiB");
    Console.WriteLine($"Commit    {machine.Commit ?? "(not a git checkout)"}");
    Console.WriteLine();
    bool ok = true;
    foreach (var (name, version) in machine.Toolchains)
    {
        Console.WriteLine($"{name,-10}{version ?? "NOT FOUND"}");
        ok &= version != null;
    }
    if (machine.Toolchains.ContainsValue(null))
        Console.WriteLine("Install the missing tools (or set their paths under \"tools\"), or remove their languages from bench.json.");

    // Rux apps resolve their packages from the local cache that `rux install` fills.
    string cache = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rux", "Packages", "Rux")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".rux", "packages", "Rux");
    bool packages = Directory.Exists(Path.Combine(cache, "Core"));
    Console.WriteLine();
    Console.WriteLine(packages
        ? $"Rux packages found in {cache}"
        : $"Rux packages missing from {cache}: run `rux install` in any Apps/<App>/Rux folder");
    ok &= packages;

    // Arguments.rux works around missing command-line support in Rux; all copies must match.
    var copies = apps.Select(a => File.ReadAllText(Path.Combine(a.Directory, "Rux", "Src", "Arguments.rux"))).Distinct().Count();
    Console.WriteLine(copies == 1 ? "Arguments.rux is identical in every Rux app" : "Arguments.rux differs between apps: keep the copies identical");
    ok &= copies == 1;

    if (ProcessRunner.RemovedVariables.Count > 0)
        Console.WriteLine($"Removed from child processes: {string.Join(", ", ProcessRunner.RemovedVariables)}");
    return ok ? 0 : 1;
}

MachineInfo Machine()
{
    var info = new MachineInfo
    {
        Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Cpu = CpuName(),
        LogicalCores = Environment.ProcessorCount,
        MemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        Commit = Commit(),
    };
    // Only the languages in bench.json, so a machine without one toolchain can leave it out there.
    foreach (var toolchain in Toolchain.All(config).Where(t => config.Languages.Contains(t.Name)))
        foreach (var (label, command) in toolchain.VersionCommands)
            if (!info.Toolchains.ContainsKey(label))
                info.Toolchains[label] = ProcessRunner.TryVersion(command);
    info.RemovedVariables = [.. ProcessRunner.RemovedVariables.Order()];
    return info;
}

string? Commit()
{
    string? hash = ProcessRunner.TryVersion(new Command("git", ["rev-parse", "--short", "HEAD"], root));
    if (hash == null)
        return null;
    try
    {
        bool dirty = ProcessRunner.RunTool(new Command("git", ["status", "--porcelain"], root)).Output.Length > 0;
        return dirty ? hash + " (uncommitted changes)" : hash;
    }
    catch (Exception)
    {
        return hash;
    }
}

static string CpuName()
{
    if (OperatingSystem.IsWindows())
        return WindowsCpuName() ?? "unknown";
    if (File.Exists("/proc/cpuinfo"))
        foreach (string line in File.ReadLines("/proc/cpuinfo"))
            if (line.StartsWith("model name"))
                return line[(line.IndexOf(':') + 1)..].Trim();
    return "unknown";
}

[SupportedOSPlatform("windows")]
static string? WindowsCpuName() =>
    (Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string)?.Trim();

static void PinTo(int cpu)
{
    // Child processes inherit the runner's CPU affinity on Windows and Linux.
    if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        System.Diagnostics.Process.GetCurrentProcess().ProcessorAffinity = (nint)(1L << cpu);
}

string? LatestResults()
{
    string dir = Path.Combine(root, "Results");
    return Directory.Exists(dir)
        ? Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "results.json"))).Order().LastOrDefault()
        : null;
}

sealed class Options
{
    public string Command { get; private set; } = "";
    public string? ReportDir { get; private set; }
    public List<string> Apps { get; private set; } = [];
    public List<string> Languages { get; private set; } = [];
    public string Profile { get; private set; } = "";
    public int Runs { get; private set; } = -1;
    public int Warmups { get; private set; } = -1;
    public int BuildRuns { get; private set; } = -1;
    public int? Cpu { get; private set; }
    public int? TimeoutSeconds { get; private set; }
    public bool UpdateExpected { get; private set; }

    public static Options Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("missing command");
        var options = new Options { Command = args[0] };
        for (int i = 1; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            int Number() => int.TryParse(Value(), out int n) && n >= 0 ? n : throw new ArgumentException($"{args[i - 1]} needs a number");
            switch (args[i])
            {
                case "--app": options.Apps = [.. Value().Split(',', StringSplitOptions.RemoveEmptyEntries)]; break;
                case "--lang": options.Languages = [.. Value().Split(',', StringSplitOptions.RemoveEmptyEntries)]; break;
                case "--profile": options.Profile = Value(); break;
                case "--runs": options.Runs = Number(); break;
                case "--warmups": options.Warmups = Number(); break;
                case "--build-runs": options.BuildRuns = Number(); break;
                case "--cpu": options.Cpu = Number(); break;
                case "--timeout": options.TimeoutSeconds = Number(); break;
                case "--update-expected": options.UpdateExpected = true; break;
                default:
                    if (options.Command == "report" && options.ReportDir == null && !args[i].StartsWith("--"))
                        options.ReportDir = Path.GetFullPath(args[i]);
                    else
                        throw new ArgumentException($"unknown option {args[i]}");
                    break;
            }
        }
        return options;
    }

    public void ApplyDefaults(BenchConfig config)
    {
        if (Apps.Count == 0) Apps = config.Apps;
        if (Languages.Count == 0) Languages = config.Languages;
        if (Profile == "") Profile = config.Defaults.Profile;
        if (Runs < 0) Runs = config.Defaults.Runs;
        if (Warmups < 0) Warmups = config.Defaults.Warmups;
        if (BuildRuns < 0) BuildRuns = config.Defaults.BuildRuns;
        foreach (string app in Apps.Where(a => !config.Apps.Contains(a)))
            throw new ArgumentException($"unknown app {app}; known: {string.Join(", ", config.Apps)}");
        foreach (string language in Languages.Where(l => !config.Languages.Contains(l)))
            throw new ArgumentException($"unknown language {language}; known: {string.Join(", ", config.Languages)}");
    }
}
