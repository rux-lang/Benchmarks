function Write-Json($Value, [string]$Path) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    $temporary = "$Path.tmp"
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 40) + [Environment]::NewLine)
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
function Quote-Argument([string]$Value) {
    '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}
function Join-Command([string]$File, [string[]]$Arguments) {
    ((@($File) + $Arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
}
function Remove-Generated([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $boundary = [IO.Path]::GetFullPath($BuildRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing cleanup outside Build: $resolved"
    }
    $ancestor = Split-Path -Parent $resolved
    while ($ancestor -eq $BuildRoot -or $ancestor.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing cleanup beneath a reparse point: $ancestor"
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    if (Test-Path -LiteralPath $resolved) {
        $items = @(Get-Item -LiteralPath $resolved) + @(Get-ChildItem -LiteralPath $resolved -Force -Recurse)
        if ($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
            throw "Refusing cleanup through a reparse point: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
function Find-Tool([string]$Name) {
    $found = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $found) { throw "Missing tool '$Name'. See Docs/Windows.md and run doctor after installation." }
    $found.Source
}
function Invoke-Child([string]$File, [string[]]$Arguments, [string]$Log, [string]$InputFile = '', [int]$PinnedCpu = -1) {
    if (-not $InputFile) { $InputFile = Join-Path $BuildRoot 'Empty.txt' }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Log)) | Out-Null
    $result = [WindowsProcess]::Run((Join-Command $File $Arguments), $RepoRoot, $InputFile,
                                   $Log, "$Log.stderr", $PinnedCpu, $TimeoutSeconds)
    if ($result.TimedOut) { throw "Timed out after $TimeoutSeconds seconds: $File (logs: $Log)" }
    if ($result.ExitCode -ne 0) {
        $detail = (Get-Content -LiteralPath "$Log.stderr" -Raw) + (Get-Content -LiteralPath $Log -Raw)
        throw "Exit $($result.ExitCode): $File. $detail. Log: $Log"
    }
    return $result
}
function Initialize-Tools {
    [IO.Directory]::CreateDirectory($BuildRoot) | Out-Null
    [IO.File]::WriteAllText((Join-Path $BuildRoot 'Empty.txt'), '')
    if (-not ('WindowsProcess' -as [type])) { Add-Type -Path (Join-Path $RepoRoot 'Scripts/WindowsProcess.cs') }
    $script:Tools = @{}
    if ('Rux' -in $SelectedLanguages) { $Tools.Rux = Find-Tool $Rux }
    if ('Cpp' -in $SelectedLanguages) { $Tools.Cpp = Find-Tool $Cpp }
    if ('Rust' -in $SelectedLanguages) { $Tools.Rust = Find-Tool $Rust }
    if (@($SelectedLanguages | Where-Object { $_ -like 'CSharp*' }).Count) { $Tools.Dotnet = Find-Tool $Dotnet }
    if ($Tools.ContainsKey('Dotnet')) {
        $env:DOTNET_ROOT = Split-Path -Parent $Tools.Dotnet
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    }
    if ($Hyperfine -eq 'hyperfine' -and -not (Get-Command hyperfine -ErrorAction SilentlyContinue)) {
        $localTool = Get-ChildItem (Join-Path $BuildRoot 'Tools') -Filter hyperfine.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($localTool) { $script:Hyperfine = $localTool.FullName }
    }
    $Tools.Hyperfine = Find-Tool $Hyperfine
    $script:Versions = @{}
    foreach ($key in $Tools.Keys) {
        $log = Join-Path $BuildRoot "Doctor/$key.txt"
        Invoke-Child $Tools[$key] @('--version') $log | Out-Null
        $Versions[$key] = @{ Path = $Tools[$key]; Version = (Get-Content -LiteralPath $log -Raw).Trim();
                            Sha256 = (Get-FileHash -LiteralPath $Tools[$key]).Hash.ToLowerInvariant() }
        if ($key -in 'Rust','Dotnet') {
            $detailArgument = if ($key -eq 'Rust') { '-vV' } else { '--info' }
            Invoke-Child $Tools[$key] @($detailArgument) "$log.details" | Out-Null
            $Versions[$key].Details = (Get-Content -LiteralPath "$log.details" -Raw).Trim()
        }
    }
    if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') { throw 'Native x86-64 is required.' }
    $script:AllowedMask = [WindowsProcess]::AllowedMask()
    if ($Cpu -lt 0) {
        for ($c = 0; $c -lt 64; $c++) { if ($AllowedMask -band ([uint64]1 -shl $c)) { $script:Cpu = $c; break } }
    }
    if (-not ($AllowedMask -band ([uint64]1 -shl $Cpu))) { throw "CPU $Cpu is outside this process's allowed processor-group mask." }
    if ('Rux' -in $SelectedLanguages) {
        if (-not $RuxRoot) {
            $candidate = Join-Path (Split-Path -Parent $RepoRoot) 'Rux'
            if (Test-Path -LiteralPath (Join-Path $candidate 'Packages')) { $script:RuxRoot = $candidate }
        }
        if (-not $RuxRoot -or -not (Test-Path -LiteralPath (Join-Path $RuxRoot 'Packages/Core/Rux.toml'))) {
            throw 'Specify -RuxRoot with a matching Rux source checkout.'
        }
        $script:RuxRoot = [IO.Path]::GetFullPath($RuxRoot, $PWD.Path)
    }
}
function Set-BenchmarkEnvironment {
    # These settings are process-local. No persistent machine settings are changed.
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_HOME = Join-Path $BuildRoot 'DotnetHome'
    $env:NUGET_PACKAGES = Join-Path $BuildRoot 'NuGet'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:CARGO_INCREMENTAL = '0'
    $env:RUSTC_WRAPPER = ''
    $env:RUSTC_WORKSPACE_WRAPPER = ''
    $env:DOTNET_TieredCompilation = '0'
    $env:DOTNET_TieredPGO = '0'
    $env:DOTNET_gcServer = '0'
    $env:DOTNET_gcConcurrent = '0'
    $env:DOTNET_EnableHWIntrinsic = '0'
    $env:DOTNET_EnableAVX = '0'
    $env:DOTNET_EnableFMA = '0'
    $env:DOTNET_EnableSSE3 = '0'
}
function Stage-RuxPackages {
    if ('Rux' -notin $SelectedLanguages) { return }
    $target = Join-Path $BuildRoot 'RuxPackages'
    Remove-Generated $target
    [IO.Directory]::CreateDirectory($target) | Out-Null
    $pending = [Collections.Generic.Queue[string]]::new()
    'Core','Allocator','Collections','Io','Math','Time' | ForEach-Object { $pending.Enqueue($_) }
    $copied = @{}
    $script:PackageHashes = @{}
    while ($pending.Count) {
        $name = $pending.Dequeue()
        if ($copied.ContainsKey($name)) { continue }
        $copied[$name] = $true
        $source = Join-Path $RuxRoot "Packages/$name"
        if (-not (Test-Path -LiteralPath "$source/Rux.toml")) { throw "Rux package missing: $source" }
        foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse | Sort-Object FullName) {
            $relative = [IO.Path]::GetRelativePath($RuxRoot, $file.FullName).Replace('\','/')
            $PackageHashes[$relative] = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
        }
        Copy-Item -LiteralPath $source -Destination $target -Recurse
        $manifest = Join-Path $target "$name/Rux.toml"
        $content = Get-Content -LiteralPath $manifest -Raw
        $content = [regex]::Replace($content, '(?m)^(\w+) = \{ Namespace = "Rux", Version = "[^"]+"(.*?) \}', {
            param($m)
            $pending.Enqueue($m.Groups[1].Value)
            $m.Groups[1].Value + ' = { Path = "../' + $m.Groups[1].Value + '"' + $m.Groups[2].Value + ' }'
        })
        [IO.File]::WriteAllText($manifest, $content)
    }
}
function Prepare-Case([string]$Name, [string]$Lang, [string]$LogPrefix) {
    $stage = Join-Path $BuildRoot "Artifacts/$Name/$Lang"
    Remove-Generated $stage
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $source = Join-Path $RepoRoot "Benchmarks/$Name"
    $exe = Join-Path $stage "$Name.exe"
    $deploy = $stage
    switch ($Lang) {
        'Cpp' {
            $file = $Tools.Cpp
            $arguments = @('-std=c++2c','-O3','-DNDEBUG','-g0','-march=x86-64','-mtune=generic',
                           '-fno-fast-math','-ffp-contract=off',"$source/Cpp/Main.cpp",'-o',$exe)
        }
        'Rust' {
            $file = $Tools.Rust
            $arguments = @('--edition=2024','-C','opt-level=3','-C','target-cpu=x86-64',
                           '-C','debuginfo=0','-C','strip=symbols',"$source/Rust/src/main.rs",'-o',$exe)
        }
        'Rux' {
            $project = Join-Path $BuildRoot "Projects/$Name/Rux"
            Remove-Generated $project
            [IO.Directory]::CreateDirectory((Split-Path -Parent $project)) | Out-Null
            Copy-Item -LiteralPath "$source/Rux" -Destination $project -Recurse
            Copy-Item -LiteralPath (Join-Path $RepoRoot 'Shared/Rux/Benchmark.rux') -Destination "$project/Src/Benchmark.rux"
            $file = $Tools.Rux
            $arguments = @('--manifest',"$project/Rux.toml",'--color=never','build','--release','--quiet')
            $deploy = "$project/Bin/Release/Windows/x86-64"
            $exe = "$deploy/$Name.exe"
        }
        { $_ -in 'CSharpAot','CSharpJit' } {
            $aot = if ($Lang -eq 'CSharpAot') { 'true' } else { 'false' }
            $properties = @("-p:BaseIntermediateOutputPath=$stage/Obj/","-p:MSBuildProjectExtensionsPath=$stage/Obj/",
                            "-p:BaseOutputPath=$stage/Bin/","-p:PublishAot=$aot","-p:SelfContained=$aot")
            $project = "$source/CSharp/$Name.csproj"
            Invoke-Child $Tools.Dotnet (@('restore',$project,'-r','win-x64','--disable-build-servers') + $properties) "$LogPrefix.Restore.log" | Out-Null
            $file = $Tools.Dotnet
            $deploy = "$stage/Publish"
            $exe = "$deploy/$Name.exe"
            $arguments = @('publish',$project,'-c','Release','-r','win-x64','--no-restore','--disable-build-servers',
                           '-o',$deploy) + $properties
        }
        default { throw "No adapter for $Lang" }
    }
    @{ File=$file; Arguments=$arguments; Executable=$exe; DeployDirectory=$deploy; Stage=$stage }
}
function Write-Input([string]$Path, $Parameters, [int]$WarmupCount, [int]$SampleCount) {
    [IO.File]::WriteAllText($Path, "1 $($Parameters.Size) $($Parameters.Work) $($Parameters.Seed) $WarmupCount $SampleCount" + [char]10)
}
function Test-Output([string]$Path, $Definition, $Parameters, [int]$WarmupCount, [int]$SampleCount, $Expected = $null) {
    $value = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in 'Protocol','Size','Work','Seed','Warmups') {
        if ($key -cnotin $value.Keys -or -not ($value[$key] -is [long] -or $value[$key] -is [int])) {
            throw "Invalid integer output '$key': $Path"
        }
    }
    if ('Benchmark' -cnotin $value.Keys -or 'Samples' -cnotin $value.Keys -or $value.Samples -isnot [array]) {
        throw "Invalid output structure: $Path"
    }
    if ($value.Protocol -ne 1 -or $value.Benchmark -cne $Definition.Name -or
        $value.Size -ne $Parameters.Size -or $value.Work -ne $Parameters.Work -or
        $value.Seed -ne $Parameters.Seed -or $value.Warmups -ne $WarmupCount -or
        @($value.Samples).Count -ne $SampleCount) { throw "Output protocol mismatch: $Path" }
    foreach ($sample in $value.Samples) {
        foreach ($key in 'Seconds','Sum','Weighted') {
            if ($key -cnotin $sample.Keys -or
                -not ($sample[$key] -is [long] -or $sample[$key] -is [int] -or $sample[$key] -is [double] -or $sample[$key] -is [decimal]) -or
                $null -eq $sample[$key] -or -not [double]::IsFinite([double]$sample[$key])) {
                throw "Invalid numeric output '$key': $Path"
            }
        }
        if ($sample.Seconds -lt 0) { throw "Negative duration: $Path" }
        if ($null -eq $Expected) { $Expected = $sample }
        foreach ($key in 'Sum','Weighted') {
            $delta = [Math]::Abs([double]$sample[$key] - [double]$Expected[$key])
            $tolerance = if ($Definition.Integer) { 0.0 } else {
                $Definition.AbsoluteTolerance + $Definition.RelativeTolerance * [Math]::Abs([double]$Expected[$key])
            }
            if ($delta -gt $tolerance) { throw "Incorrect $($Definition.Name) $key in $Path : $($sample[$key]) expected $($Expected[$key])" }
        }
    }
    return $value
}
function Verify-Case($Case, $Definition, [string]$Directory) {
    $index = 0
    foreach ($reference in $References.Cases | Where-Object Benchmark -CEQ $Definition.Name) {
        $path = "$Directory/Verify$index"
        Write-Input "$path.txt" $reference 0 1
        Invoke-Child $Case.Executable @() "$path.json" "$path.txt" $Cpu | Out-Null
        Test-Output "$path.json" $Definition $reference 0 1 $reference | Out-Null
        $index++
    }
    [IO.File]::WriteAllText("$Directory/Invalid.txt", "1 0 1 1 0 1" + [char]10)
    $bad = [WindowsProcess]::Run((Join-Command $Case.Executable @()), $RepoRoot, "$Directory/Invalid.txt",
        "$Directory/Invalid.json", "$Directory/Invalid.stderr", $Cpu, $TimeoutSeconds)
    if ($bad.TimedOut -or $bad.ExitCode -eq 0) { throw "Invalid input was not rejected: $($Case.Executable)" }
}
function Measure-Command($File, $Arguments, [string]$Prefix, [string]$InputFile='', [int]$PinnedCpu=-1) {
    $hfArguments = @('--shell=none','--runs','1','--style','none','--export-json',"$Prefix.Timing.json",
                     '--output',"$Prefix.Output.json")
    if ($InputFile) { $hfArguments += @('--input',$InputFile) }
    $hfArguments += @('--', (Join-Command $File $Arguments))
    Invoke-Child $Tools.Hyperfine $hfArguments "$Prefix.Hyperfine.log" '' $PinnedCpu | Out-Null
    $raw = Get-Content -LiteralPath "$Prefix.Timing.json" -Raw | ConvertFrom-Json
    if (@($raw.results).Count -ne 1 -or @($raw.results[0].times).Count -ne 1) { throw "Invalid Hyperfine export: $Prefix" }
    $seconds = [double]$raw.results[0].times[0]
    if (-not [double]::IsFinite($seconds) -or $seconds -lt 0) { throw 'Invalid external timing' }
    return $seconds
}

