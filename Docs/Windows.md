# Windows 11

Use native x86-64 tools and PowerShell 7.2+. Do not use Git Bash/MSYS to measure Windows programs.

## Install

- [LLVM/Clang](https://github.com/llvm/llvm-project/releases), initially 23.1.2.
- Visual Studio Build Tools or Visual Studio with **Desktop development with C++**, MSVC x64 tools and Windows SDK. Clang, Rust/MSVC and Native AOT need native linking support. Use a Developer PowerShell if automatic discovery fails.
- [Rust/rustup](https://rustup.rs/), then `rustup update stable`; initially 1.99.0, x86_64-pc-windows-msvc.
- [.NET SDK 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), initially 10.0.401 / C# 14. global.json permits stable patches in this feature band.
- A Rux compiler and matching source checkout.
- [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows) and [Hyperfine](https://github.com/sharkdp/hyperfine/releases), initially 1.21.0.

Put tools on PATH or supply explicit paths. Hyperfine can also be extracted under Build/Tools. jq and GNU time are not needed on Windows. Upgrades happen between runs, never during measurements; exact versions are saved.

WindowsProcess.cs is an API binding compiled in memory by PowerShell. It starts children suspended, applies affinity before their first instruction, and uses a job object for timeout/child cleanup. A retained process handle provides peak working set after exit. No periodic memory polling or separate runner executable is used.

```powershell
.\Run.ps1 doctor -RuxRoot D:\Work\Rux
.\Run.ps1 verify -Profile Smoke -RuxRoot D:\Work\Rux
.\Run.ps1 run -Profile Standard -Cpu 2 -RuxRoot D:\Work\Rux
```

Use an ordinary account. If script policy blocks execution, follow your organization's approved PowerShell policy; the repository does not change it.

CPU indices 0–63 apply within the runner's Windows processor group and allowed affinity mask. On multi-group machines, start the runner in the intended group. On hybrid CPUs, explicitly choose and document a performance or efficiency core.

CIM hardware discovery may be restricted. Missing data is recorded as unavailable; manual metadata can fill gaps.

