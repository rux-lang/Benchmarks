# Methodology

How `build`, `run` and `all` measure the apps. The compile benchmark uses the same build procedure, and also builds on one CPU; its own measurements are in [Compile benchmark](CompileBenchmark.md#what-is-measured).

## Build

For each app and language the runner restores packages (untimed), does one warm-up build (untimed), then deletes the build output and builds again `--build-runs` times (3 by default), timing each clean build. The compile time is the median of those builds:

- for Java AOT it is `javac` plus `native-image`;
- for Java JIT it is `javac`, `jar`, `jlink` and the class-data-sharing dump.

**Compile memory** is the peak memory of each timed build. On Windows each build step runs in a job object, and the peak is the memory committed by all of its processes together at their highest point (the build tool and every compiler, linker or server it starts). On Linux it is the peak resident set of the step's largest process (from `wait4`), so the figures are not comparable across operating systems. For a build of several steps (Java), it is the step that used the most.

**Executable size** is the program file: the app's IL assembly for C# JIT (not the apphost that starts it) and the jar for Java JIT. C# JIT and Java JIT need their runtime to run, so reports show their executable size in italics and leave it out of the ranking and the geometric means.

**Deployable size** is every file needed to run the program, including the runtime.

**Library check.** After building, the runner reads the import tables (PE) or `DT_NEEDED` entries (ELF) of every deployed native file, records the operating-system libraries as `libraries` in `results.json`, and marks the case `FAIL` if it loads a library that is neither an operating-system library nor one of its own deployed files.

**Go.** Go compiles its standard library into the build cache rather than shipping it precompiled, and caches the app too. So the runner compiles the standard library once into `Apps/.gocache-std` (untimed) and starts every clean Go build from a fresh copy of it: like Rust and C#, the standard library is ready and the app itself is compiled and linked from scratch.

## Run

Every program first runs `--warmups` times unmeasured (1 by default), then `--runs` measured times (5 by default). Languages take turns (Rux, Rust, C++, Go, C# AOT, C# JIT, Java AOT, Java JIT, Rux, ...) so slow drifts in machine state affect all of them alike. Per run the runner records:

- wall-clock time from process creation to exit,
- user and kernel CPU time,
- peak memory: the peak working set on Windows (`GetProcessTimes`, `GetProcessMemoryInfo`), the maximum resident set size on Linux (`posix_spawn` + `wait4`). On Linux a process's maximum resident set includes the peak of the memory it ran in before its `exec`, and `posix_spawn` starts a child in the runner's memory, so the runner does not start the program itself: a small `/bin/sh` starts it and exits, and the runner (a child subreaper) adopts it and collects it with `wait4`. The clock starts when the runner releases the waiting program, after the shell is gone.

Reports show the median. Execution time includes process start-up, which is what a user of a console application sees; for C# JIT and Java JIT that includes starting the runtime and JIT-compiling. Java JIT is started as `java -jar` with the `java` of its own jlink runtime, so the measured process is the JVM itself.

`run` without `build` builds any program that is not built yet, untimed.

## Validation

Every run must exit with code 0 and print exactly the output stored in `app.json`; Mandelbrot's image file must also match the hash it prints. A failed run marks that cell `FAIL` and leaves it out of the statistics.

When `app.json` has no expected output yet, all languages must agree with each other; for Sha512, Sort and Base64 the result is also checked against .NET library implementations. `--update-expected` stores the agreed output.

## Environment

Child processes get a cleaned environment: variables that change how compilers optimize or runtimes behave (`DOTNET_*`, `COMPlus_*`, `MSBUILD*`, `CARGO_*`, `RUSTFLAGS`, `CL`, `LINK`, `GOFLAGS`, `GOGC`, `GOAMD64`, `CGO_*`, `JAVA_TOOL_OPTIONS`, `_JAVA_OPTIONS`, `CLASSPATH`, ...) are removed and listed in `results.json`. `--cpu K` pins the measured programs to one logical CPU.

## Ratios

"Relative to Rux" divides each value by Rux's value for the same app (the `baseline` in `bench.json`). Every metric is lower-is-better, so below 1.00× beats Rux. Summaries use the geometric mean over apps, which treats "2× faster" and "2× slower" symmetrically.
