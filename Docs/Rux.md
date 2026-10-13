# Rux notes

- **Command-line arguments.** Rux 0.4.0 calls `Main()` without arguments and no package exposes them, so every Rux app carries the same small `Src/Arguments.rux`. It reads the command line from `GetCommandLineA` on Windows and `/proc/self/cmdline` on Linux. `doctor` checks that all copies are identical.
- **Packages.** Rux apps depend on `{ Namespace = "Rux", Version = "*" }` packages, which `rux build` takes from the local package cache (`%LOCALAPPDATA%\Rux\Packages` or `~/.rux/packages`) without network access. Run `rux install` once to fill it:

  ```bash
  rux --manifest Apps/Sha512/Rux/Rux.toml install
  ```

- **Small allocations.** `SystemAllocator` requests whole pages from the operating system for every allocation, so BinaryTrees uses `Allocator::Pool`, Rux's documented allocator for many small objects.
- **Standard library.** Rux compiles its standard library from source on every build (about 40,000 lines), while the other languages link a precompiled one. That is most of Rux's compile time for the one-file apps; the [compile benchmark](CompileBenchmark.md) separates it from the cost of compiling the project's own code.
