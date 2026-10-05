# Methodology

## What is compared

The five algorithms use matching operation order, input generation and contiguous storage. Arithmetic data are IEEE binary64; sieve storage is uint8; Mandelbrot counts are int32; integer checksums are int64 and stay below 2^53 before JSON conversion. Index types follow language conventions (C++/C# int, Rust usize, Rux int). This difference and each language's bounds checks are intentional, documented implementation characteristics.

Compilation includes parsing standard libraries, optimization, code generation and linking, using normal toolchain drivers. Rux compiles its source packages; Rust uses rustc directly on a dependency-free crate; C++ uses clang++; C# uses dotnet publish. Cargo.toml supports ordinary Rust development, but Cargo startup is not included in reported Rust compilation.

C++ uses -O3, C++26 mode, -march=x86-64, -mtune=generic, -fno-fast-math and -ffp-contract=off. Rust uses edition 2024, opt-level=3, target-cpu=x86-64, no debug info and stripped symbols. Neither enables LTO or PGO. Rux uses its Release profile. Native AOT uses speed optimization and its x86-64 instruction-set profile.

C# JIT disables tiered compilation, dynamic PGO, concurrent GC, server GC and optional hardware intrinsic paths. These settings provide a controlled baseline; they are not a claim about default .NET performance. The prebuilt standard libraries and their platform implementations can still differ. No benchmark explicitly requests SIMD; compiler auto-vectorization is allowed within its target.

## Timing boundaries

1. Stage sources/packages and restore dependencies outside all timers.
2. Build once without timing and verify independent reference cases.
3. Before each timed build, remove generated products and restore C# assets again. Measure only the build driver and its children. Persistent compiler servers and incremental products are disabled. No privileged OS cache flushing occurs.
4. After process warm-ups, use Hyperfine --shell=none for one fresh-process sample at a time. Both stdin and output go to files. This lets the runner validate every output and preserve its corresponding raw timing export.
5. A separate process performs in-process warm-ups and measured calculations. Each iteration allocates and initializes fresh arrays before the internal monotonic timer, then calculates, stops the timer, consumes all results into checksums, and releases storage. This warms JIT code; it does not reuse previous numerical results.
6. Separate fresh processes measure peak memory without Hyperfine. The OS high-water counter includes allocation, runtime, computation, checksum and output.

Fresh-process time intentionally includes initialization and JSON formatting. Kernel time excludes those costs, including FFT twiddle generation. Clock reads and function-call boundaries have finite overhead, especially in Smoke inputs. Do not subtract or clamp overhead into artificially precise timings.

Windows starts processes suspended, sets affinity and attaches a job object before resuming. Linux pins the launching process with taskset so affinity is inherited. Compiler builds are not pinned. Runs are sequential in the selected benchmark/language order, which is retained in Results.json. Repeat complete runs in a different language order to investigate drift; no automatic outlier removal is performed.

## Exact algorithms

All indices start at zero. Seed defaults to 1.

### MatrixMultiply

A[i,k] = ((i + 3k + seed) mod 17 - 8) / 8. B[k,j] = ((5k + j + seed) mod 13 - 6) / 8. C starts at zero. Execute i-k-j loops, caching A[i,k] outside the j loop. Checksum Sum is the row-major sum of C; Weighted uses weight (flatIndex mod 7 + 1).

### FastFourierTransform

Input real[i] = ((i + seed) mod 17 - 8) / 8; imaginary values start at zero. Precompute cos/sin(-2πi/n), then perform bit reversal and increasing radix-2 butterfly lengths. Complex multiply uses four multiplies and the same addition/subtraction order. The forward transform is unnormalized. Sum accumulates real[i]+imag[i]; Weighted uses real weight (i mod 7+1) and imaginary weight (i mod 5+1).

### NBody

Initialize x=(i mod 16-8)+seed/1024, y=(floor(i/16) mod 16-8), z=floor(i/256)-4 and zero velocity. Every body has mass 1/n. Each step visits i<j, computes squared distance plus 0.01, then inverse=1/sqrt(distance) and scale=0.001*inverse*inverse*inverse/n. Update both velocities with equal/opposite displacement*scale. Afterwards advance all positions by 0.001*velocity. Checksum value per body is x+2y+3z+4vx+5vy+6vz; Weighted applies (i mod 7+1).

### Mandelbrot

For each pixel, cr=3*x/n-2 and ci=2*y/n-1. Begin z=0 and iterate while |z|²<=4 and count<Work. Calculate the new real component into a temporary before updating imaginary. Store every escape count; integer Sum and Weighted consume the whole count array. No interior tests or symmetry shortcuts.

### PrimeSieve

Allocate n+1 zero bytes. For i starting at 2 while i<=n/i, mark multiples from i² through n if candidate i is unmarked. Marking alone is timed. The post-timing scan consumes every flag and reports prime count as Sum and sum of primes as Weighted.

## Correctness and limits

Tests/ReferenceCases.json contains small and Smoke fixtures derived independently: dot products, direct O(n²) DFT, per-body force accumulation, complex iteration and trial division. The two-point FFT at seed 8 is the scaled shifted impulse [0, 1/8], whose transform is [1/8, -1/8]. Integer checksums require exact equality. Floating checks use |actual-reference| <= 1e-9 + 1e-9*|reference|. NaN, infinity, missing fields, invalid sample counts and negative durations are failures.

For other sizes, each configuration first passes independent reference cases. Workload outputs are then checked for repeatability and agreement across selected languages. The first successful output is the cross-language comparison value, not an independent oracle. Results label this distinction. A single-language custom-size run establishes repeatability only beyond the reference cases.

Checksums consume all output but can collide; they are regression checks, not mathematical proofs of every element. Platform math libraries can introduce small FFT/NBody differences. A validation failure is preserved, never silently forgiven.

Memory metrics differ by OS: Windows peak working set and Linux maximum RSS include resident runtime/library pages, not just user allocations. Cross-OS comparisons need that context. The memory of the shell or Hyperfine is excluded. Compiler memory is not measured.

Native executable size includes whatever runtime code that toolchain links into its executable. External OS/native library sizes are not included. JIT apphost size alone is not comparable to a self-contained AOT executable: use the accompanying deployable size and runtime requirement.

