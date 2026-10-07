using System.Collections;
using System.Diagnostics;

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
        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(StartInfo(command))
            ?? throw new InvalidOperationException($"cannot start {command.File}");
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
