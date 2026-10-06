using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Runner;

/// <summary>bench.json: which languages and apps take part, default counts and tool names.</summary>
sealed record BenchConfig(
    string Baseline,
    List<string> Languages,
    List<string> Apps,
    Defaults Defaults,
    List<string> CppFlags,
    Dictionary<string, string> Tools)
{
    public string Tool(string name) => Tools.TryGetValue(name, out var path) ? path : name;
}

sealed record Defaults(string Profile, int BuildRuns, int Runs, int Warmups, int TimeoutSeconds);

/// <summary>Apps/&lt;App&gt;/app.json: what the app does and how it is called.</summary>
sealed record AppSpec(string Name, string Description, string? OutputFile, Dictionary<string, ProfileSpec> Profiles)
{
    [JsonIgnore] public string Directory { get; set; } = "";
}

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

    public static AppSpec LoadApp(string root, string name)
    {
        string dir = Path.Combine(root, "Apps", name);
        var app = Json.Read<AppSpec>(Path.Combine(dir, "app.json"));
        app.Directory = dir;
        return app;
    }

    /// <summary>Writes an expected output into app.json, keeping the rest of the file as it is.</summary>
    public static void SaveExpected(AppSpec app, string profile, string expected)
    {
        string path = Path.Combine(app.Directory, "app.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        node["profiles"]![profile]!["expected"] = expected;
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
    }
}
