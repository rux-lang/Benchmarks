# Running

Use PowerShell 7.2+ on Windows and Bash 4.4+ on Linux. Source paths resolve relative to the repository, independent of your current directory.

| Command | Behavior |
|---|---|
| doctor | Check tools, architecture and affinity; print compiler identities |
| prepare | Stage Rux packages, restore dependencies and build without timing |
| verify | Build, check independent references and invalid-input rejection |
| run | Prepare, verify, then measure builds, process time, computation and memory |
| report | Rebuild summaries from an existing Results.json; no compilers needed |

Downloads happen only during preparation/restoration. Internet is needed for the first C# restore. The generated NuGet cache is reused.

## Options

| PowerShell | Bash | Default |
|---|---|---|
| -Benchmark | --benchmark | All; comma-separated names |
| -Language | --language | Rux,Cpp,Rust,CSharpAot,CSharpJit |
| -Profile | --profile | Standard; also Smoke or Large |
| -BuildRuns | --build-runs | 5 |
| -RunRuns | --run-runs | 10 process and 10 computation samples |
| -MemoryRuns | --memory-runs | 3 |
| -Warmups | --warmups | 2 |
| -TimeoutSeconds | --timeout-seconds | 1800 per child invocation |
| -Cpu | --cpu | First allowed CPU; -1 / auto chooses automatically |
| -Seed | --seed | 1 |
| -Size / -Work | --size / --work | Profile values; overrides require one workload |
| -RuxRoot | --rux-root | Sibling Rux checkout |
| -Rux / -Cpp / -Rust / -Dotnet | --rux / --cpp / --rust / --dotnet | PATH discovery |
| -Hyperfine | --hyperfine | PATH; Windows also searches Build/Tools |
| -Machine | --machine | Optional metadata JSON |
| -Output | --output | New UTC-stamped Results directory |

Use canonical case for language/workload/profile names. New runs never overwrite an existing Results.json. Report updates summaries/statistics in that directory.

```powershell
.\Run.ps1 run -Benchmark MatrixMultiply -Language Rux,Cpp,Rust -Size 768 -Cpu 2
.\Run.ps1 run -Profile Large -Machine Machine.local.json -Output Results\DesktopA
```

```bash
bash Run.sh run --benchmark MatrixMultiply --language Rux,Cpp,Rust --size 768 --cpu 2
bash Run.sh run --profile Large --machine Machine.local.json --output Results/DesktopA
```

Run only one suite per checkout. Use separate copies on Windows and Linux, particularly with WSL shared mounts: executable formats and restored assets must not overwrite one another.

## Profiles

| Workload | Smoke | Standard | Large |
|---|---:|---:|---:|
| MatrixMultiply | 32 | 512 | 1024 |
| FFT | 1024 | 1048576 | 4194304 |
| NBody | 32 bodies / 2 steps | 1024 / 10 | 2048 / 20 |
| Mandelbrot | 64 square / 100 iterations | 1024 / 500 | 2048 / 1000 |
| PrimeSieve | 10000 | 50000000 | 200000000 |

Matrix/Mandelbrot size is side length; FFT requires a power of two; sieve includes its upper bound. Only NBody/Mandelbrot use Work; other workloads require a positive placeholder, normally 1. Seed initializes matrix, FFT and NBody data; Mandelbrot/sieve do not use it.

Timeout applies to an entire child invocation, including all warm-ups and samples within a warmed-computation process. Allocation failures/timeouts fail that configuration.

Close competing applications, use AC power and a known power policy, and allow temperature to settle. The runner does not change system-wide power, security, cache or scheduler settings.

