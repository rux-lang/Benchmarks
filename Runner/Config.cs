using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Runner;

/// <summary>bench.json: which languages, apps and projects take part, default counts, compiler and packaging flags and tool names.</summary>
sealed record BenchConfig(
    string Baseline,
    List<string> Languages,
    List<string> Apps,
    List<string> Projects,
    Defaults Defaults,
    List<string> CppFlags,
    List<string> JavacFlags,
    List<string> NativeImageFlags,
    List<string> JlinkFlags,
    Dictionary<string, string> Tools)
{
    public string Tool(string name) => Tools.TryGetValue(name, out var path) ? path : name;
}

sealed record Defaults(string Profile, int BuildRuns, int Runs, int Warmups, int TimeoutSeconds);

/// <summary>
/// Apps/&lt;App&gt;/app.json: what the app does and how it is called. A generated project
/// (Projects/&lt;Project&gt;/project.json) has the same fields, so the toolchains build both alike.
/// </summary>
sealed record AppSpec(string Name, string Description, string? OutputFile, Dictionary<string, ProfileSpec> Profiles)
{
    [JsonIgnore] public string Directory { get; set; } = "";
    /// <summary>The app.json or project.json the spec was read from, where expected outputs are stored.</summary>
    [JsonIgnore] public string Manifest { get; set; } = "";
    /// <summary>Generated projects only: source files and lines per language folder, as the generator counted them.</summary>
    public Dictionary<string, SourceCount>? Sources { get; init; }
    /// <summary>Seconds before a build step is stopped, when the bench.json default is too short for this project.</summary>
    public int? TimeoutSeconds { get; init; }
}

/// <summary>Number of source files and non-blank source lines of one language's project.</summary>
sealed record SourceCount(int Files, int Lines);

sealed record ProfileSpec(List<string> Args, string Expected);

static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\n",
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"{path} is empty");

    public static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + "\n");
}

static class Config
{
    /// <summary>Finds the repository root by walking up from the current directory to bench.json.</summary>
    public static string FindRoot()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "bench.json")))
                return dir.FullName;
        throw new InvalidOperationException("bench.json not found; run from inside the Benchmarks repository");
    }

    public static AppSpec LoadApp(string root, string name) => Load(Path.Combine(root, "Apps", name), "app.json");

    /// <summary>A generated project from Projects/&lt;Project&gt;/project.json.</summary>
    public static AppSpec LoadProject(string root, string name) => Load(Path.Combine(root, "Projects", name), "project.json");

    static AppSpec Load(string dir, string file)
    {
        var app = Json.Read<AppSpec>(Path.Combine(dir, file));
        app.Directory = dir;
        app.Manifest = Path.Combine(dir, file);
        return app;
    }

    /// <summary>Writes an expected output into app.json or project.json, keeping the rest of the file as it is.</summary>
    public static void SaveExpected(AppSpec app, string profile, string expected)
    {
        var node = JsonNode.Parse(File.ReadAllText(app.Manifest))!;
        node["profiles"]![profile]!["expected"] = expected;
        File.WriteAllText(app.Manifest, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
    }
}
