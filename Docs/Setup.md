# Setup

## Requirements

Windows 11 or Linux, x86-64. Install the toolchains below (any recent version; the versions used are recorded with every result):

| Tool                        | Used for                                                                                 |
| --------------------------- | ---------------------------------------------------------------------------------------- |
| `rux` on `PATH`             | Rux apps                                                                                 |
| Rust (`cargo`, `rustc`)     | Rust apps                                                                                |
| `clang++`                   | C++ apps (on Windows it uses the Visual Studio C++ libraries and linker)                 |
| CMake and Ninja             | C++ projects of the compile benchmark only                                               |
| Go (`go`)                   | Go apps                                                                                  |
| .NET SDK 10                 | C# apps and the runner                                                                   |
| JDK 25 or later (`java`)    | Java JIT apps; `javac`, `jar` and `jlink` are taken from the same JDK                    |
| GraalVM with `native-image` | Java AOT apps (`native-image` on `PATH`, in `$GRAALVM_HOME/bin`, or set in `bench.json`) |

On Windows, NativeAOT and Native Image also need Visual Studio with the "Desktop development with C++" workload.

## First run

Rux apps take their packages from the local package cache (`%LOCALAPPDATA%\Rux\Packages` or `~/.rux/packages`) without network access, so fill it once:

```bash
rux --manifest Apps/Sha512/Rux/Rux.toml install
```

Then check the setup:

```bash
dotnet run --project Runner -c Release -- doctor
```

`doctor` prints the OS, CPU, memory and git commit, the version of every toolchain in `languages`, and checks that:

- every tool is found (`NOT FOUND` otherwise),
- the Rux packages are in the cache,
- `Arguments.rux` is identical in every Rux app (see [Rux notes](Rux.md)),
- the committed generated projects match what the generator writes now (see [Compile benchmark](CompileBenchmark.md)).

It also lists the environment variables it removes from child processes (see [Methodology](Methodology.md#environment)). It exits with code 1 if any check fails.

## `bench.json`

| Field              | Meaning                                                                                                            |
| ------------------ | ------------------------------------------------------------------------------------------------------------------ |
| `baseline`         | The configuration every ratio is relative to (`Rux`)                                                               |
| `languages`        | The configurations to build and run. Leave out the ones whose tools this machine lacks; `doctor` checks only these |
| `apps`             | The apps to measure                                                                                                |
| `projects`         | The generated projects for `compile`, `generate` and `clean`                                                       |
| `defaults`         | `profile`, `buildRuns`, `runs`, `warmups` and `timeoutSeconds` used when no option overrides them                  |
| `cppFlags`         | `clang++` flags for the C++ apps and projects                                                                      |
| `javacFlags`       | `javac` flags                                                                                                      |
| `nativeImageFlags` | `native-image` flags                                                                                               |
| `jlinkFlags`       | `jlink` flags for the Java JIT runtime                                                                             |
| `tools`            | The command (or full path) of each tool, for tools that are not on `PATH` under their usual name                   |

The command-line options (`--app`, `--lang`, `--profile`, `--runs`, ...) override these for one run; see the [command reference](../README.md#commands).

## Stable numbers

- Plug in a laptop and close other programs.
- Consider excluding the repository from antivirus real-time scanning: it scans every freshly built executable, which inflates build times.
- Use `--cpu K` to pin the measured programs to one logical CPU.
