namespace Runner;

/// <summary>A command line: program, arguments and working directory.</summary>
sealed record Command(string File, IReadOnlyList<string> Arguments, string WorkingDirectory)
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
    /// <summary>Command that prints the compiler version.</summary>
    public abstract Command VersionCommand { get; }

    public string ProjectDir(AppSpec app) => Path.Combine(app.Directory, Folder);
    /// <summary>Directories removed before every timed build.</summary>
    public abstract IEnumerable<string> CleanDirs(AppSpec app);
    /// <summary>Untimed preparation, such as restoring packages. Runs once before timed builds.</summary>
    public virtual Command? Prepare(AppSpec app) => null;
    public abstract Command Build(AppSpec app);
    public abstract string Executable(AppSpec app);
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
        new CSharpToolchain(config, aot: true),
        new CSharpToolchain(config, aot: false),
    ];
}

sealed class RuxToolchain(BenchConfig config) : Toolchain(config)
{
    public override string Name => "Rux";
    public override string Display => "Rux";
    public override string Folder => "Rux";
    public override Command VersionCommand => new(Config.Tool("rux"), ["--version"], ".");

    public override IEnumerable<string> CleanDirs(AppSpec app) =>
        [Path.Combine(ProjectDir(app), "Bin"), Path.Combine(ProjectDir(app), "Temp")];

    public override Command Build(AppSpec app) =>
        new(Config.Tool("rux"), ["--color=never", "build", "--release", "--quiet"], ProjectDir(app));

    public override string Executable(AppSpec app) =>
        Path.Combine(ProjectDir(app), "Bin", "Release", IsWindows ? "Windows" : "Linux", "x86-64", app.Name + ExeSuffix);
}

sealed class RustToolchain(BenchConfig config) : Toolchain(config)
{
    public override string Name => "Rust";
    public override string Display => "Rust";
    public override string Folder => "Rust";
    public override Command VersionCommand => new(Config.Tool("rustc"), ["--version"], ".");

    public override IEnumerable<string> CleanDirs(AppSpec app) => [Path.Combine(ProjectDir(app), "target")];

    public override Command Build(AppSpec app) =>
        new(Config.Tool("cargo"), ["build", "--release", "--locked", "--offline", "--quiet"], ProjectDir(app));

    public override string Executable(AppSpec app) =>
        Path.Combine(ProjectDir(app), "target", "release", app.Name.ToLowerInvariant() + ExeSuffix);
}

sealed class CppToolchain(BenchConfig config) : Toolchain(config)
{
    public override string Name => "Cpp";
    public override string Display => "C++";
    public override string Folder => "Cpp";
    public override Command VersionCommand => new(Config.Tool("clang++"), ["--version"], ".");

    public override IEnumerable<string> CleanDirs(AppSpec app) => [Path.Combine(ProjectDir(app), "build")];

    public override Command Build(AppSpec app)
    {
        Directory.CreateDirectory(Path.Combine(ProjectDir(app), "build"));
        var args = new List<string>(Config.CppFlags);
        if (!IsWindows)
            args.Add("-s");
        args.AddRange(["main.cpp", "-o", Path.Combine("build", app.Name + ExeSuffix)]);
        return new(Config.Tool("clang++"), args, ProjectDir(app));
    }

    public override string Executable(AppSpec app) => Path.Combine(ProjectDir(app), "build", app.Name + ExeSuffix);
}

/// <summary>
/// One C# project published two ways: NativeAOT (self-contained native executable) and JIT
/// (framework-dependent, needs the .NET runtime). Each variant has its own artifacts folder.
/// </summary>
sealed class CSharpToolchain(BenchConfig config, bool aot) : Toolchain(config)
{
    static string Rid => (IsWindows ? "win" : "linux") + "-x64";
    string Variant => aot ? "aot" : "jit";

    public override string Name => aot ? "CSharpAot" : "CSharpJit";
    public override string Display => aot ? "C# AOT" : "C# JIT";
    public override string Folder => "CSharp";
    public override Command VersionCommand => new(Config.Tool("dotnet"), ["--version"], ".");
    public override string? Note => aot ? null : "C# JIT is framework-dependent: its deployable size excludes the shared .NET runtime it needs.";

    string Artifacts(AppSpec app) => Path.Combine(ProjectDir(app), ".artifacts", Variant);
    string Project(AppSpec app) => app.Name + ".csproj";
    string PublishDir(AppSpec app) => Path.Combine(Artifacts(app), "publish", app.Name, "release_" + Rid);

    IEnumerable<string> CommonArgs(AppSpec app) =>
        [Project(app), "-r", Rid, $"-p:PublishAot={(aot ? "true" : "false")}", "--artifacts-path", Artifacts(app), "-nologo", "-v", "q"];

    public override IEnumerable<string> CleanDirs(AppSpec app)
    {
        // Keep the restore output at the top of obj/<App>/ so timed builds need no restore.
        string obj = Path.Combine(Artifacts(app), "obj", app.Name);
        var dirs = new List<string> { Path.Combine(Artifacts(app), "bin"), Path.Combine(Artifacts(app), "publish") };
        if (Directory.Exists(obj))
            dirs.AddRange(Directory.GetDirectories(obj));
        return dirs;
    }

    public override Command? Prepare(AppSpec app) =>
        new(Config.Tool("dotnet"), ["restore", .. CommonArgs(app)], ProjectDir(app));

    public override Command Build(AppSpec app) =>
        new(Config.Tool("dotnet"),
            ["publish", .. CommonArgs(app), "-c", "Release", "--self-contained", aot ? "true" : "false",
             "--no-restore", "--disable-build-servers", "-nodeReuse:false"],
            ProjectDir(app));

    public override string Executable(AppSpec app) => Path.Combine(PublishDir(app), app.Name + ExeSuffix);

    public override IEnumerable<string> DeployFiles(AppSpec app) =>
        Directory.Exists(PublishDir(app))
            ? Directory.GetFiles(PublishDir(app)).Where(f => !f.EndsWith(".pdb") && !f.EndsWith(".dbg"))
            : [];
}
