# Apps

## The apps

| App            | What it does                                                                           | Stresses                          | Standard size              |
| -------------- | -------------------------------------------------------------------------------------- | --------------------------------- | -------------------------- |
| Sha512         | SHA-512 of a pseudo-random buffer, re-hashed with the digest fed back in               | 64-bit integer and bit operations | 16 MiB × 16 rounds         |
| Mandelbrot     | Renders the Mandelbrot set and writes it as a PPM image                                | Floating point, file output       | 2000×2000, 500 iterations  |
| WordCount      | Generates text from a random vocabulary, counts words in a hash map, prints the top 10 | Strings, hashing, hash maps       | 10M words, 100k vocabulary |
| BinaryTrees    | Builds and frees many complete binary trees                                            | Allocation                        | depth 18                   |
| Sort           | Quicksort (median of three, insertion sort below 16) of random 32-bit integers         | Branches, memory access           | 10M integers               |
| NBody          | Five-body planetary simulation                                                         | Floating point, square root       | 5M steps                   |
| MatrixMultiply | Dense double-precision matrix product, i-k-j loop order                                | Loops, cache, vectorization       | 1024×1024                  |
| PrimeSieve     | Sieve of Eratosthenes over a byte array, prints count and sum                          | Memory bandwidth                  | primes up to 100M          |
| Fannkuch       | Pancake flips over every permutation (fannkuch-redux)                                  | Small arrays, branches            | n = 10                     |
| Base64         | Hand-written Base64 encode and decode round trip                                       | Byte manipulation, table lookups  | 32 MiB × 4 rounds          |

Each app has two profiles in its `app.json`: `standard` (the sizes above) and `small` (for quick correctness checks). Pick one with `--profile`.

## Layout

```
Apps/<App>/app.json      description, arguments per profile, expected output
Apps/<App>/Rux/          Rux.toml, Src/Main.rux, Src/Arguments.rux
Apps/<App>/Rust/         Cargo.toml, Cargo.lock, src/main.rs
Apps/<App>/Cpp/          main.cpp
Apps/<App>/Go/           go.mod, main.go
Apps/<App>/CSharp/       <App>.csproj, Program.cs   (shared settings: Apps/Directory.Build.props and .targets)
Apps/<App>/Java/         Main.java
```

Every app is an ordinary program: it takes its sizes as command-line arguments (with defaults equal to the standard profile), prints a deterministic result and exits. There is no shared harness and no timing code inside the apps.

## Building an app by hand

Each folder builds on its own. The runner adds a few more flags; the full set is in `bench.json` and `Runner/Toolchains.cs`.

```bash
rux build --release                                                       # in Apps/<App>/Rux
cargo build --release                                                     # in Apps/<App>/Rust
clang++ -std=c++23 -O3 -DNDEBUG main.cpp -o build/<App>                   # in Apps/<App>/Cpp
go build -trimpath -ldflags="-s -w" -o build/<App>                        # in Apps/<App>/Go
dotnet publish -c Release -r win-x64 -p:PublishAot=true                   # in Apps/<App>/CSharp
javac -d build/classes Main.java && java -cp build/classes Main           # in Apps/<App>/Java
native-image -march=compatibility -cp build/classes -o build/<App> Main   # in Apps/<App>/Java
```

On Linux, use `-r linux-x64` for `dotnet publish`.

## Rules that keep the comparison fair

- The algorithm is written by hand, identically, in every language: same data generation, same operation order, same data layout. Only standard-library I/O, collections and memory allocation are used; no third-party packages, SIMD intrinsics or threads.
- Pseudo-random data comes from SplitMix64 (`s += 0x9E3779B97F4A7C15`, then the usual two multiply-xorshift rounds), seeded with 1 (Sha512), 2 (WordCount), 3 (Sort) and 4 (Base64). Hashes of results use FNV-1a 64 (offset `0xCBF29CE484222325`, prime `0x100000001B3`).
- Floating-point results are printed as raw IEEE-754 bit patterns, so all languages must agree bit for bit. C++ is compiled with `-ffp-contract=off` so no fused multiply-adds change results.
- Outputs never depend on hash-map iteration order: WordCount ranks by count, then by word.
- BinaryTrees allocates the way each language normally does: `new`/`delete` in C++, `Box` in Rust, the garbage collector in Go, C# and Java, and `Allocator::Pool` (the small-object allocator) in Rux.
- Every configuration is self-contained: its deployable files are everything it needs on a machine with only the operating system installed, so deployable sizes compare like for like. Native executables carry their own C and C++ runtime and load only operating-system libraries, as Rux does. On Windows that excludes the Universal CRT (`api-ms-win-crt-*`, `ucrtbase`), `vcruntime`, `msvcp` and `msvcrt`; on Linux only glibc and `libgcc_s` may stay dynamic. Rust uses `+crt-static` (`.cargo/config.toml`), C++ `-fms-runtime-lib=static` on Windows and `-static-libstdc++ -static-libgcc` on Linux, C# AOT links the static UCRT (`Apps/Directory.Build.targets`), and Native Image links the static CRT on Windows and uses `--static-nolibc` on Linux. Go and Rux need nothing.
- C# JIT and Java JIT ship the smallest runtime their own tools produce. C# JIT is published with `SelfContained` and `PublishTrimmed`, without the runtime's optional debugger, dump, alternative-GC and QUIC files, and with Microsoft's app-local Universal CRT from the Windows SDK (the .NET runtime DLLs load the CRT as DLLs). Java JIT ships a `jlink` image of `java.base` (all the apps need) with `--strip-debug --no-header-files --no-man-pages`, plus the default class-data-sharing archive (`java -Xshare:dump`) that the JDK uses for fast start-up; the JDK already ships its C runtime DLLs.
- All builds target baseline x86-64 (`-march=x86-64`, Rust's default target CPU, `GOAMD64=v1`, `IlcInstructionSet=x86-64`, `-march=compatibility` for Native Image, Rux's default). Native Image would otherwise default to x86-64-v3.
- Java has no unsigned integers, so the Java apps use `long` with `>>>`, `Long.remainderUnsigned` and `Long.compareUnsigned` where the others use unsigned 64-bit math.
- Runtimes run with their defaults: no GC or JIT tuning flags for .NET, Go or the JVM.
