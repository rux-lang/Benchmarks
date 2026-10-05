# Validation

Validated on **2026-10-05**. These are implementation checks, not performance claims. Smoke timings from a development session can include background activity and are unsuitable for rankings.

## Completed checks

- Windows 11 x86-64: all 25 configurations completed the smoke matrix, including independent references, clean compilation, process/kernel timing, memory collection and result-schema validation. The final matrix used one measured build, two process/kernel samples, one memory sample and one warm-up per configuration.
- Ubuntu 26.04 under WSL2: all 25 configurations completed the same smoke matrix using native Linux executables. A subsequent five-configuration FFT verification passed the added two-point impulse reference and the final Linux launcher changes.
- The final Linux launcher also completed a MatrixMultiply/Cpp measurement run with two clean builds, two process/kernel samples and one memory sample; its storage metadata and result schema were checked.
- Windows runner: 27 regression assertions passed. These cover argument quoting and paths with spaces, affinity before execution, retained-handle peak memory from a short-lived process, failed processes/builds, missing tools, cleanup boundaries, process-tree timeouts, malformed output, checksum rejection, statistics, metadata overrides, hard interruption, JSON schema and invariant CSV under a French locale.
- Linux runner: regression assertions passed for quoting, affinity, peak RSS, missing tools, failed builds/processes, cleanup boundaries, process-tree timeouts, malformed output, statistics, metadata provenance and interrupted builds. The interruption test checks both child termination and immediate release of the build lock.
- Report parity: PowerShell and Bash processed the same completed Linux run. All 600 populated statistic fields and 150 populated numeric CSV metric fields agreed within a relative tolerance of 1e-12. Both result documents passed the shared JSON schema.

Generated validation artifacts are ignored: Results/WindowsValidated, Results/LinuxSmoke2, Results/LinuxFinalVerify and Results/LinuxFinalMetrics. Results retain exact tool versions, commands, source/package hashes and raw samples.

## Toolchain and platform qualifications

Windows used Clang 23.1.2, Rust 1.99.0, .NET SDK 10.0.401 and Rux 0.4.0 from the matching local checkout. Linux used the same Rust/.NET versions and a native Rux build from that checkout. Hyperfine was 1.21.0 on both platforms.

The available Linux Clang package was **23.1.3 development**, not the requested 23.1.2 stable release; its full identity is recorded. Ubuntu validation ran under WSL2 kernel 6.18.40.1-microsoft-standard-WSL2 on a Windows-mounted filesystem. The portable SDK used invariant globalization because that environment lacked ICU. None of these timings establishes bare-metal Linux performance.

Bare-metal Ubuntu 26.04, other processors, Windows machines with multiple processor groups, and complete Standard/Large matrices remain untested here. Run verify and a smoke matrix on each target computer before collecting its full benchmark results.

## Repeat the regression checks

After installing runner prerequisites:

```powershell
.\Tests\RunnerTests.ps1 -ReportDirectory Results\YourCompletedRun
```

```bash
CXX=clang++-23 bash Tests/RunnerTests.sh
```

ReportDirectory is optional. It enables CSV-locale and result-schema checks against a copy of that run. Both test scripts create fixtures under Build/Tests With Spaces; expected failure diagnostics are part of the checks. Tests include no performance rankings and make no Git commits.

