# Ubuntu 26.04 LTS

Use native Linux x86-64 binaries. WSL2 is suitable for runner validation, but its virtualized environment and filesystem must stay identifiable in results.

## Install

```bash
sudo apt-get update
sudo apt-get install build-essential curl git jq time util-linux hyperfine zlib1g-dev
```

Install current stable [LLVM](https://github.com/llvm/llvm-project/releases) or use the [LLVM apt repository](https://apt.llvm.org/). Ubuntu's unversioned clang package can be older. The initial target is Clang 23.1.2; select a versioned installation with `--cpp clang++-23`. Record the full version; development snapshots are not stable releases.

Install [Rust with rustup](https://rustup.rs/) and update stable to 1.99.0 or newer. Install [.NET SDK 10 and its Ubuntu prerequisites](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu), including runtime libraries such as ICU. The initial SDK is 10.0.401; global.json permits stable patches within this feature band.

Hyperfine 1.21.0 is initially validated. Distribution versions must provide --input, --output and --export-json. Official Rust, .NET, jq and Hyperfine distributions can alternatively be installed in a user-writable directory. Microsoft's [dotnet-install script](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script) supports a custom install directory.

Follow the matching Rux checkout's Linux guide. Building Rux itself requires Clang 23.1+, CMake 3.31+ and Ninja 1.13.2+; these are not runner dependencies.

```bash
bash Run.sh doctor --cpp clang++-23 --rux ../Rux/Bin/rux --rux-root ../Rux
bash Run.sh verify --profile Smoke --cpp clang++-23 --rux ../Rux/Bin/rux --rux-root ../Rux
bash Run.sh run --profile Standard --cpp clang++-23 --rux ../Rux/Bin/rux --rux-root ../Rux
```

The runner uses taskset for affinity, timeout for process-group termination, /usr/bin/time for maximum RSS, and jq for validation/reporting. It invokes neither Python nor PowerShell.

Use an executable local Linux filesystem for useful measurements. A WSL /mnt mount changes compilation and startup behavior. CPU selection respects allowed CPU lists and cgroups. Memory speed/firmware information may require manual metadata instead of privileged discovery.

