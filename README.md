# Benchmarks

Ten small console applications, each written the same way in **Rux, C++, Go, Rust, C# and Java**, plus one runner that builds and runs them all and compares:

- **execution time** and **CPU time**
- **peak memory**
- **compile time** (clean release build) and **compile memory**
- **executable size** and **deployable size**

A second, separate benchmark measures **compile time and compiler memory on a large project**: a generated program of about 100 files and 100,000 lines per language, built clean on all CPUs and on one CPU, and rebuilt after a one-line edit.

Results are reported relative to Rux. Every metric is lower-is-better, so below 1.00× beats Rux.

## Configurations

C# and Java are measured twice each, which gives 10 apps × 8 configurations. The name in the first column is what `--lang` and `bench.json` use.

| Name        | Language | What is built                                                                                |
| ----------- | -------- | -------------------------------------------------------------------------------------------- |
| `Rux`       | Rux      | One native executable (`rux build --release`)                                                |
| `Rust`      | Rust     | One native executable (`cargo build --release`, static CRT)                                  |
| `Cpp`       | C++      | One native executable (`clang++ -O3`, static C++ runtime)                                    |
| `Go`        | Go       | One native executable (`go build`)                                                           |
| `CSharpAot` | C#       | NativeAOT: one native executable                                                             |
| `CSharpJit` | C#       | Self-contained and trimmed: the app plus the part of the .NET runtime it uses, which JITs it |
| `JavaAot`   | Java     | GraalVM Native Image: one native executable                                                  |
| `JavaJit`   | Java     | A jar plus a minimal `jlink` runtime whose HotSpot JVM JIT-compiles it                       |

## Quick start

Windows 11 or Linux, x86-64. Install the toolchains listed in [Docs/Setup.md](Docs/Setup.md) (Rux, Rust, Clang, CMake and Ninja, Go, .NET SDK 10, JDK 25+, GraalVM), then:

```bash
rux --manifest Apps/Sha512/Rux/Rux.toml install   # once: fills the local Rux package cache
dotnet run --project Runner -c Release -- doctor  # checks tools and setup
dotnet run --project Runner -c Release -- all     # builds, runs, writes the report
```

`all` with the standard profile takes about half an hour. A quick correctness check of everything takes under two minutes:

```bash
dotnet run --project Runner -c Release -- all --profile small --runs 1 --warmups 0 --build-runs 1
```

Open `Results/<date>-<os>/report.html` in a browser to see the results.

## Commands

Every command is run from the repository root as:

```bash
dotnet run --project Runner -c Release -- <command> [options]
```

| Command        | What it does                                                                                                       |
| -------------- | ------------------------------------------------------------------------------------------------------------------ |
| `doctor`       | Shows the machine and toolchain versions, checks the Rux package cache, `Arguments.rux` and the generated projects |
| `all`          | `build` + `run`, then writes the report (the usual choice)                                                         |
| `build`        | Measures compile time, compile memory and program size of the apps, and writes a report                            |
| `run`          | Measures execution time, CPU time and peak memory of the apps (building them first if needed), and writes a report |
| `compile`      | The compile benchmark: clean builds (all CPUs and one CPU) and one-line-edit rebuilds of the generated projects, then its report |
| `generate`     | Writes the generated projects under `Projects/` (`Baseline` and the 100,000-line `Large`)                          |
| `report [dir]` | Rewrites the report files of a results folder from its `results.json` (default: the latest)                        |
| `clean`        | Deletes the build output of the apps and projects                                                                  |

| Option              | Applies to                                | Meaning                                                                        | Default (`bench.json`) |
| ------------------- | ----------------------------------------- | ------------------------------------------------------------------------------ | ---------------------- |
| `--app A,B`         | `all`, `build`, `run`, `clean`            | Only these apps                                                                | all 10                 |
| `--project A,B`     | `compile`, `generate`, `clean`            | Only these projects: `Baseline`, `Large` (`compile` always adds `Baseline`)    | both                   |
| `--lang X,Y`        | `all`, `build`, `run`, `compile`, `clean` | Only these configurations (names in the table above)                           | all 8                  |
| `--profile NAME`    | `all`, `run`                              | Problem sizes: `small` or `standard`                                           | `standard`             |
| `--runs N`          | `all`, `run`                              | Measured runs per program                                                      | 5                      |
| `--warmups N`       | `all`, `run`                              | Unmeasured runs before those                                                   | 1                      |
| `--build-runs N`    | `all`, `build`, `compile`                 | Timed clean builds per program; for `compile` also builds on one CPU and rebuilds | 3                   |
| `--cpu K`           | `all`, `run`, `compile`                   | Pin the measured programs (`compile`: the builds on one CPU) to logical CPU `K` | not pinned (`compile`: the first allowed CPU) |
| `--timeout S`       | `all`, `build`, `run`, `compile`          | Seconds before a build or run is stopped                                       | 600 (1 h for `Large`)  |
| `--update-expected` | `all`, `run`, `compile`                   | Store the agreed output in `app.json` / `project.json` when none is stored yet | off                    |

### App benchmark

```bash
dotnet run --project Runner -c Release -- all                                        # everything, standard profile
dotnet run --project Runner -c Release -- all --profile small --runs 1 --warmups 0 --build-runs 1   # quick check
dotnet run --project Runner -c Release -- all --app Sha512,Sort --lang Rux,Cpp        # a subset
dotnet run --project Runner -c Release -- all --cpu 2                                 # pin the measured programs to CPU 2
dotnet run --project Runner -c Release -- build --lang Rux,Rust                       # compile time and size only
dotnet run --project Runner -c Release -- run --app NBody                             # execution only
```

### Compile benchmark

```bash
dotnet run --project Runner -c Release -- generate                                   # write Projects/Large (and check Baseline)
dotnet run --project Runner -c Release -- generate --project Large                   # only the large project
dotnet run --project Runner -c Release -- compile                                    # Baseline and Large, all languages
dotnet run --project Runner -c Release -- compile --build-runs 1                     # one build of each kind
dotnet run --project Runner -c Release -- compile --lang Rux,Cpp,Go --build-runs 1   # a quick subset
dotnet run --project Runner -c Release -- compile --project Baseline                 # only the fixed cost (seconds)
dotnet run --project Runner -c Release -- compile --cpu 2                            # run the builds on one CPU on CPU 2
```

`Projects/Large/` is not committed (about 4 MB of source per language). `compile` writes it when it is missing or out of date, so running `generate` first is optional. See [Docs/CompileBenchmark.md](Docs/CompileBenchmark.md).

### Reports and cleanup

```bash
dotnet run --project Runner -c Release -- report                                     # rewrite the latest report
dotnet run --project Runner -c Release -- report Results/20261012-180214-windows     # rewrite a specific one
dotnet run --project Runner -c Release -- clean                                      # all build output
dotnet run --project Runner -c Release -- clean --app Sort --lang Go                 # one app, one language
dotnet run --project Runner -c Release -- clean --project Large                      # build output of Large
```

`clean` removes build output only. To delete the generated sources of `Large` as well, delete `Projects/Large/`.

## Results

Each run writes a folder `Results/<date>-<os>/` (`Results/<date>-compile-<os>/` for `compile`):

| File           | Contents                                                                      |
| -------------- | ----------------------------------------------------------------------------- |
| `report.html`  | Charts and tables; open it in a browser                                       |
| `report.md`    | The same tables in Markdown                                                   |
| `summary.csv`  | One row per app, language and metric: median, min, mean, stddev, ratio to Rux |
| `samples.csv`  | Every individual measurement                                                  |
| `results.json` | Everything above plus machine, toolchain versions and settings                |

## Repository layout

```
bench.json             languages, apps, projects, default counts, compiler flags, tool names
.cargo/config.toml     shared Rust settings (static CRT) for every Rust project in the repository
Apps/<App>/            one app: app.json plus a Rux, Rust, Cpp, Go, CSharp and Java folder
Projects/<Project>/    the compile benchmark's generated projects, one folder per language
Runner/                the C# measurement tool; Runner/Generator/ writes Projects/
Docs/                  documentation
Results/               output, not committed
```

## Documentation

| Document                                      | Contents                                                                 |
| --------------------------------------------- | ------------------------------------------------------------------------ |
| [Setup](Docs/Setup.md)                        | Toolchains, platform requirements, `bench.json`, tips for stable numbers |
| [Apps](Docs/Apps.md)                          | The ten apps, the fairness rules, building an app by hand                |
| [Methodology](Docs/Methodology.md)            | How builds and runs are measured, validated and summarized               |
| [Compile benchmark](Docs/CompileBenchmark.md) | The generated projects, the generator and what `compile` measures        |
| [Rux notes](Docs/Rux.md)                      | Rux-specific workarounds                                                 |
| [Extending](Docs/Extending.md)                | Adding an app or a language, changing the generator                      |

## License

Licensed under the [MIT License](LICENSE.md).
