using System.Text.RegularExpressions;
using Runner.Generator;

namespace Runner;

/// <summary>
/// The compile benchmark. For every generated project and language: the timed clean builds of
/// <see cref="Suite.Build"/>, then (for a project with an editable module) timed rebuilds after
/// changing one line of that module, then one run of the program to check its output.
/// </summary>
sealed class CompileSuite(BenchConfig config, Options options, Results results, string resultsDir)
{
    readonly Suite suite = new(config, options, results, resultsDir);

    public void Measure(AppSpec project, IReadOnlyList<Toolchain> toolchains)
    {
        foreach (var toolchain in toolchains)
        {
            var result = results.Case(project.Name, toolchain.Name);
            if (project.Sources?.GetValueOrDefault(toolchain.Folder) is SourceCount sources)
                (result.SourceFiles, result.SourceLines) = (sources.Files, sources.Lines);
            suite.Build(project, toolchain);
            if (result.Ok && EditableFile(project, toolchain) is string file)
                MeasureRebuilds(project, toolchain, result, file);
        }
        Check(project, toolchains);
    }

    /// <summary>
    /// Sets the number on the `bench:edit` line to 1, 2, ... and rebuilds after each change. The
    /// file is restored afterwards and rebuilt untimed, so the program matches the committed source.
    /// </summary>
    void MeasureRebuilds(AppSpec project, Toolchain toolchain, CaseResult result, string file)
    {
        string original = File.ReadAllText(file);
        try
        {
            for (int i = 1; i <= options.BuildRuns; i++)
            {
                File.WriteAllText(file, Edit(original, i));
                var (seconds, peak) = Suite.RunSteps(toolchain.Rebuild(project), suite.Timeout(project));
                result.RebuildSeconds.Add(seconds);
                result.RebuildPeakBytes.Add(peak);
            }
            Console.WriteLine($"  {project.Name,-15}{toolchain.Display,-9} rebuild {string.Join("  ", result.RebuildSeconds.Select(s => $"{s,6:0.00} s"))}   mem {result.RebuildPeakBytes.Max() / 1048576.0,6:0} MiB");
        }
        catch (Exception e)
        {
            result.Fail("rebuild failed: " + e.Message);
            Console.WriteLine($"  {project.Name,-15}{toolchain.Display,-9} REBUILD FAILED  {Suite.FirstLine(e.Message)}");
        }
        finally
        {
            File.WriteAllText(file, original);
            try
            {
                Suite.RunSteps(toolchain.Rebuild(project), suite.Timeout(project));
            }
            catch (Exception e)
            {
                result.Fail("rebuild of the restored source failed: " + e.Message);
            }
        }
    }

    /// <summary>Replaces the first number on the line marked `bench:edit`.</summary>
    static string Edit(string text, int value)
    {
        var lines = text.Split('\n');
        int index = Array.FindIndex(lines, l => l.Contains("bench:edit"));
        if (index < 0)
            throw new InvalidOperationException("no bench:edit line");
        lines[index] = new Regex(@"\d+").Replace(lines[index], value.ToString(), 1);
        return string.Join('\n', lines);
    }

    /// <summary>The source file the generator marked for editing in this language, if the project has one.</summary>
    static string? EditableFile(AppSpec project, Toolchain toolchain)
    {
        var module = ProjectSpec.Find(project.Name)?.Modules.FirstOrDefault(m => m.Editable);
        var emitter = Emitter.All.FirstOrDefault(e => e.Folder == toolchain.Folder);
        return module == null || emitter == null ? null : Path.Combine(toolchain.ProjectDir(project), emitter.ModuleFile(module));
    }

    /// <summary>Runs each program once: it must exit cleanly and print the stored (or agreed) output.</summary>
    void Check(AppSpec project, IReadOnlyList<Toolchain> toolchains)
    {
        var profile = project.Profiles["standard"];
        var checkedCases = new List<CaseResult>();
        foreach (var toolchain in toolchains)
        {
            var result = results.Case(project.Name, toolchain.Name);
            result.Args = profile.Args;
            if (!result.Ok)
                continue;
            string workDir = Path.Combine(resultsDir, "work", project.Name, toolchain.Name);
            Directory.CreateDirectory(workDir);
            try
            {
                var sample = ProcessRunner.Measure(toolchain.Launch(project, profile.Args, workDir), suite.Timeout(project));
                result.Output = Suite.Validate(project, profile, sample, workDir);
                checkedCases.Add(result);
            }
            catch (Exception e)
            {
                result.Fail(e.Message);
                Console.WriteLine($"  {project.Name,-15}{toolchain.Display,-9} FAILED  {Suite.FirstLine(e.Message)}");
            }
        }
        suite.CheckAgreement(project, profile, checkedCases);
    }
}
