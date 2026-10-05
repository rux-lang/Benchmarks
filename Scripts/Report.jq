"# Benchmark results\n",
"Run: \(.RunId) | Platform: \(.Platform) | Profile: \(.Profile)\n",
"Times are seconds; sizes are bytes. Full samples, statistics, validation, and machine provenance are in Results.json.\n",
"| Benchmark | Language | Status | Build median | Process median | Kernel median | Peak memory median | Executable | Deployable |",
"|---|---|---|---:|---:|---:|---:|---:|---:|",
(.Results[] | [ .Benchmark,.Language,.Status,.Statistics.Compilation.Median,.Statistics.Process.Median,
    .Statistics.Kernel.Median,.Statistics.Memory.Median,.ExecutableBytes,.DeployableBytes ] |
    "| " + (map(if .==null then "—" else tostring end) | join(" | ")) + " |"),
"\nFresh-process time includes startup, initialization, validation, output and teardown. Kernel time excludes these.",
"Memory is process peak working set on Windows and process maximum RSS on Linux; the metrics are related, not identical.",
"CSharpJit build time creates managed code. Its deployable size excludes the shared .NET runtime.",
"Failed and interrupted rows are incomplete; do not rank them against completed rows."

