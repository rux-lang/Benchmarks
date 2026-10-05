function Get-Statistics($Values) {
    $a = @($Values | Sort-Object)
    if (-not $a.Count) { return $null }
    $mean = ($a | Measure-Object -Average).Average
    $median = if ($a.Count % 2) { $a[[int][Math]::Floor($a.Count/2)] } else { ($a[$a.Count/2-1] + $a[$a.Count/2])/2 }
    $squares = 0.0
    foreach ($v in $a) { $squares += ($v-$mean)*($v-$mean) }
    @{ Count=$a.Count; Median=$median; Minimum=$a[0]; Maximum=$a[-1]; Mean=$mean;
       StandardDeviation=$(if ($a.Count -gt 1) { [Math]::Sqrt($squares/($a.Count-1)) } else { $null }) }
}
function Get-MachineMetadata {
    $warnings = @()
    $detected = @{ OS=[Runtime.InteropServices.RuntimeInformation]::OSDescription;
        Architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString();
        LogicalProcessors=[Environment]::ProcessorCount; AffinityMask=$AllowedMask.ToString();
        SelectedCpu=$Cpu; ProcessorGroupPolicy='CurrentRunnerGroup' }
    try {
        $detected.Cpu = @(Get-CimInstance Win32_Processor | Select-Object Name,Manufacturer,NumberOfCores,
            NumberOfLogicalProcessors,MaxClockSpeed,L2CacheSize,L3CacheSize,Architecture)
        $detected.Memory = @(Get-CimInstance Win32_PhysicalMemory | Select-Object Capacity,Speed,ConfiguredClockSpeed,Manufacturer,PartNumber)
        $detected.OperatingSystem = Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,TotalVisibleMemorySize
        $detected.Storage = @(Get-CimInstance Win32_DiskDrive | Select-Object Model,MediaType,InterfaceType,Size)
        $detected.Bios = Get-CimInstance Win32_BIOS | Select-Object Manufacturer,SMBIOSBIOSVersion,ReleaseDate
    } catch { $warnings += "CIM metadata unavailable: $($_.Exception.Message)" }
    try { $detected.PowerPolicy = (& powercfg /getactivescheme | Out-String).Trim() } catch { $warnings += 'Power policy unavailable' }
    $detected.CpuFeatures = @{ Sse2=[Runtime.Intrinsics.X86.Sse2]::IsSupported; Avx=[Runtime.Intrinsics.X86.Avx]::IsSupported;
                              Avx2=[Runtime.Intrinsics.X86.Avx2]::IsSupported; Fma=[Runtime.Intrinsics.X86.Fma]::IsSupported }
    $manual = if ($Machine) { Get-Content -LiteralPath $Machine -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
    $effective = @{} + $detected
    if ($manual.ContainsKey('Overrides')) { foreach ($key in $manual.Overrides.Keys) { $effective[$key] = $manual.Overrides[$key] } }
    @{ Detected=$detected; Manual=$manual; Effective=$effective; Warnings=$warnings }
}
function Save-Results {
    $Document.UpdatedAt = [DateTime]::UtcNow.ToString('o')
    Write-Json $Document (Join-Path $Output 'Results.json')
}
function Write-Reports([string]$Directory) {
    $data = Get-Content -LiteralPath (Join-Path $Directory 'Results.json') -Raw | ConvertFrom-Json -AsHashtable
    $rows = foreach ($r in $data.Results) {
        $compile = Get-Statistics $r.BuildSeconds; $process = Get-Statistics $r.ProcessSeconds
        $kernel = Get-Statistics $r.KernelSeconds; $memory = Get-Statistics $r.PeakMemoryBytes
        $r.Statistics = @{ Compilation=$compile; Process=$process; Kernel=$kernel; Memory=$memory }
        [pscustomobject][ordered]@{
            Benchmark=$r.Benchmark; Language=$r.Language; Profile=$data.Profile; Status=$r.Status;
            Size=$r.Parameters.Size; Work=$r.Parameters.Work; Seed=$r.Parameters.Seed;
            BuildMedianSeconds=$(if ($compile) { $compile.Median } else { $null });
            ProcessMedianSeconds=$(if ($process) { $process.Median } else { $null });
            KernelMedianSeconds=$(if ($kernel) { $kernel.Median } else { $null });
            PeakMemoryMedianBytes=$(if ($memory) { $memory.Median } else { $null });
            ExecutableBytes=$r.ExecutableBytes; DeployableBytes=$r.DeployableBytes;
            MemoryMetric=$r.MemoryMetric; RuntimeRequirement=$r.RuntimeRequirement
        }
    }
    Write-Json $data (Join-Path $Directory 'Results.json')
    @($rows) | ForEach-Object {
        $cells = [ordered]@{}
        foreach ($property in $_.PSObject.Properties) {
            $cells[$property.Name] = if ($property.Value -is [IFormattable]) {
                $property.Value.ToString($null, [Globalization.CultureInfo]::InvariantCulture)
            } else { $property.Value }
        }
        [pscustomobject]$cells
    } | Export-Csv -LiteralPath (Join-Path $Directory 'Summary.csv') -NoTypeInformation -Encoding utf8
    $lines = @('# Benchmark results', '', "Run: $($data.RunId) | Platform: $($data.Platform) | Profile: $($data.Profile)",
        '', 'Times are seconds; sizes are bytes. Full samples, statistics, validation, and machine provenance are in Results.json.',
        '', '| Benchmark | Language | Status | Build median | Process median | Kernel median | Peak memory median | Executable | Deployable |',
        '|---|---|---|---:|---:|---:|---:|---:|---:|')
    foreach ($row in $rows) {
        $cells = @($row.BuildMedianSeconds,$row.ProcessMedianSeconds,$row.KernelMedianSeconds,
                   $row.PeakMemoryMedianBytes,$row.ExecutableBytes,$row.DeployableBytes) | ForEach-Object {
            if ($null -eq $_) { '—' } else { ([double]$_).ToString('G8',[Globalization.CultureInfo]::InvariantCulture) }
        }
        $lines += "| $($row.Benchmark) | $($row.Language) | $($row.Status) | $($cells -join ' | ') |"
    }
    $lines += @('', 'Fresh-process time includes startup, initialization, validation, output and teardown. Kernel time excludes these.',
        'Memory is process peak working set on Windows and process maximum RSS on Linux; the metrics are related, not identical.',
        'CSharpJit build time creates managed code. Its deployable size excludes the shared .NET runtime.',
        'Failed and interrupted rows are incomplete; do not rank them against completed rows.')
    [IO.File]::WriteAllText((Join-Path $Directory 'Report.md'), ($lines -join [Environment]::NewLine) + [Environment]::NewLine)
}

