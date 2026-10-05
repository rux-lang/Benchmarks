function Invoke-Suite {
    if ($Command -eq 'report') {
        if (-not $Output) { throw 'report requires -Output pointing to an existing result directory.' }
        Write-Reports ([IO.Path]::GetFullPath($Output, $PWD.Path))
        return
    }
    $script:Configuration = Get-Content (Join-Path $RepoRoot 'Config/Benchmarks.json') -Raw | ConvertFrom-Json -AsHashtable
    $script:References = Get-Content (Join-Path $RepoRoot 'Tests/ReferenceCases.json') -Raw | ConvertFrom-Json -AsHashtable
    $script:SelectedLanguages = @(if ($Language.Count) { $Language | ForEach-Object { $_.Split(',') } } else { $Configuration.Languages })
    $names = @(if ($Benchmark.Count) { $Benchmark | ForEach-Object { $_.Split(',') } } else { $Configuration.Benchmarks.Name })
    foreach ($name in $SelectedLanguages) { if ($name -cnotin $Configuration.Languages) { throw "Unknown language '$name'" } }
    foreach ($name in $names) { if ($name -cnotin $Configuration.Benchmarks.Name) { throw "Unknown benchmark '$name'" } }
    if (@($SelectedLanguages | Select-Object -Unique).Count -ne $SelectedLanguages.Count) { throw 'Duplicate languages are not allowed.' }
    if (@($names | Select-Object -Unique).Count -ne $names.Count) { throw 'Duplicate benchmarks are not allowed.' }
    if (($Size -or $Work) -and $names.Count -ne 1) { throw 'Custom Size/Work requires exactly one benchmark.' }
    Set-BenchmarkEnvironment
    Initialize-Tools
    $script:PackageHashes = @{}
    if ($Command -eq 'doctor') {
        $Versions | ConvertTo-Json -Depth 5
        Write-Host "Native Windows x86-64; runtime CPU $Cpu; Rux checkout: $RuxRoot"
        return
    }
    Stage-RuxPackages
    if (-not $Output) {
        $script:Output = Join-Path $RepoRoot ('Results/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-Windows')
    } else { $script:Output = [IO.Path]::GetFullPath($Output, $PWD.Path) }
    if (Test-Path -LiteralPath (Join-Path $Output 'Results.json')) { throw "Result directory already contains a run: $Output" }
    foreach ($reserved in 'Artifacts','Projects','RuxPackages','Tools','NuGet','DotnetHome') {
        $boundary = [IO.Path]::GetFullPath((Join-Path $BuildRoot $reserved)).TrimEnd('\')
        if ($Output -eq $boundary -or $Output.StartsWith($boundary + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Results must not be placed inside a generated tool or build directory.'
        }
    }
    [IO.Directory]::CreateDirectory($Output) | Out-Null
    $hashes = @{}
    foreach ($root in 'Benchmarks','Shared','Config','Scripts','Tests') {
        foreach ($file in Get-ChildItem (Join-Path $RepoRoot $root) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](obj|bin|target)[\\/]' }) {
            $relative = [IO.Path]::GetRelativePath($RepoRoot,$file.FullName).Replace('\','/')
            $hashes[$relative] = (Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()
        }
    }
    foreach ($name in 'Run.ps1','Run.sh','global.json') {
        if (Test-Path -LiteralPath (Join-Path $RepoRoot $name)) { $hashes[$name] = (Get-FileHash (Join-Path $RepoRoot $name)).Hash.ToLowerInvariant() }
    }
    $script:Document = [ordered]@{
        SchemaVersion=1; RunId=(Split-Path -Leaf $Output); StartedAt=[DateTime]::UtcNow.ToString('o'); UpdatedAt=$null;
        Platform='Windows'; Architecture='x86-64'; Profile=$Profile; Command=$Command; Status='Running';
        Settings=@{ BuildRuns=$BuildRuns; RunRuns=$RunRuns; MemoryRuns=$MemoryRuns; Warmups=$Warmups;
                    Cpu=$Cpu; TimeoutSeconds=$TimeoutSeconds; CpuPolicy='PinnedRunsSerialBuilds';
                    Isa='x86-64-baseline'; JitPolicy='NoTieringNoConcurrentGC'; FilesystemCache='Warm' };
        Machine=(Get-MachineMetadata); Toolchains=$Versions; SourceHashes=$hashes; RuxPackageHashes=$PackageHashes;
        Results=[Collections.Generic.List[object]]::new()
    }
    Save-Results
    $failed = $false
    try {
        foreach ($name in $names) {
            $definition = $Configuration.Benchmarks | Where-Object Name -CEQ $name
            $parameters = @{ Size=$definition.Profiles[$Profile].Size; Work=$definition.Profiles[$Profile].Work; Seed=$Seed }
            if ($Size) { $parameters.Size=$Size }; if ($Work) { $parameters.Work=$Work }
            if ($parameters.Size -gt $definition.MaxSize -or $parameters.Size -lt 2 -or
                ($definition.PowerOfTwo -and ($parameters.Size -band ($parameters.Size-1)))) { throw "Invalid size for $name" }
            $expected = $References.Cases | Where-Object {
                $_.Benchmark -ceq $name -and $_.Size -eq $parameters.Size -and
                $_.Work -eq $parameters.Work -and $_.Seed -eq $parameters.Seed
            } | Select-Object -First 1
            $validationKind = if ($expected) { 'IndependentReference' } else { 'ReferenceCasesAndCrossLanguage' }
            foreach ($lang in $SelectedLanguages) {
                $dir = Join-Path $Output "$name/$lang"
                [IO.Directory]::CreateDirectory($dir) | Out-Null
                $row = [ordered]@{
                    Benchmark=$name; Language=$lang; Parameters=$parameters; Status='Building'; Error=$null;
                    BuildKind=$(if ($lang -eq 'CSharpJit') { 'ManagedBuild' } else { 'NativeCompileAndLink' });
                    BuildCommand=$null; Dependencies=@(); ExecutableBytes=$null; DeployableBytes=$null; ManagedPayloadBytes=$null;
                    RuntimeRequirement=$(if ($lang -eq 'CSharpJit') { '.NET 10 x64 shared runtime' } else { 'OS native libraries' });
                    MemoryMetric='WindowsPeakWorkingSet'; Validation=$validationKind;
                    BuildSeconds=@(); ProcessSeconds=@(); KernelSeconds=@(); PeakMemoryBytes=@(); Checksums=@()
                }
                $Document.Results.Add($row); Save-Results
                Write-Host "$name / $lang : preparing and validating"
                try {
                    $case = Prepare-Case $name $lang "$dir/Prepare"
                    $row.BuildCommand = @{ File=$case.File; Arguments=$case.Arguments }
                    Invoke-Child $case.File $case.Arguments "$dir/Prepare.Build.log" | Out-Null
                    if ($Command -ne 'prepare') { Verify-Case $case $definition $dir }
                    if ($Command -eq 'run') {
                        for ($i=0; $i -lt $BuildRuns; $i++) {
                            $case = Prepare-Case $name $lang "$dir/Build$i"
                            $row.BuildSeconds += Measure-Command $case.File $case.Arguments "$dir/Build$i"
                            Save-Results
                        }
                    }
                    $row.ExecutableBytes = (Get-Item -LiteralPath $case.Executable).Length
                    if ($lang -like 'CSharp*') {
                        $assets = Get-Content "$($case.Stage)/Obj/project.assets.json" -Raw | ConvertFrom-Json -AsHashtable
                        $row.Dependencies = @($assets.libraries.Keys | Sort-Object)
                    }
                    $payload = @(Get-ChildItem -LiteralPath $case.DeployDirectory -File -Recurse | Where-Object {
                        $_.Extension -notin '.pdb','.dbg','.lib','.exp','.obj' -and $_.Name -notlike '*.debug'
                    })
                    $row.DeployableBytes = ($payload | Measure-Object Length -Sum).Sum
                    if ($lang -eq 'CSharpJit') { $row.ManagedPayloadBytes = (Get-Item "$($case.DeployDirectory)/$name.dll").Length }
                    if ($Command -eq 'run') {
                        $row.Status='Measuring'; Save-Results
                        Write-Input "$dir/Input.txt" $parameters 0 1
                        for ($i=0; $i -lt $Warmups; $i++) {
                            Invoke-Child $case.Executable @() "$dir/Warmup$i.json" "$dir/Input.txt" $Cpu | Out-Null
                            $v = Test-Output "$dir/Warmup$i.json" $definition $parameters 0 1 $expected
                            if (-not $expected) { $expected=$v.Samples[0] }
                        }
                        for ($i=0; $i -lt $RunRuns; $i++) {
                            $elapsed = Measure-Command $case.Executable @() "$dir/Process$i" "$dir/Input.txt" $Cpu
                            $v = Test-Output "$dir/Process$i.Output.json" $definition $parameters 0 1 $expected
                            if (-not $expected) { $expected=$v.Samples[0] }
                            $row.ProcessSeconds += $elapsed; $row.Checksums += $v.Samples[0]; Save-Results
                        }
                        Write-Input "$dir/KernelInput.txt" $parameters $Warmups $RunRuns
                        Invoke-Child $case.Executable @() "$dir/Kernel.json" "$dir/KernelInput.txt" $Cpu | Out-Null
                        $v = Test-Output "$dir/Kernel.json" $definition $parameters $Warmups $RunRuns $expected
                        $row.KernelSeconds = @($v.Samples | ForEach-Object { $_.Seconds }); Save-Results
                        for ($i=0; $i -lt $MemoryRuns; $i++) {
                            $usage = Invoke-Child $case.Executable @() "$dir/Memory$i.json" "$dir/Input.txt" $Cpu
                            Test-Output "$dir/Memory$i.json" $definition $parameters 0 1 $expected | Out-Null
                            $row.PeakMemoryBytes += $usage.PeakWorkingSetBytes; Save-Results
                        }
                    }
                    $row.Status = if ($Command -eq 'run') { 'Completed' } elseif ($Command -eq 'verify') { 'Verified' } else { 'Prepared' }
                } catch {
                    $row.Status='Failed'; $row.Error=$_.Exception.Message; $failed=$true
                    Write-Warning "$name / $lang : $($row.Error)"
                } finally { Save-Results }
            }
        }
        $Document.Status = if ($failed) { 'Failed' } else { 'Completed' }
    } finally {
        if ($Document.Status -eq 'Running') {
            $Document.Status='Interrupted'
            foreach ($row in $Document.Results) { if ($row.Status -in 'Building','Measuring') { $row.Status='Interrupted' } }
        }
        Save-Results
        Write-Reports $Output
        Write-Host "Results: $Output"
    }
    if ($failed) { throw 'One or more configurations failed. Inspect Results.json and retained logs.' }
}

