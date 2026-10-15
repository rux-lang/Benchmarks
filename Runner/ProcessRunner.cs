using System.Collections;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace Runner;

/// <summary>Result of one measured run of a benchmark program.</summary>
sealed record RunSample(int ExitCode, string Stdout, string Stderr, double WallMs, double UserMs, double SysMs, long PeakBytes);

static class ProcessRunner
{
    /// <summary>
    /// Variables removed from every child process so that a developer's shell cannot change how
    /// a compiler optimizes or how a runtime behaves. They are listed in results.json.
    /// </summary>
    static readonly string[] RemovedPrefixes =
        ["DOTNET_", "COMPlus_", "MSBUILD", "CARGO_", "RUSTFLAGS", "RUSTC_", "RUSTDOCFLAGS", "CCC_OVERRIDE_OPTIONS", "CGO_"];
    static readonly string[] RemovedNames =
        ["CL", "_CL_", "LINK", "_LINK_",
         "GOFLAGS", "GOAMD64", "GOGC", "GOMEMLIMIT", "GOMAXPROCS", "GODEBUG", "GOEXPERIMENT", "GOOS", "GOARCH", "GOTOOLCHAIN",
         "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS", "JAVA_OPTS", "CLASSPATH"];
    static readonly string[] KeptNames = ["DOTNET_ROOT", "DOTNET_ROOT_X64", "CARGO_HOME"];

    public static List<string> RemovedVariables { get; } = [];
    static Dictionary<string, string>? environment;

    /// <summary>The cleaned environment shared by every child process.</summary>
    public static Dictionary<string, string> Environment
    {
        get
        {
            if (environment != null)
                return environment;
            environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                string key = (string)entry.Key;
                bool remove = !KeptNames.Contains(key, StringComparer.OrdinalIgnoreCase)
                    && (RemovedNames.Contains(key, StringComparer.OrdinalIgnoreCase)
                        || RemovedPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
                if (remove)
                    RemovedVariables.Add(key);
                else
                    environment[key] = (string?)entry.Value ?? "";
            }
            environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            environment["DOTNET_NOLOGO"] = "1";
            environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            environment["MSBUILDDISABLENODEREUSE"] = "1";

            // NativeAOT's linker lookup (vcvarsall.bat) calls vswhere.exe by name, which Visual
            // Studio normally puts on PATH. Add its folder when a shell left it out.
            if (OperatingSystem.IsWindows())
            {
                string installer = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft Visual Studio", "Installer");
                if (Directory.Exists(installer))
                    environment["PATH"] = installer + Path.PathSeparator + environment.GetValueOrDefault("PATH", "");
            }
            return environment;
        }
    }

    static ProcessStartInfo StartInfo(Command command)
    {
        var info = new ProcessStartInfo(command.File)
        {
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in command.Arguments)
            info.ArgumentList.Add(argument);
        info.Environment.Clear();
        foreach (var (key, value) in Environment)
            info.Environment[key] = value;
        foreach (var (key, value) in command.Environment ?? new Dictionary<string, string>())
        {
            if (value == "")
                info.Environment.Remove(key);
            else
                info.Environment[key] = value;
        }
        return info;
    }

    /// <summary>
    /// Runs a tool (compiler, version query) and returns its wall time in seconds. Throws with the
    /// tool's output when it fails.
    /// </summary>
    public static (double Seconds, string Output) RunTool(Command command, int timeoutSeconds = 600)
    {
        return Run(command, timeoutSeconds, job: null);
    }

    /// <summary>
    /// Runs one build step and returns its wall time and peak memory. On Windows the peak is the
    /// memory committed by all of the step's processes together (the build tool and every
    /// compiler, linker or server it starts), from a job object. On Linux it is the peak resident
    /// set size of the step's largest process, from wait4.
    /// </summary>
    public static (double Seconds, long PeakBytes) RunBuild(Command command, int timeoutSeconds)
    {
        if (command.InProcess != null)
            return (RunTool(command, timeoutSeconds).Seconds, 0);
        if (OperatingSystem.IsWindows())
        {
            using var job = new Native.BuildJob();
            double seconds = Run(command, timeoutSeconds, job).Seconds;
            return (seconds, job.PeakBytes);
        }
        if (OperatingSystem.IsLinux())
        {
            var environment = new Dictionary<string, string>(Environment);
            foreach (var (key, value) in command.Environment ?? new Dictionary<string, string>())
            {
                if (value == "")
                    environment.Remove(key);
                else
                    environment[key] = value;
            }
            var sample = Native.MeasureLinux(FindExecutable(command.File, environment), command.Arguments, command.WorkingDirectory, environment, timeoutSeconds);
            if (sample.ExitCode != 0)
                throw new InvalidOperationException($"{command} exited with {sample.ExitCode}\n{(sample.Stdout + sample.Stderr).Trim()}");
            return (sample.WallMs / 1000, sample.PeakBytes);
        }
        return (RunTool(command, timeoutSeconds).Seconds, 0);
    }

    /// <summary>posix_spawn needs a path: a bare tool name is looked up on PATH.</summary>
    static string FindExecutable(string file, IReadOnlyDictionary<string, string> environment)
    {
        if (file.Contains('/'))
            return Path.GetFullPath(file);
        foreach (string dir in environment.GetValueOrDefault("PATH", "").Split(':', StringSplitOptions.RemoveEmptyEntries))
            if (File.Exists(Path.Combine(dir, file)))
                return Path.Combine(dir, file);
        return file;
    }

    static (double Seconds, string Output) Run(Command command, int timeoutSeconds, Native.BuildJob? job)
    {
        var stopwatch = Stopwatch.StartNew();
        if (command.InProcess != null)
        {
            command.InProcess();
            return (stopwatch.Elapsed.TotalSeconds, "");
        }
        using var process = Process.Start(StartInfo(command))
            ?? throw new InvalidOperationException($"cannot start {command.File}");
        if (job != null && OperatingSystem.IsWindows())
            job.Add(process);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{command} did not finish in {timeoutSeconds} s");
        }
        process.WaitForExit();
        double seconds = stopwatch.Elapsed.TotalSeconds;
        string output = (stdout.Result + stderr.Result).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{command} exited with {process.ExitCode}\n{output}");
        return (seconds, output);
    }

    /// <summary>Runs a tool and returns its first output line, or null when it is missing.</summary>
    public static string? TryVersion(Command command)
    {
        try
        {
            return RunTool(command, 60).Output.Split('\n')[0].Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> with this process allowed on one logical CPU only. Child
    /// processes inherit the affinity on Windows and Linux, so every process of a build started
    /// meanwhile shares that CPU. The previous affinity is restored afterwards.
    /// </summary>
    public static void OnOneCpu(int cpu, Action action)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            nint previous = Affinity;
            Affinity = (nint)(1L << cpu);
            try
            {
                action();
            }
            finally
            {
                Affinity = previous;
            }
            return;
        }
        throw new PlatformNotSupportedException("CPU affinity supports Windows and Linux");
    }

    /// <summary>The lowest logical CPU this process may run on.</summary>
    public static int FirstAllowedCpu() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? System.Numerics.BitOperations.TrailingZeroCount((ulong)Affinity) : 0;

    /// <summary>The logical CPUs this process may run on, one bit each.</summary>
    [SupportedOSPlatform("windows"), SupportedOSPlatform("linux")]
    static nint Affinity
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return process.ProcessorAffinity;
        }
        set
        {
            using var process = Process.GetCurrentProcess();
            process.ProcessorAffinity = value;
        }
    }

    /// <summary>Runs a benchmark program once and measures time and peak memory.</summary>
    public static RunSample Measure(Command command, int timeoutSeconds)
    {
        if (OperatingSystem.IsWindows())
            return Native.MeasureWindows(StartInfo(command), timeoutSeconds);
        if (OperatingSystem.IsLinux())
            return Native.MeasureLinux(command.File, command.Arguments, command.WorkingDirectory, Environment, timeoutSeconds);
        throw new PlatformNotSupportedException("measurements support Windows and Linux");
    }
}
