using System.Text.Json;
using System.Text.Json.Nodes;

namespace Runner.Generator;

/// <summary>
/// Writes Projects/&lt;Project&gt;/&lt;Language&gt;/ from the specs and keeps project.json in step.
/// Files are rewritten only when their content changes, so timestamps (and incremental builds)
/// are left alone, and source files the generator no longer produces are removed.
/// </summary>
static class ProjectGenerator
{
    /// <summary>Extensions counted as source code; manifests and lock files are not.</summary>
    static readonly string[] CodeExtensions = [".rux", ".rs", ".cpp", ".h", ".go", ".cs", ".java"];
    static readonly string[] OwnedExtensions = [.. CodeExtensions, ".toml", ".lock", ".mod", ".csproj", ".txt"];
    /// <summary>Build output folders, never touched by the generator.</summary>
    static readonly string[] OutputFolders = ["Bin", "Temp", "target", "build", ".artifacts", "bin", "obj"];

    public static string ProjectsDir(string root) => Path.Combine(root, "Projects");

    public static void Write(string root, IEnumerable<ProjectSpec> projects)
    {
        foreach (var project in projects)
        {
            string dir = Path.Combine(ProjectsDir(root), project.Name);
            var counts = new Dictionary<string, SourceCount>();
            int written = 0, removed = 0;
            foreach (var emitter in Emitter.All)
            {
                string languageDir = Path.Combine(dir, emitter.Folder);
                var files = emitter.Emit(project).ToList();
                var paths = files.Select(f => Path.GetFullPath(Path.Combine(languageDir, f.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (string stale in OwnedFiles(languageDir).Where(f => !paths.Contains(f)))
                {
                    File.Delete(stale);
                    removed++;
                }
                foreach (var file in files)
                {
                    string path = Path.Combine(languageDir, file.Path);
                    string content = Normalize(file.Content);
                    if (File.Exists(path) && File.ReadAllText(path) == content)
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, content);
                    written++;
                }
                counts[emitter.Folder] = Count(files);
            }
            WriteProjectJson(dir, project, counts);
            Console.WriteLine($"{project.Name,-10}{project.Modules.Count,4} modules   {written} files written, {removed} removed");
            foreach (var (language, count) in counts)
                Console.WriteLine($"  {language,-8}{count.Files,5} files {count.Lines,7} lines");
        }
    }

    /// <summary>Generated files whose content on disk differs from what the generator writes now, or that it no longer writes.</summary>
    public static List<string> Stale(string root, IEnumerable<ProjectSpec> projects)
    {
        var stale = new List<string>();
        foreach (var project in projects)
        {
            if (!File.Exists(Path.Combine(ProjectsDir(root), project.Name, "project.json")))
            {
                stale.Add(Path.Combine("Projects", project.Name, "project.json"));
                continue;
            }
            string dir = Path.Combine(ProjectsDir(root), project.Name);
            foreach (var emitter in Emitter.All)
            {
                string languageDir = Path.Combine(dir, emitter.Folder);
                var files = emitter.Emit(project).ToList();
                var paths = files.Select(f => Path.GetFullPath(Path.Combine(languageDir, f.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                stale.AddRange(OwnedFiles(languageDir).Where(f => !paths.Contains(f)));
                foreach (var file in files)
                {
                    string path = Path.Combine(languageDir, file.Path);
                    if (!File.Exists(path) || File.ReadAllText(path) != Normalize(file.Content))
                        stale.Add(path);
                }
            }
        }
        return stale.Select(p => Path.GetRelativePath(root, p)).ToList();
    }

    static string Normalize(string content) => content.Replace("\r\n", "\n").TrimEnd('\n') + "\n";

    static SourceCount Count(List<SourceFile> files)
    {
        var code = files.Where(f => CodeExtensions.Contains(Path.GetExtension(f.Path))).ToList();
        return new SourceCount(code.Count, code.Sum(f => f.Content.Split('\n').Count(l => l.Trim().Length > 0)));
    }

    static IEnumerable<string> OwnedFiles(string languageDir)
    {
        if (!Directory.Exists(languageDir))
            return [];
        return Directory.EnumerateFiles(languageDir, "*", SearchOption.AllDirectories)
            .Where(f => OwnedExtensions.Contains(Path.GetExtension(f)))
            .Where(f => !Path.GetRelativePath(languageDir, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .SkipLast(1).Any(OutputFolders.Contains))
            .Select(Path.GetFullPath);
    }

    /// <summary>project.json: the app.json fields the runner reads, plus the source counts. A stored expected output is kept.</summary>
    static void WriteProjectJson(string dir, ProjectSpec project, Dictionary<string, SourceCount> counts)
    {
        string path = Path.Combine(dir, "project.json");
        string expected = "";
        if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path))?["profiles"]?["standard"]?["expected"] is JsonNode stored)
            expected = stored.GetValue<string>();
        var node = new JsonObject
        {
            ["name"] = project.Name,
            ["description"] = project.Description,
            ["modules"] = project.Modules.Count,
            ["files"] = project.Files.Count(),
            ["profiles"] = new JsonObject
            {
                ["standard"] = new JsonObject { ["args"] = new JsonArray(), ["expected"] = expected },
            },
            ["sources"] = new JsonObject(counts.Select(c => KeyValuePair.Create<string, JsonNode?>(c.Key,
                new JsonObject { ["files"] = c.Value.Files, ["lines"] = c.Value.Lines }))),
        };
        if (project.TimeoutSeconds is int timeout)
            node["timeoutSeconds"] = timeout;
        string json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n";
        if (!File.Exists(path) || File.ReadAllText(path) != json)
            File.WriteAllText(path, json);
    }
}
