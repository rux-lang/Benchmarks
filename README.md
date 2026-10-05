# Benchmarks

Single-threaded computational benchmarks for **Rux, C++, Rust, and C#**, with separate **C# Native AOT** and **C# JIT** configurations. Five workloads × five configurations = 25 comparisons.

The runners use **PowerShell 7 on Windows 11** and **Bash on Ubuntu 26.04 LTS**, on native x86-64. No Python, Node.js, or separate C++ runner is required.

## Workloads

| Project | Algorithm | Exercises |
|---|---|---|
| MatrixMultiply | Dense row-major multiplication, `i-k-j` loops | Floating-point arithmetic and memory locality |
| FastFourierTransform | Iterative radix-2 FFT, bit reversal, precomputed twiddles | Arithmetic, indexing and memory |
| NBody | Direct pairwise softened gravity, symplectic Euler | Division, square roots and floating-point arithmetic |
| Mandelbrot | Escape-time iteration over a fixed viewport | Branching and floating-point loops |
| PrimeSieve | Eratosthenes sieve, one byte per candidate | Integer operations and memory bandwidth |

Algorithms, input formulas, operation order and layouts match across languages. Allocation and safety checks remain language-specific. No BLAS, FFT library, explicit SIMD, GPU code or parallel benchmark algorithms are used.

## Quick start

Install the toolchains and runner tools in [Windows setup](Docs/Windows.md) or [Ubuntu setup](Docs/Ubuntu.md). Supply a **matching Rux source checkout** for its first-party packages.

PowerShell 7:

```powershell
.\Run.ps1 doctor -RuxRoot D:\Work\Rux
.\Run.ps1 verify -Profile Smoke -RuxRoot D:\Work\Rux
.\Run.ps1 run -Profile Standard -RuxRoot D:\Work\Rux
```

Ubuntu:

```bash
bash Run.sh doctor --rux-root ../Rux --cpp clang++-23
bash Run.sh verify --profile Smoke --rux-root ../Rux --cpp clang++-23
bash Run.sh run --profile Standard --rux-root ../Rux --cpp clang++-23
```

The sibling ../Rux checkout is discovered automatically. Its package dependency closure is copied into Build/RuxPackages; the original checkout is never edited.

Quick end-to-end smoke runs:

```powershell
.\Run.ps1 run -Profile Smoke -BuildRuns 1 -RunRuns 2 -MemoryRuns 1 -Warmups 1
```

```bash
bash Run.sh run --profile Smoke --build-runs 1 --run-runs 2 --memory-runs 1 --warmups 1
```

Smoke inputs check infrastructure. Their tiny computation times are unsuitable for performance rankings.

## Measurements

| Metric | Definition |
|---|---|
| Compilation | Clean compile + link; restoration and cleanup excluded; OS file caches warm |
| Executable size | Native executable or C# JIT apphost bytes |
| Deployable size | Application files excluding symbols/linker artifacts; JIT shared runtime excluded and identified |
| Memory | Windows peak working set or Linux maximum RSS, in bytes |
| Fresh-process time | Startup, input, allocation, calculation, checksum, JSON output and teardown |
| Warmed computation time | Calculation only, after in-process warm-ups |

Defaults: **5 clean builds**, **10 fresh-process samples**, **10 warmed computation samples**, **3 memory samples**, **2 warm-ups**. Retain every sample and report median, minimum, maximum, mean and sample standard deviation.

Benchmark processes are pinned to one logical CPU before execution. Builds run sequentially with normal compiler workers. C# uses nonconcurrent workstation GC, with tiering and dynamic PGO disabled. Runtime helper threads may exist; the entire process shares the selected CPU.

## Results

A new UTC-stamped directory under Results contains:

- **Results.json**: authoritative machine/toolchain metadata, commands, hashes, validation and raw samples.
- **Summary.csv**: one row per workload/configuration for spreadsheet import.
- **Report.md**: readable comparison table.
- Input files, output checksums, diagnostic logs and raw Hyperfine exports.

Durations are seconds; sizes are bytes. Failed/interrupted rows preserve partial samples and error status. Missing measurements are null/empty, never zero.

```powershell
.\Run.ps1 report -Output Results\YourRun
```

```bash
bash Run.sh report --output Results/YourRun
```

Compare matching inputs/settings within the same machine and OS first. Power policy, thermal conditions, compiler versions, libraries, antivirus activity and virtualization affect results. These tests measure language implementations, not an isolated compiler backend.

## Documentation

- [Commands and profiles](Docs/Running.md)
- [Methodology and algorithms](Docs/Methodology.md)
- [Hardware metadata](Docs/Hardware.md)
- [Result and executable interfaces](Docs/Results.md)
- [Adding Go, Java or another language](Docs/AddingLanguages.md)
- [Validation status](Docs/Validation.md)

Repository-owned names use PascalCase; language conventions retain src/main.rs, Cargo.toml, global.json and .csproj. Generated builds, downloaded tools and results are ignored by Git.
## License

Licensed under the [MIT License](LICENSE.md).
