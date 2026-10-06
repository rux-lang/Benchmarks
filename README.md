# Benchmarks

Ten small console applications, each written the same way in **Rux, Rust, C++ and C#**, plus one
runner that builds and runs them all and compares:

- **execution time** and **CPU time**
- **peak memory**
- **compile time** (clean release build)
- **executable size** and **deployable size**

C# is measured twice: **C# AOT** (NativeAOT, one native executable) and **C# JIT**
(framework-dependent, runs on the installed .NET runtime). That gives 10 apps × 5 configurations.
Results are reported relative to Rux.

## Quick start

Windows 11 or Linux, x86-64. Install the toolchains (any recent version; the versions used are
recorded with every result):

| Tool | Used for |
|---|---|
| `rux` on `PATH` | Rux apps |
| Rust (`cargo`, `rustc`) | Rust apps |
| `clang++` | C++ apps (on Windows it uses the Visual Studio C++ libraries and linker) |
| .NET SDK 10 | C# apps and the runner |

On Windows, NativeAOT also needs Visual Studio with the "Desktop development with C++" workload.

```bash
rux --manifest Apps/Sha512/Rux/Rux.toml install   # once: fills the local Rux package cache
dotnet run --project Runner -c Release -- doctor  # checks tools and setup
dotnet run --project Runner -c Release -- all     # builds, runs, writes the report
```

`all` with the standard profile takes about half an hour, most of it in the slowest
language. Use `--profile small --runs 1 --warmups 0 --build-runs 1` for a quick
correctness check (under two minutes), or `--app` and `--lang` to measure a subset:

```bash
dotnet run --project Runner -c Release -- all --app Sha512,Sort --lang Rux,Cpp
```

Each run writes a folder under `Results/<date>-<os>/`:

| File | Contents |
|---|---|
| `report.html` | Charts and tables; open it in a browser |
| `report.md` | The same tables in Markdown |
| `summary.csv` | One row per app, language and metric: median, min, mean, stddev, ratio to Rux |
| `samples.csv` | Every individual measurement |
| `results.json` | Everything above plus machine, toolchain versions and settings |

`dotnet run --project Runner -c Release -- report [folder]` rewrites the report files from a
saved `results.json`.

## Layout

```
bench.json                    languages, apps, default counts, C++ flags, tool names
Apps/<App>/app.json           description, arguments per profile, expected output
Apps/<App>/Rux/               Rux.toml, Src/Main.rux, Src/Arguments.rux
Apps/<App>/Rust/              Cargo.toml, Cargo.lock, src/main.rs
Apps/<App>/Cpp/               main.cpp
Apps/<App>/CSharp/            <App>.csproj, Program.cs   (shared settings: Apps/Directory.Build.props)
Runner/                       the C# measurement tool
Results/                      output, not committed
```

Every app is an ordinary program: it takes its sizes as command-line arguments (with defaults
equal to the standard profile), prints a deterministic result and exits. There is no shared
harness and no timing code inside the apps. Each folder builds on its own:

```bash
rux build --release                                  # in Apps/<App>/Rux
cargo build --release                                # in Apps/<App>/Rust
clang++ -std=c++23 -O3 -DNDEBUG main.cpp -o build/<App>   # in Apps/<App>/Cpp (full flags in bench.json)
dotnet publish -c Release -r win-x64 -p:PublishAot=true   # in Apps/<App>/CSharp
```

## The apps

| App | What it does | Stresses | Standard size |
|---|---|---|---|
| Sha512 | SHA-512 of a pseudo-random buffer, re-hashed with the digest fed back in | 64-bit integer and bit operations | 16 MiB × 16 rounds |
| Mandelbrot | Renders the Mandelbrot set and writes it as a PPM image | Floating point, file output | 2000×2000, 500 iterations |
| WordCount | Generates text from a random vocabulary, counts words in a hash map, prints the top 10 | Strings, hashing, hash maps | 10M words, 100k vocabulary |
| BinaryTrees | Builds and frees many complete binary trees | Allocation | depth 18 |
| Sort | Quicksort (median of three, insertion sort below 16) of random 32-bit integers | Branches, memory access | 10M integers |
| NBody | Five-body planetary simulation | Floating point, square root | 5M steps |
| MatrixMultiply | Dense double-precision matrix product, i-k-j loop order | Loops, cache, vectorization | 1024×1024 |
| PrimeSieve | Sieve of Eratosthenes over a byte array, prints count and sum | Memory bandwidth | primes up to 100M |
| Fannkuch | Pancake flips over every permutation (fannkuch-redux) | Small arrays, branches | n = 10 |
| Base64 | Hand-written Base64 encode and decode round trip | Byte manipulation, table lookups | 32 MiB × 4 rounds |

### Rules that keep the comparison fair

- The algorithm is written by hand, identically, in every language: same data generation, same
  operation order, same data layout. Only standard-library I/O, collections and memory
  allocation are used; no third-party packages, SIMD intrinsics or threads.
- Pseudo-random data comes from SplitMix64 (`s += 0x9E3779B97F4A7C15`, then the usual two
  multiply-xorshift rounds), seeded with 1 (Sha512), 2 (WordCount), 3 (Sort) and 4 (Base64).
  Hashes of results use FNV-1a 64 (offset `0xCBF29CE484222325`, prime `0x100000001B3`).
- Floating-point results are printed as raw IEEE-754 bit patterns, so all languages must agree
  bit for bit. C++ is compiled with `-ffp-contract=off` so no fused multiply-adds change results.
- Outputs never depend on hash-map iteration order: WordCount ranks by count, then by word.
- BinaryTrees allocates the way each language normally does: `new`/`delete` in C++, `Box` in
  Rust, the garbage collector in C#, and `Allocator::Pool` (the small-object allocator) in Rux.
- All builds target baseline x86-64 (`-march=x86-64`, Rust's default target CPU,
  `IlcInstructionSet=x86-64`, Rux's default).

## How measuring works

**Build.** For each app and language the runner restores packages (untimed), does one warm-up
build (untimed), then deletes the build output and builds again 3 times, timing each clean
build. The compile time is the median of those builds. Executable size is the program file;
deployable size is every file needed to run it (for C# JIT, excluding the shared .NET runtime).

**Run.** Every program first runs once unmeasured (warm-up), then 5 measured times. Languages
take turns (Rux, Rust, C++, C# AOT, C# JIT, Rux, ...) so slow drifts in machine state affect
all of them alike. Per run the runner records:

- wall-clock time from process creation to exit,
- user and kernel CPU time,
- peak memory: the peak working set on Windows (`GetProcessTimes`, `GetProcessMemoryInfo`),
  the maximum resident set size on Linux (`posix_spawn` + `wait4`).

Reports show the median. Execution time includes process start-up, which is what a user of a
console application sees; for C# JIT that includes starting the runtime and JIT-compiling.

**Validation.** Every run must exit with code 0 and print exactly the output stored in
`app.json`; Mandelbrot's image file must also match the hash it prints. A failed run marks that
cell `FAIL` and leaves it out of the statistics. When `app.json` has no expected output yet, all
languages must agree with each other; for Sha512, Sort and Base64 the result is also checked
against .NET library implementations. `--update-expected` stores the agreed output.

**Environment.** Child processes get a cleaned environment: variables that change how compilers
optimize or runtimes behave (`DOTNET_*`, `COMPlus_*`, `MSBUILD*`, `CARGO_*`, `RUSTFLAGS`, `CL`,
`LINK`, ...) are removed and listed in `results.json`. `--cpu K` pins the measured programs to
one logical CPU.

**Ratios.** "Relative to Rux" divides each value by Rux's value for the same app. Every metric
is lower-is-better, so below 1.00× beats Rux. Summaries use the geometric mean over apps, which
treats "2× faster" and "2× slower" symmetrically.

For stable numbers: plug in a laptop, close other programs, and consider excluding the
repository from antivirus real-time scanning (it scans every freshly built executable).

## Rux notes

- **Command-line arguments.** Rux 0.4.0 calls `Main()` without arguments and no package exposes
  them, so every Rux app carries the same small `Src/Arguments.rux`. It reads the command line
  from `GetCommandLineA` on Windows and `/proc/self/cmdline` on Linux. `doctor` checks that all
  copies are identical.
- **Packages.** Rux apps depend on `{ Namespace = "Rux", Version = "*" }` packages, which
  `rux build` takes from the local package cache (`%LOCALAPPDATA%\Rux\Packages` or
  `~/.rux/packages`) without network access. Run `rux install` once to fill it.
- **Small allocations.** `SystemAllocator` requests whole pages from the operating system for
  every allocation, so BinaryTrees uses `Allocator::Pool`, Rux's documented allocator for many
  small objects.

## Adding an app

1. Create `Apps/<Name>/` with the four language folders (copy an existing app, keep
   `Arguments.rux` unchanged) and an `app.json` with `small` and `standard` arguments and empty
   `expected` values.
2. Add the name to `apps` in `bench.json`.
3. Run `all --app <Name> --profile small --runs 1 --warmups 0 --build-runs 1 --update-expected`,
   then the same with `--profile standard`, and commit the stored outputs.

Adding a language means one new `Toolchain` class in `Runner/Toolchains.cs` (build command,
output path, clean folders) and a folder per app.
