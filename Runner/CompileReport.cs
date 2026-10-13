using System.Globalization;
using System.Net;
using System.Text;

namespace Runner;

/// <summary>
/// Writes the compile benchmark's summary.csv, samples.csv, report.md and report.html. The
/// projects are Baseline (the fixed cost of a build: tool start-up, standard library, linking)
/// and one or more larger projects, whose difference from Baseline is the cost of compiling the
/// code itself.
/// </summary>
static class CompileReport
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    const string BaselineProject = "Baseline";

    /// <summary>One row of the summary: a value per language, how to print it, and whether lower is better.</summary>
    sealed record Row(string Title, string Unit, string Format, Func<string, double?> Value, bool LowerIsBetter = true, bool RankHosted = true);

    static double? Median(IReadOnlyList<double> values) => Stats.Summarize(values)?.Median;

    static CaseResult? Find(Results results, string project, string language) =>
        results.Cases.Find(c => c.App == project && c.Language == language && c.Ok);

    /// <summary>The kinds of timed build: clean on all CPUs, clean on one CPU, and rebuild after a one-line edit.</summary>
    enum Build { Clean, OneCpu, Rebuild }

    /// <summary>The metric names of the builds in the CSV files.</summary>
    static readonly (Build Build, string Name)[] BuildNames = [(Build.Clean, "clean"), (Build.OneCpu, "clean_one_cpu"), (Build.Rebuild, "rebuild")];

    static List<double> Seconds(CaseResult c, Build build) =>
        build switch { Build.Clean => c.BuildSeconds, Build.OneCpu => c.OneCpuBuildSeconds, _ => c.RebuildSeconds };

    static List<long> PeakBytes(CaseResult c, Build build) =>
        build switch { Build.Clean => c.BuildPeakBytes, Build.OneCpu => c.OneCpuBuildPeakBytes, _ => c.RebuildPeakBytes };

    static List<double> MiB(IEnumerable<long> bytes) => bytes.Where(b => b > 0).Select(b => b / 1048576.0).ToList();

    /// <summary>Median build time in seconds.</summary>
    static double? Time(Results results, string project, string language, Build build = Build.Clean) =>
        Find(results, project, language) is CaseResult c ? Median(Seconds(c, build)) : null;

    /// <summary>Median peak memory of the builds in MiB; 0 means the platform could not measure it.</summary>
    static double? Memory(Results results, string project, string language, Build build = Build.Clean) =>
        Find(results, project, language) is CaseResult c ? Median(MiB(PeakBytes(c, build))) : null;

    /// <summary>Clean build of the project minus clean build of Baseline: the time spent on the project's own code.</summary>
    static double? CodeSeconds(Results results, string project, string language, Build build = Build.Clean) =>
        Time(results, project, language, build) is double large && Time(results, BaselineProject, language, build) is double small ? large - small : null;

    /// <summary>How many times faster the clean build is on all CPUs than on one.</summary>
    static double? SpeedUp(Results results, string project, string language) =>
        Time(results, project, language, Build.OneCpu) is double one && Time(results, project, language) is double all && all > 0 ? one / all : null;

    /// <summary>The project's lines beyond Baseline's, per second of <see cref="CodeSeconds"/>.</summary>
    static double? LinesPerSecond(Results results, string project, string language, Build build = Build.Clean)
    {
        if (CodeSeconds(results, project, language, build) is not double seconds || seconds <= 0)
            return null;
        int? lines = Find(results, project, language)?.SourceLines - (Find(results, BaselineProject, language)?.SourceLines ?? 0);
        return lines is int l ? l / seconds : null;
    }

    static IEnumerable<string> LargeProjects(Results results) => results.Settings.Apps.Where(p => p != BaselineProject);

    static List<Row> Rows(Results results, string project)
    {
        var rows = new List<Row>
        {
            new($"Clean build of {project}", "s", "0.00", l => Time(results, project, l)),
        };
        bool hasBaseline = results.Settings.Apps.Contains(BaselineProject);
        if (hasBaseline)
        {
            rows.Add(new($"Clean build of {BaselineProject}", "s", "0.00", l => Time(results, BaselineProject, l)));
            rows.Add(new($"{project} − {BaselineProject}", "s", "0.00", l => CodeSeconds(results, project, l)));
            rows.Add(new("Compile speed", "lines/s", "0", l => LinesPerSecond(results, project, l), LowerIsBetter: false));
        }
        rows.Add(new($"Clean build of {project} on one CPU", "s", "0.00", l => Time(results, project, l, Build.OneCpu)));
        if (hasBaseline)
        {
            rows.Add(new($"Clean build of {BaselineProject} on one CPU", "s", "0.00", l => Time(results, BaselineProject, l, Build.OneCpu)));
            rows.Add(new($"{project} − {BaselineProject} on one CPU", "s", "0.00", l => CodeSeconds(results, project, l, Build.OneCpu)));
            rows.Add(new("Compile speed on one CPU", "lines/s", "0", l => LinesPerSecond(results, project, l, Build.OneCpu), LowerIsBetter: false));
        }
        rows.Add(new("Parallel speed-up", "×", "0.00", l => SpeedUp(results, project, l), LowerIsBetter: false));
        rows.Add(new("Rebuild after a one-line edit", "s", "0.00", l => Time(results, project, l, Build.Rebuild)));
        rows.Add(new($"Peak memory, clean build of {project}", "MiB", "0", l => Memory(results, project, l)));
        if (hasBaseline)
            rows.Add(new($"Peak memory, clean build of {BaselineProject}", "MiB", "0", l => Memory(results, BaselineProject, l)));
        rows.Add(new($"Peak memory, clean build of {project} on one CPU", "MiB", "0", l => Memory(results, project, l, Build.OneCpu)));
        rows.Add(new("Peak memory, rebuild", "MiB", "0", l => Memory(results, project, l, Build.Rebuild)));
        rows.Add(new("Source files", "", "0", l => Find(results, project, l)?.SourceFiles));
        rows.Add(new("Source lines", "", "0", l => Find(results, project, l)?.SourceLines));
        rows.Add(new("Executable size", "KiB", "0", l => Find(results, project, l)?.ExecutableBytes / 1024.0, RankHosted: false));
        rows.Add(new("Deployable size", "KiB", "0", l => Find(results, project, l)?.DeployableBytes / 1024.0));
        return rows;
    }

    /// <summary>Rows that are a measurement to rank, not a description of the source.</summary>
    static bool Ranked(Row row) => row.Unit != "";

    public static void Write(Results results, IReadOnlyList<Toolchain> toolchains, string dir)
    {
        File.WriteAllText(Path.Combine(dir, "summary.csv"), SummaryCsv(results));
        File.WriteAllText(Path.Combine(dir, "samples.csv"), SamplesCsv(results));
        File.WriteAllText(Path.Combine(dir, "report.md"), Markdown(results, toolchains));
        File.WriteAllText(Path.Combine(dir, "report.html"), Html(results, toolchains));
    }

    static string Settings(Results results)
    {
        int n = results.Settings.BuildRuns;
        string cpu = results.Settings.Cpu is int k ? $" (CPU {k})" : "";
        return n == 1
            ? $"1 timed clean build on all CPUs, 1 on one CPU{cpu} and 1 rebuild after a one-line edit per project and language"
            : $"{n} timed clean builds on all CPUs, {n} on one CPU{cpu} and {n} rebuilds after a one-line edit per project and language";
    }

    static string SummaryCsv(Results results)
    {
        var csv = new StringBuilder("project,language,metric,unit,median,min,mean,stddev,count,ratio_vs_baseline\n");
        foreach (var c in results.Cases.Where(c => c.Ok))
        {
            void Add(string metric, string unit, IReadOnlyList<double> values, double? baselineMedian)
            {
                if (Stats.Summarize(values) is not Summary s)
                    return;
                string ratio = baselineMedian is double b && b > 0 ? (s.Median / b).ToString("R", Invariant) : "";
                csv.AppendLine(string.Join(',', c.App, c.Language, metric, unit, s.Median.ToString("R", Invariant), s.Min.ToString("R", Invariant),
                    s.Mean.ToString("R", Invariant), s.StdDev.ToString("R", Invariant), s.Count, ratio));
            }
            var baseline = Find(results, c.App, results.Settings.Baseline);
            string reference = results.Settings.Baseline;
            foreach (var (build, name) in BuildNames)
            {
                Add(name, "s", Seconds(c, build), baseline != null ? Median(Seconds(baseline, build)) : null);
                Add(name + "_memory", "MiB", MiB(PeakBytes(c, build)), Memory(results, c.App, reference, build));
            }
            if (SpeedUp(results, c.App, c.Language) is double speedUp)
                Add("speedup", "x", [speedUp], SpeedUp(results, c.App, reference));
            foreach (var (build, suffix) in new[] { (Build.Clean, ""), (Build.OneCpu, "_one_cpu") })
            {
                if (c.App != BaselineProject && CodeSeconds(results, c.App, c.Language, build) is double code)
                    Add("code" + suffix, "s", [code], CodeSeconds(results, c.App, reference, build));
                if (c.App != BaselineProject && LinesPerSecond(results, c.App, c.Language, build) is double speed)
                    Add("speed" + suffix, "lines/s", [speed], LinesPerSecond(results, c.App, reference, build));
            }
            if (c.ExecutableBytes is long exe)
                Add("executable", "KiB", [exe / 1024.0], baseline?.ExecutableBytes / 1024.0);
            if (c.DeployableBytes is long deploy)
                Add("deployable", "KiB", [deploy / 1024.0], baseline?.DeployableBytes / 1024.0);
        }
        return csv.ToString();
    }

    static string SamplesCsv(Results results)
    {
        var csv = new StringBuilder("project,language,metric,unit,index,value\n");
        foreach (var c in results.Cases)
        {
            foreach (var (build, name) in BuildNames)
            {
                var seconds = Seconds(c, build);
                for (int i = 0; i < seconds.Count; i++)
                    csv.AppendLine($"{c.App},{c.Language},{name},s,{i},{seconds[i].ToString("R", Invariant)}");
                var bytes = PeakBytes(c, build);
                for (int i = 0; i < bytes.Count; i++)
                    csv.AppendLine($"{c.App},{c.Language},{name}_memory,MiB,{i},{(bytes[i] / 1048576.0).ToString("R", Invariant)}");
            }
        }
        return csv.ToString();
    }

    static IEnumerable<string> Notes(Results results, IReadOnlyList<Toolchain> toolchains)
    {
        var baseline = toolchains.First(t => t.Name == results.Settings.Baseline);
        yield return "The projects are generated (`dotnet run --project Runner -c Release -- generate`) from one specification, so every language compiles the same modules, types and functions and prints the same result.";
        yield return $"{BaselineProject} has Main and one module, with the same build setup and dependencies as the large project: its clean build is the fixed cost of a build (starting the tools, the standard library, linking, packaging). The difference between the two is the cost of compiling the code itself, and compile speed is the difference in lines divided by it.";
        yield return $"{baseline.Display} compiles its standard library from source as part of every build, while the other languages use a precompiled one, so most of {baseline.Display}'s {BaselineProject} time is spent on the standard library.";
        yield return "A clean build removes the build output first: Ninja's object files (CMake stays configured), Cargo's target folder, the Go build cache except a precompiled standard library, and the MSBuild and Java output folders.";
        yield return "A rebuild changes one line in the last module (which only Main calls) and builds again without cleaning: what each build tool does incrementally. Release Cargo builds are not incremental, C# recompiles the project, javac recompiles every file, and Java JIT keeps its jlink runtime.";
        yield return "Peak memory is the build's highest memory use. On Windows it is the memory committed by all of the build's processes together (the build tool and every compiler, linker or server it starts, tracked by a job object), so parallel compilers add up. On Linux it is the peak resident set of the build's largest single process, so the two are not comparable across operating systems. For a build of several steps (Java), it is the step that used the most.";
        yield return "C++ builds with CMake and Ninja, which compiles the translation units in parallel (on Windows CMake links with lld, its default for clang); the other build tools use as many cores as they normally do.";
        yield return "A build on one CPU is the same clean build with the runner and every process the build starts pinned to one logical CPU (CPU affinity, which child processes inherit), so the build gets one CPU's worth of time whatever it does. Cargo and Ninja are also told to run one job (--jobs 1, --parallel 1) rather than relying on them to read the affinity. The parallel speed-up is the clean build on one CPU divided by the clean build on all CPUs: how much faster the build gets from the other CPUs.";
        foreach (var note in toolchains.Select(t => t.Note).OfType<string>())
            yield return note;
    }

    // ------------------------------------------------------------------ Markdown

    static string Markdown(Results results, IReadOnlyList<Toolchain> toolchains)
    {
        var languages = toolchains.Where(t => results.Settings.Languages.Contains(t.Name)).ToList();
        var baseline = toolchains.First(t => t.Name == results.Settings.Baseline);
        var md = new StringBuilder();
        md.AppendLine("# Compile benchmark report").AppendLine();
        md.AppendLine(Report.Header(results, md: true, Settings(results))).AppendLine();
        foreach (string project in LargeProjects(results))
        {
            md.AppendLine($"## {project}").AppendLine();
            md.AppendLine("Medians. Lower is better except for compile speed and parallel speed-up; the best value in each row is bold.").AppendLine();
            md.AppendLine(MarkdownTable(results, project, languages, ratios: false)).AppendLine();
            md.AppendLine($"Relative to {baseline.Display} (*value ÷ {baseline.Display} value*; for compile speed and parallel speed-up, above 1.00× is more than {baseline.Display}):").AppendLine();
            md.AppendLine(MarkdownTable(results, project, languages, ratios: true)).AppendLine();
        }
        var failures = results.Cases.Where(c => !c.Ok).ToList();
        if (failures.Count > 0)
        {
            md.AppendLine("## Failures").AppendLine();
            foreach (var c in failures)
                md.AppendLine($"- **{c.App} / {c.Language}**: {c.Error?.Split('\n')[0]}");
            md.AppendLine();
        }
        md.AppendLine("## Notes").AppendLine();
        foreach (string note in Notes(results, toolchains))
            md.AppendLine("- " + note);
        return md.ToString();
    }

    static string MarkdownTable(Results results, string project, List<Toolchain> languages, bool ratios)
    {
        var table = new StringBuilder();
        table.AppendLine("| | " + string.Join(" | ", languages.Select(t => t.Display)) + " |");
        table.AppendLine("|---|" + string.Concat(languages.Select(_ => "---:|")));
        foreach (var row in Rows(results, project).Where(r => !ratios || Ranked(r)))
        {
            var cells = Cells(results, row, languages, ratios).Select(c => c.Best ? $"**{c.Text}**" : c.Hosted ? $"*{c.Text}*" : c.Text);
            string unit = row.Unit.Length > 0 && !ratios ? $" ({row.Unit})" : "";
            table.AppendLine($"| {row.Title}{unit} | " + string.Join(" | ", cells) + " |");
        }
        return table.ToString().TrimEnd();
    }

    /// <summary>The printed cells of a row: values or ratios, which one is best, and which are shown but not ranked.</summary>
    static List<(string Text, bool Best, bool Hosted)> Cells(Results results, Row row, List<Toolchain> languages, bool ratios)
    {
        string baseline = results.Settings.Baseline;
        double? reference = row.Value(baseline);
        var values = languages.Select(t => row.Value(t.Name) is double v && (!ratios || reference is > 0) ? ratios ? v / reference!.Value : v : (double?)null).ToList();
        bool Hosted(int i) => !row.RankHosted && languages[i].RuntimeHosted;
        var ranked = values.Where((v, i) => v != null && !Hosted(i)).Select(v => v!.Value).ToList();
        double? best = !Ranked(row) || ranked.Count < 2 ? null : row.LowerIsBetter ? ranked.Min() : ranked.Max();
        return values.Select((v, i) => v is double value
            ? (ratios ? Report.Times(value) : Report.Number(value, row.Format), value == best && !Hosted(i), Hosted(i))
            : (results.Cases.Any(c => c.Language == languages[i].Name && !c.Ok) ? "FAIL" : "–", false, false)).ToList();
    }

    public static string ConsoleSummary(Results results, IReadOnlyList<Toolchain> toolchains)
    {
        var languages = toolchains.Where(t => results.Settings.Languages.Contains(t.Name)).ToList();
        var text = new StringBuilder();
        foreach (string project in LargeProjects(results))
        {
            text.AppendLine($"{project} (medians; lower is better except compile speed and parallel speed-up)");
            text.AppendLine($"{"",-52}" + string.Concat(languages.Select(t => $"{t.Display,10}")));
            foreach (var row in Rows(results, project).Where(Ranked))
                text.AppendLine($"{row.Title + (row.Unit.Length > 0 ? $" ({row.Unit})" : ""),-52}" +
                    string.Concat(Cells(results, row, languages, ratios: false).Select(c => $"{c.Text,10}")));
        }
        return text.ToString();
    }

    // ------------------------------------------------------------------ HTML

    static readonly string[] SeriesVars = ["--series-1", "--series-2", "--series-3", "--series-4", "--series-5", "--series-6", "--series-7", "--series-8"];

    static string Html(Results results, IReadOnlyList<Toolchain> toolchains)
    {
        var languages = toolchains.Where(t => results.Settings.Languages.Contains(t.Name)).ToList();
        var baseline = toolchains.First(t => t.Name == results.Settings.Baseline);
        var html = new StringBuilder(Report.HtmlStart("Compile benchmark", Report.Header(results, md: false, Settings(results))));
        bool hasBaseline = results.Settings.Apps.Contains(BaselineProject);
        foreach (string project in LargeProjects(results))
        {
            html.Append($"<h2>Clean build of {project}</h2>");
            html.Append(hasBaseline
                ? $"<p>Each bar is a full clean release build. The pale part is the build of {BaselineProject} (one module, same setup): the fixed cost of starting the tools, the standard library and linking. The coloured part is the rest: compiling {project}'s own code. Shorter is better.</p>"
                : "<p>Each bar is a full clean release build. Shorter is better.</p>");
            html.Append("<div class=\"card\">").Append(Bars(results, languages,
                l => hasBaseline ? Time(results, BaselineProject, l) : null, l => Time(results, project, l), "s")).Append("</div>");

            html.Append($"<h2>Clean build of {project} on one CPU</h2>");
            html.Append($"<p>Each bar is a clean build with every build process pinned to one logical CPU. The pale part is the same build on all {results.Machine.LogicalCores} logical CPUs, so the coloured part is the time the other CPUs save. Shorter is better.</p>");
            html.Append("<div class=\"card\">").Append(Bars(results, languages,
                l => Time(results, project, l), l => Time(results, project, l, Build.OneCpu), "s", partLabel: "on all CPUs")).Append("</div>");

            html.Append($"<h2>Peak memory of the clean build of {project}</h2>");
            html.Append(hasBaseline
                ? $"<p>The most memory the build used at once, all of its processes together (Windows) or its largest process (Linux). The pale part is what the build of {BaselineProject} needed. Shorter is better.</p>"
                : "<p>The most memory the build used at once. Shorter is better.</p>");
            html.Append("<div class=\"card\">").Append(Bars(results, languages,
                l => hasBaseline ? Memory(results, BaselineProject, l) : null, l => Memory(results, project, l), "MiB", "0")).Append("</div>");

            html.Append($"<h2>Rebuild of {project} after a one-line edit</h2>");
            html.Append("<p>One line of the last module changes, and the project builds again without cleaning. Shorter is better.</p>");
            html.Append("<div class=\"card\">").Append(Bars(results, languages, _ => null, l => Time(results, project, l, Build.Rebuild), "s")).Append("</div>");

            html.Append($"<h2>Summary of {project}</h2><p>Medians. Lower is better except for compile speed and parallel speed-up; the best value in each row is bold, and ratios are relative to {baseline.Display}.</p>");
            html.Append("<div class=\"card\">").Append(HtmlTable(results, project, languages)).Append("</div>");
        }
        var failures = results.Cases.Where(c => !c.Ok).ToList();
        if (failures.Count > 0)
            html.Append("<h2>Failures</h2><ul>" + string.Concat(failures.Select(c =>
                $"<li><strong>{c.App} / {c.Language}</strong>: {WebUtility.HtmlEncode(c.Error?.Split('\n')[0] ?? "")}</li>")) + "</ul>");
        html.Append("<h2>Notes</h2><ul>" + string.Concat(Notes(results, toolchains).Select(n => $"<li>{WebUtility.HtmlEncode(n).Replace("`", "")}</li>")) + "</ul>");
        html.Append(Report.HtmlEnd);
        return html.ToString();
    }

    static string HtmlTable(Results results, string project, List<Toolchain> languages)
    {
        var html = new StringBuilder("<table><tr><th></th>");
        html.Append(string.Concat(languages.Select(t => $"<th>{WebUtility.HtmlEncode(t.Display)}</th>"))).Append("</tr>");
        foreach (var row in Rows(results, project))
        {
            var values = Cells(results, row, languages, ratios: false);
            var ratios = Ranked(row) ? Cells(results, row, languages, ratios: true) : null;
            html.Append($"<tr><td>{WebUtility.HtmlEncode(row.Title)}{(row.Unit.Length > 0 ? $" ({row.Unit})" : "")}</td>");
            for (int i = 0; i < languages.Count; i++)
            {
                var (text, best, hosted) = values[i];
                string ratio = ratios != null && ratios[i].Text.EndsWith('×') ? $" <small>({ratios[i].Text})</small>" : "";
                string style = hosted ? " class=\"hosted\"" : best ? " class=\"best\"" : text is "FAIL" or "–" ? " class=\"fail\"" : "";
                html.Append($"<td{style}>{text}{ratio}</td>");
            }
            html.Append("</tr>");
        }
        return html.Append("</table>").ToString();
    }

    /// <summary>
    /// One horizontal bar per language on a linear axis; the part up to <paramref name="fixedPart"/>
    /// is drawn pale, and its tooltip calls it <paramref name="partLabel"/>.
    /// </summary>
    static string Bars(Results results, List<Toolchain> languages, Func<string, double?> fixedPart, Func<string, double?> total, string unit,
        string format = "0.00", string partLabel = "for " + BaselineProject)
    {
        const double Left = 90, Right = 870, Top = 8, RowHeight = 30, BarHeight = 18;
        var totals = languages.Select(t => total(t.Name)).ToList();
        double max = totals.OfType<double>().DefaultIfEmpty(0).Max();
        if (max <= 0)
            return "<p class=\"meta\">Not measured.</p>";
        double step = NiceStep(max / 5);
        double axisMax = Math.Ceiling(max / step) * step;
        double X(double v) => Left + v / axisMax * (Right - Left);
        double height = Top + languages.Count * RowHeight + 26;
        var svg = new StringBuilder($"<svg viewBox=\"0 0 960 {Report.Number(height, "0")}\" role=\"img\" aria-label=\"{unit} by language\">");
        double axisY = Top + languages.Count * RowHeight;
        for (double tick = 0; tick <= axisMax + step / 2; tick += step)
        {
            string x = Report.Number(X(tick), "0.0");
            svg.Append($"<line class=\"grid\" x1=\"{x}\" x2=\"{x}\" y1=\"{Top}\" y2=\"{Report.Number(axisY, "0")}\"/>");
            svg.Append($"<text x=\"{x}\" y=\"{Report.Number(axisY + 18, "0")}\" text-anchor=\"middle\">{tick.ToString("0.##", Invariant)}</text>");
        }
        svg.Append($"<text x=\"{Right}\" y=\"{Report.Number(axisY + 18, "0")}\" text-anchor=\"start\" dx=\"16\">{unit}</text>");
        for (int i = 0; i < languages.Count; i++)
        {
            double y = Top + i * RowHeight + (RowHeight - BarHeight) / 2;
            string color = $"var({SeriesVars[i % SeriesVars.Length]})";
            svg.Append($"<text class=\"app\" x=\"{Left - 12}\" y=\"{Report.Number(y + BarHeight - 4, "0")}\" text-anchor=\"end\">{WebUtility.HtmlEncode(languages[i].Display)}</text>");
            if (totals[i] is not double value)
            {
                string missing = results.Cases.Any(c => c.Language == languages[i].Name && !c.Ok) ? "FAIL" : "not measured";
                svg.Append($"<text x=\"{Left + 6}\" y=\"{Report.Number(y + BarHeight - 4, "0")}\">{missing}</text>");
                continue;
            }
            double part = Math.Clamp(fixedPart(languages[i].Name) ?? 0, 0, value);
            string tip = $"{languages[i].Display}: {value.ToString(format, Invariant)} {unit}" +
                (part > 0 ? $" ({part.ToString(format, Invariant)} {partLabel})" : "");
            string h = Report.Number(BarHeight, "0"), top = Report.Number(y, "0.0");
            if (part > 0)
                svg.Append($"<rect x=\"{Left}\" y=\"{top}\" width=\"{Report.Number(X(part) - Left, "0.0")}\" height=\"{h}\" fill=\"{color}\" opacity=\"0.35\" data-tip=\"{WebUtility.HtmlEncode(tip)}\"/>");
            svg.Append($"<rect x=\"{Report.Number(X(part), "0.0")}\" y=\"{top}\" width=\"{Report.Number(Math.Max(X(value) - X(part), 0.5), "0.0")}\" height=\"{h}\" fill=\"{color}\" data-tip=\"{WebUtility.HtmlEncode(tip)}\"><title>{WebUtility.HtmlEncode(tip)}</title></rect>");
            svg.Append($"<text class=\"value\" x=\"{Report.Number(X(value) + 6, "0.0")}\" y=\"{Report.Number(y + BarHeight - 4, "0")}\">{value.ToString(format, Invariant)}</text>");
        }
        return svg.Append("</svg>").ToString();
    }

    /// <summary>1, 2 or 5 times a power of ten, at least <paramref name="raw"/>.</summary>
    static double NiceStep(double raw)
    {
        double power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        return new[] { 1.0, 2.0, 5.0, 10.0 }.Select(m => m * power).First(s => s >= raw);
    }
}
