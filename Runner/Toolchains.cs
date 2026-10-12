namespace Runner;

/// <summary>
/// A command line: program, arguments, working directory and variables set on top of the cleaned
/// environment (an empty value removes the variable).
/// </summary>
sealed record Command(string File, IReadOnlyList<string> Arguments, string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    public override string ToString() =>
        string.Join(' ', new[] { File }.Concat(Arguments).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}

/// <summary>
/// Everything the runner knows about building one language. This is the only place with
/// build commands, output paths and clean rules.
/// </summary>
abstract class Toolchain(BenchConfig config)
{
    protected BenchConfig Config { get; } = config;
    protected static bool IsWindows => OperatingSystem.IsWindows();
    protected static string ExeSuffix => IsWindows ? ".exe" : "";

    public abstract string Name { get; }
    public abstract string Display { get; }
    /// <summary>Folder under Apps/&lt;App&gt;/ that holds this language's project.</summary>
    public abstract string Folder { get; }
    /// <summary>Commands that print the compiler versions, by the name shown in reports.</summary>
    public abstract IEnumerable<(string, Command)> VersionCommands { get; }

    public string ProjectDir(AppSpec app) => Path.Combine(app.Directory, Folder);
    /// <summary>Directories removed before every timed build.</summary>
    public abstract IEnumerable<string> CleanDirs(AppSpec app);
    /// <summary>Every directory the build writes, removed by the `clean` command.</summary>
    public virtual IEnumerable<string> OutputDirs(AppSpec app) => CleanDirs(app);
    /// <summary>Untimed preparation, such as restoring packages. Runs once before timed builds.</summary>
    public virtual Command? Prepare(AppSpec app) => null;
    /// <summary>The steps of one release build; compile time is their total.</summary>
    public abstract IReadOnlyList<Command> Build(AppSpec app);
    public abstract string Executable(AppSpec app);
    /// <summary>How the program is started. Most languages run the executable directly.</summary>
    public virtual Command Launch(AppSpec app, IReadOnlyList<string> arguments, string workingDirectory) =>
        new(Executable(app), arguments, workingDirectory);
    /// <summary>Files needed to run the program on a machine with no SDK installed.</summary>
    public virtual IEnumerable<string> DeployFiles(AppSpec app) => [Executable(app)];
    /// <summary>A note shown under the report, if the language needs one.</summary>
    public virtual string? Note => null;

    public virtual void Clean(AppSpec app)
    {
        foreach (string dir in CleanDirs(app))
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
    }

    public static List<Toolchain> All(BenchConfig config) =>
    [
        new RuxToolchain(config),
        new RustToolchain(config),
        new CppToolchain(config),
        new GoToolchain(config),
        new CSharpToolchain(config, aot: true),
        new CSharpToolchain(config, aot: false),
        new JavaToolchain(config, aot: true),
        new JavaToolchain(config, aot: false),
    ];
}

sealed class RuxToolchain(BenchConfig config) : Toolchain(config)
{
    public override string Name => "Rux";
    public override string Display => "Rux";
    public override string Folder => "Rux";
    public override IEnumerable<(string, Command)> VersionCommands => [("Rux", new(Config.Tool("rux"), ["--version"], "."))];

    public override IEnumerable<string> CleanDirs(AppSpec app) =>
        [Path.Combine(ProjectDir(app), "Bin"), Path.Combine(ProjectDir(app), "Temp")];

    public override IReadOnlyList<Command> Build(AppSpec app) =>
        [new(Config.Tool("rux"), ["--color=never", "build", "--release", "--quiet"], ProjectDir(app))];

    public override string Executable(AppSpec app) =>
        Path.Combine(ProjectDir(app), "Bin", "Release", IsWindows ? "Windows" : "Linux", "x86-64", app.Name + ExeSuffix);
}

sealed class RustToolchain(BenchConfig config) : Toolchain(config)
{
    public override string Name => "Rust";
    public override string Display => "Rust";
    public override string Folder => "Rust";
    public override IEnumerable<(string, Command)> VersionCommands =>
        [("Rust", new(Config.Tool("rustc"), ["--version"], ".")), ("Cargo", new(Config.Tool("cargo"), ["--version"], "."))];

    public override IEnumerable<string> CleanDirs(AppSpec app) => [Path.Combine(ProjectDir(app), "target")];

    public override IReadOnlyList<Command> Build(AppSpec app) =>
        [new(Config.Tool("cargo"), ["build", "--release", "--locked", "--offline", "--quiet"], ProjectDir(app))];

    public override string Executable(AppSpec app) =>
        Path.Combine(ProjectDir(app), "target", "release", app.Name.ToLowerInvariant() + ExeSuffix);
}

sealed class CppToolchain(BenchConfig config) : Toolchain(config)
{
    public override string Name => "Cpp";
    public override string Display => "C++";
    public override string Folder => "Cpp";
    public override IEnumerable<(string, Command)> VersionCommands => [("C++", new(Config.Tool("clang++"), ["--version"], "."))];

    public override IEnumerable<string> CleanDirs(AppSpec app) => [Path.Combine(ProjectDir(app), "build")];

    public override IReadOnlyList<Command> Build(AppSpec app)
    {
        Directory.CreateDirectory(Path.Combine(ProjectDir(app), "build"));
        var args = new List<string>(Config.CppFlags);
        // Link the C and C++ runtimes into the executable (clang's default for MSVC targets, made explicit).
        if (IsWindows)
            args.Add("-fms-runtime-lib=static");
        else
            args.AddRange(["-s", "-static-libstdc++", "-static-libgcc"]);
        args.AddRange(["main.cpp", "-o", Path.Combine("build", app.Name + ExeSuffix)]);
        return [new(Config.Tool("clang++"), args, ProjectDir(app))];
    }

    public override string Executable(AppSpec app) => Path.Combine(ProjectDir(app), "build", app.Name + ExeSuffix);
}

/// <summary>
/// One C# project published two ways, both self-contained and trimmed: NativeAOT (one native
/// executable) and JIT (the app with a trimmed copy of the .NET runtime that JIT-compiles it).
/// Each variant has its own artifacts folder.
/// </summary>
sealed class CSharpToolchain(BenchConfig config, bool aot) : Toolchain(config)
{
    static string Rid => (IsWindows ? "win" : "linux") + "-x64";
    string Variant => aot ? "aot" : "jit";

    public override string Name => aot ? "CSharpAot" : "CSharpJit";
    public override string Display => aot ? "C# AOT" : "C# JIT";
    public override string Folder => "CSharp";
    public override IEnumerable<(string, Command)> VersionCommands => [(".NET SDK", new(Config.Tool("dotnet"), ["--version"], "."))];
    public override string? Note => aot ? null
        : "C# JIT is published self-contained and trimmed: its executable is the apphost, its deployable size includes the .NET runtime it ships.";

    string Artifacts(AppSpec app) => Path.Combine(ProjectDir(app), ".artifacts", Variant);
    string Project(AppSpec app) => app.Name + ".csproj";
    string PublishDir(AppSpec app) => Path.Combine(Artifacts(app), "publish", app.Name, "release_" + Rid);

    IEnumerable<string> CommonArgs(AppSpec app) =>
        [Project(app), "-r", Rid, $"-p:PublishAot={(aot ? "true" : "false")}", "-p:SelfContained=true", "-p:PublishTrimmed=true",
         "--artifacts-path", Artifacts(app), "-nologo", "-v", "q"];

    public override IEnumerable<string> CleanDirs(AppSpec app)
    {
        // Keep the restore output at the top of obj/<App>/ so timed builds need no restore.
        string obj = Path.Combine(Artifacts(app), "obj", app.Name);
        var dirs = new List<string> { Path.Combine(Artifacts(app), "bin"), Path.Combine(Artifacts(app), "publish") };
        if (Directory.Exists(obj))
            dirs.AddRange(Directory.GetDirectories(obj));
        return dirs;
    }

    /// <summary>
    /// Everything under .artifacts except the other variant's folder (older layouts left more there),
    /// plus bin and obj, which only a plain `dotnet build` (or an IDE) writes.
    /// </summary>
    public override IEnumerable<string> OutputDirs(AppSpec app)
    {
        string root = Path.GetDirectoryName(Artifacts(app))!;
        string other = aot ? "jit" : "aot";
        var dirs = Directory.Exists(root)
            ? Directory.GetDirectories(root).Where(d => Path.GetFileName(d) != other).ToList()
            : [];
        return [.. dirs, Path.Combine(ProjectDir(app), "bin"), Path.Combine(ProjectDir(app), "obj")];
    }

    public override Command? Prepare(AppSpec app) =>
        new(Config.Tool("dotnet"), ["restore", .. CommonArgs(app)], ProjectDir(app));

    public override IReadOnlyList<Command> Build(AppSpec app) =>
        [new(Config.Tool("dotnet"),
            ["publish", .. CommonArgs(app), "-c", "Release",
             "--no-restore", "--disable-build-servers", "-nodeReuse:false"],
            ProjectDir(app))];

    public override string Executable(AppSpec app) => Path.Combine(PublishDir(app), app.Name + ExeSuffix);

    public override IEnumerable<string> DeployFiles(AppSpec app) =>
        Directory.Exists(PublishDir(app))
            ? Directory.GetFiles(PublishDir(app)).Where(f => !f.EndsWith(".pdb") && !f.EndsWith(".dbg"))
            : [];
}

/// <summary>
/// Go has no prebuilt standard library: `go build` compiles it into the build cache, and later
/// builds of an unchanged app are cache hits. To time what Rust and C++ do (prebuilt standard
/// library, app compiled from scratch), the standard library is compiled once into a snapshot
/// cache, and every clean build starts from a fresh copy of that snapshot.
/// </summary>
sealed class GoToolchain(BenchConfig config) : Toolchain(config)
{
    public override string Name => "Go";
    public override string Display => "Go";
    public override string Folder => "Go";
    public override IEnumerable<(string, Command)> VersionCommands => [("Go", new(Config.Tool("go"), ["version"], "."))];

    static string Snapshot(AppSpec app) => Path.Combine(Path.GetDirectoryName(app.Directory)!, ".gocache-std");
    string Cache(AppSpec app) => Path.Combine(ProjectDir(app), "build", "gocache");

    // GOAMD64=v1 is baseline x86-64 (no FMA, like the other languages); no cgo, no toolchain downloads.
    static Dictionary<string, string> Environment(string cache) => new()
    {
        ["GOCACHE"] = cache, ["GOAMD64"] = "v1", ["CGO_ENABLED"] = "0", ["GOTOOLCHAIN"] = "local",
    };

    public override IEnumerable<string> CleanDirs(AppSpec app) => [Path.Combine(ProjectDir(app), "build")];

    public override Command? Prepare(AppSpec app) =>
        new(Config.Tool("go"), ["build", "-trimpath", "std"], ProjectDir(app), Environment(Snapshot(app)));

    public override void Clean(AppSpec app)
    {
        base.Clean(app);
        if (Directory.Exists(Snapshot(app)))
            CopyDirectory(Snapshot(app), Cache(app));
    }

    public override IReadOnlyList<Command> Build(AppSpec app) =>
        [new(Config.Tool("go"),
            ["build", "-trimpath", "-buildvcs=false", "-ldflags=-s -w", "-o", Path.Combine("build", app.Name + ExeSuffix), "."],
            ProjectDir(app), Environment(Cache(app)))];

    public override string Executable(AppSpec app) => Path.Combine(ProjectDir(app), "build", app.Name + ExeSuffix);

    static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }
}

/// <summary>
/// One Java source compiled with javac, then packaged two ways, both self-contained: a jar with a
/// minimal jlink runtime whose HotSpot JVM JIT-compiles it (JIT), or a GraalVM Native Image (AOT,
/// one native executable). Each variant has its own folder.
/// </summary>
sealed class JavaToolchain(BenchConfig config, bool aot) : Toolchain(config)
{
    string Variant => aot ? "aot" : "jit";

    public override string Name => aot ? "JavaAot" : "JavaJit";
    public override string Display => aot ? "Java AOT" : "Java JIT";
    public override string Folder => "Java";
    public override string? Note => aot ? null
        : "Java JIT runs the jar with java -jar from a minimal jlink runtime: its executable is the jar, its deployable size includes the runtime.";

    public override IEnumerable<(string, Command)> VersionCommands => aot
        ? [("JDK", new(JdkTool("java"), ["-version"], ".")), ("GraalVM", new(NativeImage, ["--version"], "."))]
        : [("JDK", new(JdkTool("java"), ["-version"], "."))];

    string Output(AppSpec app) => Path.Combine(ProjectDir(app), "build", Variant);
    string Classes(AppSpec app) => Path.Combine("build", Variant, "classes");
    /// <summary>The jlink runtime image that ships with the Java JIT jar.</summary>
    string Runtime(AppSpec app) => Path.Combine(Output(app), "runtime");
    string RuntimeJava(AppSpec app) => Path.Combine(Runtime(app), "bin", "java" + ExeSuffix);

    public override IEnumerable<string> CleanDirs(AppSpec app) => [Output(app)];

    public override IReadOnlyList<Command> Build(AppSpec app)
    {
        var javac = new Command(JdkTool("javac"), [.. Config.JavacFlags, "-d", Classes(app), "Main.java"], ProjectDir(app));
        if (aot)
            return [javac, new Command(NativeImage,
                [.. Config.NativeImageFlags, .. StaticRuntimeFlags, "-cp", Classes(app), "-o", Path.Combine("build", Variant, app.Name), "Main"],
                ProjectDir(app))];
        var jar = new Command(JdkTool("jar"),
            ["--create", "--file", Path.Combine("build", Variant, app.Name + ".jar"), "--main-class", "Main", "-C", Classes(app), "."],
            ProjectDir(app));
        var jlink = new Command(JdkTool("jlink"), [.. Config.JlinkFlags, "--output", Runtime(app)], ProjectDir(app));
        // The class-data-sharing archive the JDK ships for fast start-up, for the JVM's default settings
        // only (jlink's --generate-cds-archive writes four, one per heap layout, and the JVM maps one).
        var cds = new Command(RuntimeJava(app), ["-Xshare:dump"], ProjectDir(app));
        return [javac, jar, jlink, cds];
    }

    /// <summary>
    /// Link the C runtime into the executable. On Linux, --static-nolibc links everything but glibc
    /// statically. On Windows, Native Image links the C runtime DLLs (vcruntime140, api-ms-win-crt-*),
    /// and the JDK's static libraries are compiled for them (/MD), so the static runtime replaces
    /// them at link time. Their calls go through __imp_ pointers, which the linker resolves to a
    /// static definition only once that definition is linked in, hence the /INCLUDE list.
    /// </summary>
    static IEnumerable<string> StaticRuntimeFlags => IsWindows
        ? ((string[])["/NODEFAULTLIB:msvcrt.lib", "/NODEFAULTLIB:vcruntime.lib", "/NODEFAULTLIB:ucrt.lib",
              "libcmt.lib", "libvcruntime.lib", "libucrt.lib", "/IGNORE:4217,4286",
              .. JdkCrtImports.Select(f => "/INCLUDE:" + f)])
            .Select(o => "-H:NativeLinkerOption=" + o)
        : ["--static-nolibc"];

    /// <summary>C runtime functions the JDK's static libraries call through __imp_ pointers.</summary>
    static readonly string[] JdkCrtImports =
    [
        "__acrt_iob_func", "__stdio_common_vfprintf", "__stdio_common_vsprintf", "__stdio_common_vsscanf",
        "__stdio_common_vswprintf", "__stdio_common_vswscanf", "_errno", "_exit", "_itoa_s", "_strdup", "_wcsdup",
        "_wcsupr", "_wfullpath", "_wgetcwd", "_wgetdcwd", "_wgetenv", "_wrename", "abort", "atoi", "fclose", "fgets",
        "fopen", "fopen_s", "free", "iswctype", "malloc", "raise", "realloc", "signal", "strerror", "strerror_s",
        "strncpy", "towupper", "wcscat", "wcscat_s", "wcscmp", "wcscpy", "wcslen", "wcsncat", "wcsncmp", "wcstok_s",
        "wcstombs",
    ];

    public override string Executable(AppSpec app) =>
        Path.Combine(Output(app), app.Name + (aot ? ExeSuffix : ".jar"));

    public override Command Launch(AppSpec app, IReadOnlyList<string> arguments, string workingDirectory) =>
        aot ? base.Launch(app, arguments, workingDirectory)
            : new(RuntimeJava(app), ["-jar", Executable(app), .. arguments], workingDirectory);

    /// <summary>
    /// AOT: the executable plus any shared libraries Native Image put next to it.
    /// JIT: the jar plus the whole jlink runtime.
    /// </summary>
    public override IEnumerable<string> DeployFiles(AppSpec app)
    {
        if (!Directory.Exists(Output(app)))
            return [];
        return aot
            ? Directory.GetFiles(Output(app)).Where(f => f == Executable(app) || f.EndsWith(".dll") || f.EndsWith(".so"))
            : [Executable(app), .. Directory.Exists(Runtime(app)) ? Directory.GetFiles(Runtime(app), "*", SearchOption.AllDirectories) : []];
    }

    /// <summary>
    /// The JDK's own bin folder, found from java.home. On Windows the `java` on PATH can be a
    /// copied launcher stub (Oracle's javapath) rather than the JDK's java.exe.
    /// </summary>
    string JdkTool(string name)
    {
        jdkHome ??= FindJdkHome(Config.Tool("java")) ?? "";
        string path = Path.Combine(jdkHome, "bin", name + ExeSuffix);
        return jdkHome != "" && File.Exists(path) ? path : name;
    }

    static string? jdkHome;

    static string? FindJdkHome(string java)
    {
        try
        {
            string output = ProcessRunner.RunTool(new Command(java, ["-XshowSettings:properties", "-version"], "."), 60).Output;
            return output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("java.home = "))?["java.home = ".Length..];
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>native-image from bench.json or PATH, else from GRAALVM_HOME. On Windows it is a .cmd script.</summary>
    string NativeImage => nativeImage ??= ResolveNativeImage(Config.Tool("native-image"));

    static string? nativeImage;

    static string ResolveNativeImage(string tool)
    {
        if (Path.IsPathRooted(tool) || Path.HasExtension(tool))
            return tool;
        string[] names = IsWindows ? [tool + ".cmd", tool + ".exe"] : [tool];
        var dirs = (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).ToList();
        if (System.Environment.GetEnvironmentVariable("GRAALVM_HOME") is string graal)
            dirs.Add(Path.Combine(graal, "bin"));
        foreach (string dir in dirs.Where(d => d != ""))
            foreach (string name in names)
                if (File.Exists(Path.Combine(dir, name)))
                    return Path.Combine(dir, name);
        return tool;
    }
}
