# Compile benchmark

The apps are one file each, so their compile time is mostly fixed cost: starting the build tool, the standard library, linking. Rux also compiles its standard library from source on every build (about 40,000 lines), while the other languages link a precompiled one. The compile benchmark measures what happens on a real-sized project instead. It is a separate command with its own report because it takes longer and measures something else.

## Running it

```bash
dotnet run --project Runner -c Release -- compile                                    # Baseline and Large, all languages
dotnet run --project Runner -c Release -- compile --build-runs 1                     # one build of each kind
dotnet run --project Runner -c Release -- compile --lang Rux,Cpp,Go                  # a subset
dotnet run --project Runner -c Release -- compile --project Baseline                 # only the fixed cost
```

Results go to `Results/<date>-compile-<os>/`, with the same files as the app benchmark. A clean build of `Large` takes from a few seconds (Go) to about 20 seconds (Rux), and every project and language is built several times (warm-up, `--build-runs` clean builds on all CPUs, as many on one CPU, as many rebuilds, and a rebuild after restoring the edit); use `--build-runs 1` and `--lang` to keep runs short. Each build step may run for up to an hour (`timeoutSeconds` in `Projects/Large/project.json`, or `--timeout`).

## The projects

`Projects/` holds two generated projects in every language:

| Project    | Contents                                                                                          | Per language                   |
| ---------- | ------------------------------------------------------------------------------------------------- | ------------------------------ |
| `Baseline` | Main, the shared helpers and one module, with the same build setup and dependencies               | 4–6 files, ~130 lines          |
| `Large`    | 1,000 modules in 100 files of 10 (each module in its own namespace), in 10 groups of 10 files     | ~100 files, ~100,000 lines     |

`Baseline` is committed. `Large` (about 4 MB of source per language) is gitignored: `compile` and `generate` write it when it is missing or out of date, which takes a few seconds. `compile` always builds `Baseline` too, since `Large` is measured against it.

Each project folder has a `project.json` with its description, module and file counts, the expected output and the source file and line counts per language.

### What the code does

A module is the unit of work, and module _N_ is the same code in both projects. Each module is one of ten kinds, taking turns, so every group has one of each:

1. a struct with methods (3D vectors),
2. an enum state machine,
3. an interface with three implementations called through dynamic dispatch,
4. hash-map counting,
5. insertion sort and binary search,
6. a ring buffer,
7. a table-driven CRC,
8. decimal text formatting and parsing,
9. small matrix products,
10. union-find.

Each module also has two or three generated arithmetic functions, and public `Mix` and `Run` functions. Each module calls `Mix` of up to three of the twenty modules before it. Every group has a runner that runs its modules in order, and Main calls the runners and prints one FNV-1a hash, which must be the same in every language. All arithmetic is unsigned 64-bit and wraps.

### Layout per language

Every language uses its usual project layout:

| Language | Layout                                                                                         | Build                                                                                 |
| -------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| Rux      | one package; `Src/GroupN/PartNNNN.rux`, a `pub module GroupN::NameNNN` per module              | `rux build --release`                                                                 |
| Rust     | one crate; `src/groupN/mod.rs`, and `partNNNN.rs` with an inline `mod` per module              | `cargo build --release`                                                               |
| C++      | `src/groupN/partNNNN.h` and `.cpp`, a namespace `groupN::namennn` per module, `CMakeLists.txt` | CMake configures Ninja once (untimed); Ninja builds in parallel                       |
| Go       | one module, a package per group; a module's names start with its own name                      | `go build`                                                                            |
| C#       | one project, a namespace per group, a static class per module                                  | `dotnet publish` (AOT and JIT, as for the apps)                                       |
| Java     | `src/groupN/PartNNNN.java`, a package per group, a nested class per module                     | `javac` of every file (from an argument file), then `native-image` or `jar` + `jlink` |

## Generating the projects

The sources are generated by `Runner/Generator/` from one seeded specification:

| File                                                                                                     | Role                                                        |
| -------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------- |
| `Spec.cs`                                                                                                | The projects, their modules and the seeded specification    |
| `Kernel.cs`                                                                                              | The generated arithmetic functions                          |
| `ProjectGenerator.cs`                                                                                    | Writes the projects and `project.json`, detects stale files |
| `Emitter.cs`                                                                                             | Shared emitter base                                         |
| `RuxEmitter.cs`, `RustEmitter.cs`, `CppEmitter.cs`, `GoEmitter.cs`, `CSharpEmitter.cs`, `JavaEmitter.cs` | One emitter per language                                    |

```bash
dotnet run --project Runner -c Release -- generate                     # Baseline and Large
dotnet run --project Runner -c Release -- generate --project Large     # only Large
dotnet run --project Runner -c Release -- generate --project Baseline  # only Baseline
```

`generate` rewrites only the files that change, removes files that are no longer generated, and prints the file and line counts per language. It always writes all six languages; `--lang` does not narrow it.

`compile` and `doctor` refuse to run when the committed `Baseline` differs from what the generator writes: after changing the generator, run `generate` and commit `Projects/Baseline`. When a change alters the program's output, clear `expected` in `Projects/*/project.json` and run `compile --update-expected`. `generate` clears it by itself when a project's number of modules changes.

To remove the generated `Large` sources, delete `Projects/Large/`; `clean` removes only build output.

## What is measured

Per project and language, with the same clean-build procedure as the apps (untimed preparation and warm-up build, then `--build-runs` timed clean builds; see [Methodology](Methodology.md#build)):

- **Clean build** of `Large` and of `Baseline`.
- **Large − Baseline**: the time spent on the project's own code, without the fixed cost. For Rux, most of the fixed cost is the standard library.
- **Compile speed**: the extra lines of `Large` over `Baseline` per second of Large − Baseline.
- **Clean build on one CPU**: the same clean builds, `--build-runs` times, with the runner and every process the build starts pinned to one logical CPU (`--cpu K`, by default the first CPU the runner may use). CPU affinity is inherited by child processes on Windows and Linux, so the build gets one CPU's worth of time whatever it does. Cargo and Ninja are also told to run one job (`cargo build --jobs 1`, `cmake --build --parallel 1`) rather than relying on them to read the affinity; the other tools get no extra option. The report shows the clean build, Large − Baseline and compile speed on one CPU as well, and its peak memory.
- **Parallel speed-up**: the clean build of `Large` on one CPU divided by the clean build on all CPUs, which is how much faster the build gets from the other CPUs.
- **Peak memory** of every timed build, measured as for the apps' compile memory. On Windows Ninja's parallel compilers add up, since all processes of a build step are counted together.
- **Rebuild after a one-line edit**: the line marked `bench:edit` in the last module (a leaf: only Main calls it) gets a new number, and the project builds again without cleaning, `--build-runs` times. The file is then restored and rebuilt. This shows what each build tool does incrementally: Ninja recompiles one translation unit and links, Go recompiles one package and links, release Cargo builds and C# recompile the whole crate or project, javac recompiles every file, and Java JIT keeps its `jlink` runtime.
- Executable and deployable size, as for the apps.

Each program also runs once, and its output must match `expected` in `project.json`; otherwise the case is marked `FAIL`.
