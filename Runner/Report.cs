using System.Globalization;
using System.Net;
using System.Text;

namespace Runner;

/// <summary>Writes summary.csv, samples.csv, report.md and report.html from the results.</summary>
static class Report
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Median of one metric for one app and language, or null when it failed or was not measured.</summary>
    static double? Median(Results results, string app, string language, Metric metric)
    {
        var c = results.Cases.Find(x => x.App == app && x.Language == language);
        return c is { Ok: true } ? Stats.Summarize(metric.Values(c))?.Median : null;
    }

    static bool Measured(Results results, Metric metric) => results.Cases.Any(c => c.Ok && metric.Values(c).Count > 0);

    static double? Ratio(Results results, string app, string language, Metric metric)
    {
        double? value = Median(results, app, language, metric);
        double? baseline = Median(results, app, results.Settings.Baseline, metric);
        return value is double v && baseline is double b && b > 0 ? v / b : null;
    }

    /// <summary>Geometric mean over apps of value ÷ baseline value.</summary>
    static double? GeoMeanRatio(Results results, string language, Metric metric)
    {
        var ratios = results.Settings.Apps.Select(a => Ratio(results, a, language, metric)).OfType<double>().ToList();
        return ratios.Count == results.Settings.Apps.Count ? Stats.GeometricMean(ratios) : null;
    }

    static string Number(double value, string format) => value.ToString(format, Invariant);
    static string Times(double ratio) => ratio >= 10 ? $"{ratio.ToString("0", Invariant)}×" : $"{ratio.ToString("0.00", Invariant)}×";

    public static void Write(Results results, IReadOnlyList<Toolchain> toolchains, string dir)
    {
        File.WriteAllText(Path.Combine(dir, "summary.csv"), SummaryCsv(results));
        File.WriteAllText(Path.Combine(dir, "samples.csv"), SamplesCsv(results));
        File.WriteAllText(Path.Combine(dir, "report.md"), Markdown(results, toolchains));
        File.WriteAllText(Path.Combine(dir, "report.html"), Html(results, toolchains));
    }

    static string SummaryCsv(Results results)
    {
        var csv = new StringBuilder("app,language,metric,unit,median,min,mean,stddev,count,ratio_vs_baseline\n");
        foreach (var metric in Metric.All)
            foreach (var c in results.Cases.Where(c => c.Ok))
                if (Stats.Summarize(metric.Values(c)) is Summary s)
                    csv.AppendLine(string.Join(',', c.App, c.Language, metric.Key, metric.Unit,
                        s.Median.ToString("R", Invariant), s.Min.ToString("R", Invariant), s.Mean.ToString("R", Invariant),
                        s.StdDev.ToString("R", Invariant), s.Count, Ratio(results, c.App, c.Language, metric)?.ToString("R", Invariant) ?? ""));
        return csv.ToString();
    }

    static string SamplesCsv(Results results)
    {
        var csv = new StringBuilder("app,language,metric,unit,index,value\n");
        foreach (var metric in Metric.All)
            foreach (var c in results.Cases)
            {
                var values = metric.Values(c);
                for (int i = 0; i < values.Count; i++)
                    csv.AppendLine($"{c.App},{c.Language},{metric.Key},{metric.Unit},{i},{values[i].ToString("R", Invariant)}");
            }
        return csv.ToString();
    }

    // ------------------------------------------------------------------ Markdown

    static string Markdown(Results results, IReadOnlyList<Toolchain> toolchains)
    {
        var languages = toolchains.Where(t => results.Settings.Languages.Contains(t.Name)).ToList();
        var baseline = toolchains.First(t => t.Name == results.Settings.Baseline);
        var md = new StringBuilder();
        md.AppendLine("# Benchmark report").AppendLine();
        md.AppendLine(Header(results, md: true)).AppendLine();

        md.AppendLine($"## Summary: geometric mean relative to {baseline.Display}").AppendLine();
        md.AppendLine($"Each cell is the geometric mean, over all apps, of *value ÷ {baseline.Display} value*. Every metric is lower-is-better, so below 1.00× beats {baseline.Display} and above 1.00× loses to it.").AppendLine();
        md.AppendLine("| Metric | " + string.Join(" | ", languages.Select(t => t.Display)) + " |");
        md.AppendLine("|---|" + string.Concat(languages.Select(_ => "---:|")));
        foreach (var metric in Metric.All.Where(m => Measured(results, m)))
            md.AppendLine($"| {metric.Title} | " + string.Join(" | ", languages.Select(t => GeoMeanRatio(results, t.Name, metric) is double g ? Times(g) : "–")) + " |");
        md.AppendLine();

        foreach (var metric in Metric.All.Where(m => Measured(results, m)))
        {
            md.AppendLine($"## {metric.Title} ({metric.Unit})").AppendLine();
            md.AppendLine(metric.Description + " Lower is better; the best value in each row is bold.").AppendLine();
            md.AppendLine(MarkdownTable(results, languages, metric, ratios: false)).AppendLine();
            md.AppendLine($"Relative to {baseline.Display}:").AppendLine();
            md.AppendLine(MarkdownTable(results, languages, metric, ratios: true)).AppendLine();
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
        foreach (string note in Notes(toolchains))
            md.AppendLine("- " + note);
        return md.ToString();
    }

    static string MarkdownTable(Results results, List<Toolchain> languages, Metric metric, bool ratios)
    {
        var table = new StringBuilder();
        table.AppendLine("| App | " + string.Join(" | ", languages.Select(t => t.Display)) + " |");
        table.AppendLine("|---|" + string.Concat(languages.Select(_ => "---:|")));
        foreach (string app in results.Settings.Apps)
        {
            var values = languages.Select(t => ratios ? Ratio(results, app, t.Name, metric) : Median(results, app, t.Name, metric)).ToList();
            double? best = values.OfType<double>().DefaultIfEmpty().Min();
            var cells = values.Select((v, i) =>
            {
                if (v is not double value)
                    return results.Cases.Any(c => c.App == app && c.Language == languages[i].Name && !c.Ok) ? "FAIL" : "–";
                string text = ratios ? Times(value) : Number(value, metric.Format);
                return value == best && values.Count(x => x != null) > 1 ? $"**{text}**" : text;
            });
            table.AppendLine($"| {app} | " + string.Join(" | ", cells) + " |");
        }
        if (ratios)
            table.AppendLine("| *Geometric mean* | " + string.Join(" | ", languages.Select(t => GeoMeanRatio(results, t.Name, metric) is double g ? $"*{Times(g)}*" : "–")) + " |");
        return table.ToString().TrimEnd();
    }

    static string Header(Results results, bool md)
    {
        var m = results.Machine;
        var s = results.Settings;
        var lines = new List<string>
        {
            $"Machine: {m.Cpu}, {m.LogicalCores} logical cores, {m.MemoryBytes / 1073741824.0:0} GiB, {m.Os}",
            "Toolchains: " + string.Join(" · ", m.Toolchains.Select(t => $"{t.Key}: {ShortVersion(t.Value)}")),
            $"Profile: {results.Profile} · {s.BuildRuns} timed clean builds · {s.Warmups} warm-up + {s.Runs} measured runs" + (s.Cpu is int cpu ? $" · pinned to CPU {cpu}" : ""),
            $"Date: {results.StartedAt:yyyy-MM-dd HH:mm}" + (m.Commit != null ? $" · commit {m.Commit}" : ""),
        };
        return md ? string.Join("  \n", lines) : string.Join("<br>", lines.Select(WebUtility.HtmlEncode));
    }

    /// <summary>"clang version 23.1.2 (https://github.com/...)" becomes "clang version 23.1.2".</summary>
    static string ShortVersion(string? version) =>
        version == null ? "missing" : System.Text.RegularExpressions.Regex.Replace(version, @"\s*\((https?://|[0-9a-f]{7,}|20\d\d-)[^)]*\)", "");

    static IEnumerable<string> Notes(IReadOnlyList<Toolchain> toolchains)
    {
        yield return "Each app implements the same algorithm by hand in every language; only standard-library I/O and collections are used.";
        yield return "Execution time includes process start-up. Compile time is a full clean release build through the language's usual build tool (rux, cargo, clang++, dotnet publish).";
        yield return "BinaryTrees allocates with new/delete (C++), Box (Rust), the GC (C#) and Allocator::Pool (Rux).";
        yield return "WordCount uses each standard library's hash map with its default hash function.";
        yield return "Peak memory is the working set on Windows and maxrss on Linux; the two are not comparable across operating systems.";
        foreach (var note in toolchains.Select(t => t.Note).OfType<string>())
            yield return note;
    }

    public static string ConsoleSummary(Results results, IReadOnlyList<Toolchain> toolchains)
    {
        var languages = toolchains.Where(t => results.Settings.Languages.Contains(t.Name)).ToList();
        var baseline = toolchains.First(t => t.Name == results.Settings.Baseline);
        var text = new StringBuilder();
        text.AppendLine($"Geometric mean relative to {baseline.Display} (lower is better; below 1.00× beats {baseline.Display})");
        text.AppendLine($"{"",-18}" + string.Concat(languages.Select(t => $"{t.Display,10}")));
        foreach (var metric in Metric.All.Where(m => Measured(results, m)))
            text.AppendLine($"{metric.Title,-18}" + string.Concat(languages.Select(t => $"{(GeoMeanRatio(results, t.Name, metric) is double g ? Times(g) : "–"),10}")));
        return text.ToString();
    }

    // ------------------------------------------------------------------ HTML

    static readonly string[] SeriesVars = ["--series-1", "--series-2", "--series-3", "--series-4", "--series-5"];

    static string Html(Results results, IReadOnlyList<Toolchain> toolchains)
    {
        var languages = toolchains.Where(t => results.Settings.Languages.Contains(t.Name)).ToList();
        var baseline = toolchains.First(t => t.Name == results.Settings.Baseline);
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Benchmark Report</title>
            <style>
            :root {
              color-scheme: light;
              --page: #f9f9f7; --surface: #fcfcfb; --ink: #0b0b0b; --ink-2: #52514e; --muted: #898781;
              --grid: #e1e0d9; --axis: #c3c2b7; --border: rgba(11,11,11,0.10);
              --series-1: #2a78d6; --series-2: #eb6834; --series-3: #1baf7a; --series-4: #eda100; --series-5: #e87ba4;
            }
            @media (prefers-color-scheme: dark) {
              :root:not([data-theme="light"]) {
                color-scheme: dark;
                --page: #0d0d0d; --surface: #1a1a19; --ink: #ffffff; --ink-2: #c3c2b7; --muted: #898781;
                --grid: #2c2c2a; --axis: #383835; --border: rgba(255,255,255,0.10);
                --series-1: #3987e5; --series-2: #d95926; --series-3: #199e70; --series-4: #c98500; --series-5: #d55181;
              }
            }
            :root[data-theme="dark"] {
              color-scheme: dark;
              --page: #0d0d0d; --surface: #1a1a19; --ink: #ffffff; --ink-2: #c3c2b7; --muted: #898781;
              --grid: #2c2c2a; --axis: #383835; --border: rgba(255,255,255,0.10);
              --series-1: #3987e5; --series-2: #d95926; --series-3: #199e70; --series-4: #c98500; --series-5: #d55181;
            }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--page); color: var(--ink); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
            main { max-width: 980px; margin: 0 auto; padding: 32px 16px 64px; }
            h1 { font-size: 28px; margin: 0 0 8px; }
            h2 { font-size: 20px; margin: 40px 0 4px; }
            p, .meta { color: var(--ink-2); margin: 4px 0 12px; }
            .meta { font-size: 13px; }
            .card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 16px; margin: 12px 0; overflow-x: auto; }
            table { border-collapse: collapse; width: 100%; font-variant-numeric: tabular-nums; font-size: 14px; }
            th, td { padding: 6px 10px; text-align: right; border-bottom: 1px solid var(--grid); white-space: nowrap; }
            th:first-child, td:first-child { text-align: left; }
            th { color: var(--ink-2); font-weight: 600; }
            td.best { font-weight: 700; }
            td.fail { color: var(--muted); }
            tr.mean td { color: var(--ink-2); font-style: italic; }
            .legend { display: flex; flex-wrap: wrap; gap: 6px 18px; font-size: 13px; color: var(--ink-2); margin-bottom: 8px; }
            .legend span::before { content: ""; display: inline-block; width: 10px; height: 10px; border-radius: 50%; margin-right: 6px; background: var(--c); vertical-align: -1px; }
            svg { display: block; width: 100%; height: auto; }
            svg text { fill: var(--muted); font-size: 12px; }
            svg text.app { fill: var(--ink-2); font-size: 13px; }
            svg .grid { stroke: var(--grid); }
            svg .row { stroke: var(--grid); stroke-dasharray: 2 4; }
            svg circle { stroke: var(--surface); stroke-width: 2; }
            svg circle:hover { stroke: var(--ink); }
            details summary { cursor: pointer; color: var(--ink-2); font-size: 14px; margin-top: 8px; }
            #tip { position: fixed; pointer-events: none; background: var(--surface); color: var(--ink); border: 1px solid var(--border);
                   border-radius: 8px; padding: 6px 10px; font-size: 13px; box-shadow: 0 4px 12px rgba(0,0,0,0.15); display: none; }
            ul { color: var(--ink-2); padding-left: 20px; }
            </style>
            </head>
            <body>
            <main>
            <h1>Benchmark report</h1>
            <div class="meta">{{Header(results, md: false)}}</div>
            """);

        string legend = "<div class=\"legend\">" + string.Concat(languages.Select((t, i) =>
            $"<span style=\"--c: var({SeriesVars[i % SeriesVars.Length]})\">{WebUtility.HtmlEncode(t.Display)}</span>")) + "</div>";

        html.Append($"<h2>Summary</h2><p>Geometric mean over all apps of <em>value ÷ {baseline.Display} value</em>. Every metric is lower-is-better: below 1.00× beats {baseline.Display}.</p>");
        html.Append("<div class=\"card\"><table><tr><th>Metric</th>" + string.Concat(languages.Select(t => $"<th>{WebUtility.HtmlEncode(t.Display)}</th>")) + "</tr>");
        foreach (var metric in Metric.All.Where(m => Measured(results, m)))
            html.Append($"<tr><td>{metric.Title}</td>" + string.Concat(languages.Select(t => $"<td>{(GeoMeanRatio(results, t.Name, metric) is double g ? Times(g) : "–")}</td>")) + "</tr>");
        html.Append("</table></div>");

        foreach (var metric in Metric.All.Where(m => Measured(results, m)))
        {
            html.Append($"<h2>{metric.Title}</h2><p>{WebUtility.HtmlEncode(metric.Description)} Lower is better. Logarithmic scale: each grid step is a constant factor.</p>");
            html.Append("<div class=\"card\">").Append(legend).Append(DotPlot(results, languages, metric));
            html.Append("<details><summary>Table</summary>").Append(HtmlTable(results, languages, metric)).Append("</details></div>");
        }

        var failures = results.Cases.Where(c => !c.Ok).ToList();
        if (failures.Count > 0)
            html.Append("<h2>Failures</h2><ul>" + string.Concat(failures.Select(c =>
                $"<li><strong>{c.App} / {c.Language}</strong>: {WebUtility.HtmlEncode(c.Error?.Split('\n')[0] ?? "")}</li>")) + "</ul>");
        html.Append("<h2>Notes</h2><ul>" + string.Concat(Notes(toolchains).Select(n => $"<li>{WebUtility.HtmlEncode(n)}</li>")) + "</ul>");
        html.Append("""
            </main>
            <div id="tip"></div>
            <script>
            const tip = document.getElementById("tip");
            document.querySelectorAll("circle[data-tip]").forEach(dot => {
              dot.addEventListener("mousemove", e => {
                tip.textContent = dot.dataset.tip;
                tip.style.display = "block";
                tip.style.left = Math.min(e.clientX + 14, innerWidth - tip.offsetWidth - 8) + "px";
                tip.style.top = (e.clientY + 14) + "px";
              });
              dot.addEventListener("mouseleave", () => tip.style.display = "none");
            });
            </script>
            </body>
            </html>
            """);
        return html.ToString();
    }

    static string HtmlTable(Results results, List<Toolchain> languages, Metric metric)
    {
        var html = new StringBuilder($"<table><tr><th>App ({metric.Unit})</th>");
        html.Append(string.Concat(languages.Select(t => $"<th>{WebUtility.HtmlEncode(t.Display)}</th>"))).Append("</tr>");
        foreach (string app in results.Settings.Apps)
        {
            var values = languages.Select(t => Median(results, app, t.Name, metric)).ToList();
            double? best = values.OfType<double>().DefaultIfEmpty().Min();
            html.Append($"<tr><td>{app}</td>");
            for (int i = 0; i < languages.Count; i++)
            {
                if (values[i] is double v)
                {
                    string ratio = Ratio(results, app, languages[i].Name, metric) is double r ? $" <small>({Times(r)})</small>" : "";
                    html.Append($"<td{(v == best ? " class=\"best\"" : "")}>{Number(v, metric.Format)}{ratio}</td>");
                }
                else
                {
                    html.Append("<td class=\"fail\">–</td>");
                }
            }
            html.Append("</tr>");
        }
        html.Append("<tr class=\"mean\"><td>Geometric mean</td>" + string.Concat(languages.Select(t =>
            $"<td>{(GeoMeanRatio(results, t.Name, metric) is double g ? Times(g) : "–")}</td>")) + "</tr></table>");
        return html.ToString();
    }

    /// <summary>One row per app, one dot per language, on a logarithmic axis.</summary>
    static string DotPlot(Results results, List<Toolchain> languages, Metric metric)
    {
        const double Left = 130, Right = 930, Top = 12, RowHeight = 30;
        var apps = results.Settings.Apps;
        double height = Top + apps.Count * RowHeight + 28;
        var values = apps.SelectMany(a => languages.Select(t => Median(results, a, t.Name, metric))).OfType<double>().Where(v => v > 0).ToList();
        if (values.Count == 0)
            return "";
        double low = Math.Floor(Math.Log10(values.Min())), high = Math.Ceiling(Math.Log10(values.Max()));
        if (high - low < 1)
            high = low + 1;
        double X(double v) => Left + (Math.Log10(v) - low) / (high - low) * (Right - Left);

        var svg = new StringBuilder($"<svg viewBox=\"0 0 960 {Number(height, "0")}\" role=\"img\" aria-label=\"{metric.Title} by app and language\">");
        double axisY = Top + apps.Count * RowHeight;
        for (double e = low; e <= high; e++)
        {
            foreach (double m in new[] { 1.0, 2.0, 5.0 })
            {
                double tick = m * Math.Pow(10, e);
                if (Math.Log10(tick) > high + 1e-9)
                    break;
                double x = X(tick);
                svg.Append($"<line class=\"grid\" x1=\"{Number(x, "0.0")}\" x2=\"{Number(x, "0.0")}\" y1=\"{Top}\" y2=\"{Number(axisY, "0")}\"/>");
                svg.Append($"<text x=\"{Number(x, "0.0")}\" y=\"{Number(axisY + 18, "0")}\" text-anchor=\"middle\">{TickLabel(tick)}</text>");
            }
        }
        svg.Append($"<text x=\"{Right}\" y=\"{Number(axisY + 18, "0")}\" text-anchor=\"end\" dx=\"24\">{metric.Unit}</text>");
        for (int row = 0; row < apps.Count; row++)
        {
            double y = Top + row * RowHeight + RowHeight / 2;
            svg.Append($"<line class=\"row\" x1=\"{Left}\" x2=\"{Right}\" y1=\"{Number(y, "0")}\" y2=\"{Number(y, "0")}\"/>");
            svg.Append($"<text class=\"app\" x=\"{Left - 12}\" y=\"{Number(y + 4, "0")}\" text-anchor=\"end\">{apps[row]}</text>");
            for (int i = 0; i < languages.Count; i++)
            {
                if (Median(results, apps[row], languages[i].Name, metric) is not double v || v <= 0)
                    continue;
                string ratio = Ratio(results, apps[row], languages[i].Name, metric) is double r ? $" ({Times(r)})" : "";
                string tipText = $"{apps[row]} · {languages[i].Display}: {Number(v, metric.Format)} {metric.Unit}{ratio}";
                svg.Append($"<circle cx=\"{Number(X(v), "0.0")}\" cy=\"{Number(y, "0")}\" r=\"6\" fill=\"var({SeriesVars[i % SeriesVars.Length]})\" data-tip=\"{WebUtility.HtmlEncode(tipText)}\"><title>{WebUtility.HtmlEncode(tipText)}</title></circle>");
            }
        }
        return svg.Append("</svg>").ToString();
    }

    static string TickLabel(double value) =>
        value >= 1000 ? (value / 1000).ToString("0.#", Invariant) + "k"
        : value >= 1 ? value.ToString("0", Invariant)
        : value.ToString("0.###", Invariant);
}
