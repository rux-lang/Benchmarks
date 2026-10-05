# Interfaces and result files

Config/Benchmarks.json defines supported languages, parameters, bounds and tolerances. Config/Results.schema.json describes authoritative JSON. Both runners use the same field names and seconds/bytes units.

## Executable input

Read six unsigned decimal integers separated by ASCII whitespace from stdin:

```text
1 Size Work Seed Warmups Samples
```

Version is 1. Size is at least 2 and bounded per workload. Work is 1–1000000, Seed 0–1000000, Warmups 0–100, Samples 1–1000. Reject extra fields, signed/overflowed integers and unsupported versions. Runners supply LF-terminated files and close stdin. No compile-time workload sizing is used.

Each process emits exactly one JSON object:

```json
{
  "Protocol": 1,
  "Benchmark": "MatrixMultiply",
  "Size": 32,
  "Work": 1,
  "Seed": 1,
  "Warmups": 0,
  "Samples": [
    {"Seconds": 0.001, "Sum": -0.03125, "Weighted": -43.5}
  ]
}
```

Seconds above is illustrative, not a benchmark result. Each sample corresponds to one independent calculation with fresh initialized arrays. Warm-up results are consumed and checked for finite values but omitted from the returned samples. Diagnostics use stderr and failures exit nonzero.

## Results.json

Top-level fields include SchemaVersion, RunId, UTC timestamps, Platform, Architecture, Profile, Command, Status, Settings, Machine, Toolchains, SourceHashes, RuxPackageHashes and Results.

Each result has Benchmark, Language, Parameters, Status, Error, BuildKind, BuildCommand, Dependencies, size fields, runtime requirement, memory metric, validation kind, raw BuildSeconds/ProcessSeconds/KernelSeconds/PeakMemoryBytes, Checksums and Statistics. C# Dependencies records restored package identities from project.assets.json. Toolchain Details records .NET SDK/runtime information and Rust verbose version information when applicable.

Statuses: Building, Measuring, Prepared, Verified, Completed, Failed, Interrupted. Only Completed rows contain finished performance measurements. The top-level status represents the whole operation; a successful verify operation contains Verified rows.

Statistics include Count, Median, Minimum, Maximum, Mean and sample StandardDeviation (denominator n-1). StandardDeviation is null for a single sample. Empty metrics have null statistics. CSV uses invariant decimal points on both platforms.

Result writes replace a temporary JSON file after each completed measurement. Failure/interruption retains completed samples and logs. A hard kill/power failure can leave the last persisted status as Building/Measuring; treat it as incomplete. Report generation never upgrades an incomplete status.

Report regenerates summaries/statistics from the original run and preserves all raw data. It does not merge runs or invent an overall language score.

