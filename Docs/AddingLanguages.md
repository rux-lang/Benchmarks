# Adding a language

Go and Java are intentionally not implemented yet.

1. Add a project for each workload under Benchmarks/<Workload>/<Language>, using that language's normal source naming.
2. Implement the stdin/JSON contract in Docs/Results.md. Share only harness code; keep each algorithm readable and match Docs/Methodology.md.
3. Add its configuration name to Config/Benchmarks.json and build/restore adapters to both Scripts/Runner.ps1 and Scripts/Runner.sh. Return build executable/argv, program executable and deployment directory. No shell eval or command interpolation.
4. Keep dependency restoration, downloads and cleanup outside compilation timing. Retain exact toolchain/dependency versions.
5. Document startup, runtime, memory and size semantics. Java JIT should use a separately named configuration and appropriate in-process warm-up/runtime settings. Future Go runtime helper threads must share the pinned CPU.
6. Pass independent reference cases, invalid-input tests, native platform smoke matrices and reporting parity tests.

For a runtime launched as an interpreter/VM command, extend the adapter's runtime interface to carry executable and argument arrays rather than hiding a shell script inside the timing. Label JVM/.NET runtime requirements separately from application payload size.

When changing an algorithm or input formula, update all implementations, independent fixtures and documentation together. Never use a language-specific algorithmic shortcut in a compiler-efficiency comparison.

