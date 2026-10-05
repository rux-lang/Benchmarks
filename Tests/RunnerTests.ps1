#Requires -Version 7.2
param([string]$Cpp = 'clang++', [string]$ReportDirectory = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$BuildRoot = Join-Path $RepoRoot 'Build'
$TimeoutSeconds = 10
. "$RepoRoot/Scripts/Runner.ps1"
. "$RepoRoot/Scripts/Report.ps1"
if (-not ('WindowsProcess' -as [type])) { Add-Type -Path "$RepoRoot/Scripts/WindowsProcess.cs" }
$directory = Join-Path $BuildRoot ('Tests With Spaces/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
[IO.File]::WriteAllText("$BuildRoot/Empty.txt", '')
$exe = "$directory/Process Probe.exe"
& $Cpp -std=c++2c -O2 "$PSScriptRoot/Fixtures/ProcessProbe.cpp" -o $exe
if ($LASTEXITCODE) { throw 'Fixture compilation failed' }
$passed = 0
function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $script:passed++
}
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    $caught = $false
    try { & $Action | Out-Null } catch { $caught = $true }
    Assert $caught $Message
}
$mask = [WindowsProcess]::AllowedMask()
$Cpu = 0
while (-not ($mask -band ([uint64]1 -shl $Cpu))) { $Cpu++ }
$argument = 'alpha beta "gamma"\tail\'
Invoke-Child $exe @('echo',$argument) "$directory/Echo.txt" '' $Cpu | Out-Null
Assert ((Get-Content "$directory/Echo.txt" -Raw) -ceq $argument) 'Windows quoting preserves spaces, quotes and trailing backslashes'
Invoke-Child $exe @('affinity') "$directory/Affinity.txt" '' $Cpu | Out-Null
Assert ((Get-Content "$directory/Affinity.txt" -Raw) -eq '1') 'Affinity applies before process code runs'
$memory = Invoke-Child $exe @('memory') "$directory/Memory.txt" '' $Cpu
Assert ($memory.PeakWorkingSetBytes -ge 64MB) 'Retained handle reports transient peak after child exit'
Assert-Throws { Invoke-Child $exe @('fail') "$directory/Failure.txt" } 'Child failures propagate'
Assert-Throws { Find-Tool 'benchmark-nonexistent-tool-73aa' } 'Missing tools fail explicitly'
Assert-Throws { Remove-Generated $RepoRoot } 'Cleanup rejects repository root'
Assert-Throws { Remove-Generated (Split-Path -Parent $BuildRoot) } 'Cleanup rejects parent paths'
$TimeoutSeconds = 1
Assert-Throws { Invoke-Child $exe @('spawn') "$directory/Timeout.txt" '' $Cpu } 'Timeout terminates process tree'
Start-Sleep -Milliseconds 150
$childPid = [int](Get-Content "$directory/Timeout.txt" -Raw).Trim()
Assert (-not (Get-Process -Id $childPid -ErrorAction SilentlyContinue)) 'Job cleanup terminates descendants'
$TimeoutSeconds = 10
$stats = Get-Statistics @(4,1,3,2)
Assert ($stats.Median -eq 2.5 -and [Math]::Abs($stats.StandardDeviation-1.2909944487358056) -lt 1e-12) 'Known statistical distribution'
Assert ($null -eq (Get-Statistics @())) 'Empty distribution is null'
Assert ($null -eq (Get-Statistics @(1)).StandardDeviation) 'One-sample standard deviation is null'
$config = Get-Content "$RepoRoot/Config/Benchmarks.json" -Raw | ConvertFrom-Json -AsHashtable
$definition = $config.Benchmarks | Where-Object Name -EQ MatrixMultiply
$parameters = @{Size=32;Work=1;Seed=1}
$value = @{Protocol=1;Benchmark='MatrixMultiply';Size=32;Work=1;Seed=1;Warmups=0;
           Samples=@(@{Seconds=0.001;Sum=-0.03125;Weighted=-43.5})}
Write-Json $value "$directory/Output.json"
Test-Output "$directory/Output.json" $definition $parameters 0 1 @{Sum=-0.03125;Weighted=-43.5} | Out-Null
$passed++
foreach ($bad in @('NaN',$true,$null)) {
    $value.Samples[0].Seconds = $bad
    Write-Json $value "$directory/Bad.json"
    Assert-Throws { Test-Output "$directory/Bad.json" $definition $parameters 0 1 } 'Malformed numeric samples rejected'
}
$value.Samples[0].Seconds=0.001; $value.Samples[0].Sum=100
Write-Json $value "$directory/Bad.json"
Assert-Throws { Test-Output "$directory/Bad.json" $definition $parameters 0 1 @{Sum=-0.03125;Weighted=-43.5} } 'Incorrect checksum rejected'
$manualPath = "$directory/Machine.json"
Write-Json @{Label='Test machine';Overrides=@{LogicalProcessors=123};Notes='Regression fixture'} $manualPath
$pwsh = (Get-Process -Id $PID).Path
$suiteDirectory = "$directory/Failure Suite"
$failureArguments = @('-NoProfile','-File',"$RepoRoot/Run.ps1",'verify','-Benchmark','PrimeSieve',
    '-Language','Cpp','-Cpp',$exe,'-Output',$suiteDirectory,'-Machine',$manualPath)
$failure = [WindowsProcess]::Run((Join-Command $pwsh $failureArguments), $RepoRoot, "$BuildRoot/Empty.txt",
    "$directory/Suite.log", "$directory/Suite.stderr", -1, 60)
Assert ($failure.ExitCode -ne 0 -and -not $failure.TimedOut) 'Failed compilation returns nonzero'
$failedReport = Get-Content "$suiteDirectory/Results.json" -Raw | ConvertFrom-Json -AsHashtable
Assert ($failedReport.Results[0].Status -eq 'Failed' -and $null -eq $failedReport.Results[0].ExecutableBytes -and
    $failedReport.Results[0].BuildSeconds.Count -eq 0) 'Failed compilation preserves an incomplete row without fake zero metrics'
Assert ($failedReport.Machine.Effective.LogicalProcessors -eq 123 -and
    $failedReport.Machine.Detected.LogicalProcessors -ne 123) 'Manual metadata retains detected provenance'
Assert ((Get-Content "$suiteDirectory/Results.json" -Raw) | Test-Json -SchemaFile "$RepoRoot/Config/Results.schema.json") 'Failure report conforms to schema'
$interruptedDirectory = "$directory/Interrupted Suite"
$start = [Diagnostics.ProcessStartInfo]::new($pwsh)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @('-NoProfile','-File',"$RepoRoot/Run.ps1",'verify','-Benchmark','PrimeSieve',
    '-Language','Cpp','-Cpp',$exe,'-Output',$interruptedDirectory)) { $start.ArgumentList.Add($argument) }
$start.Environment['BENCHMARK_TEST_SLOW_BUILD'] = '1'
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
try {
    $started = $false
    for ($i=0; $i -lt 200 -and -not $process.HasExited; $i++) {
        $buildLog = "$interruptedDirectory/PrimeSieve/Cpp/Prepare.Build.log"
        if ((Test-Path -LiteralPath $buildLog) -and (Get-Content -LiteralPath $buildLog -Raw) -match 'BuildStarted') {
            $started = $true; break
        }
        Start-Sleep -Milliseconds 100
    }
    Assert $started 'Interruption fixture reached compilation'
} finally {
    if (-not $process.HasExited) { $process.Kill() }
    $process.WaitForExit()
    [IO.File]::WriteAllText("$directory/Interrupted.log", $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult())
    $process.Dispose()
}
$partial = Get-Content "$interruptedDirectory/Results.json" -Raw | ConvertFrom-Json -AsHashtable
Assert ($partial.Status -eq 'Running' -and $partial.Results[0].Status -eq 'Building' -and
    $partial.Results[0].BuildSeconds.Count -eq 0 -and $null -eq $partial.Results[0].ExecutableBytes) 'Hard interruption retains the last durable checkpoint without fabricated samples'
Assert ((Get-Content "$interruptedDirectory/Results.json" -Raw) | Test-Json -SchemaFile "$RepoRoot/Config/Results.schema.json") 'Interrupted checkpoint conforms to schema'
if ($ReportDirectory) {
    $reportCopy = "$directory/Report"
    [IO.Directory]::CreateDirectory($reportCopy) | Out-Null
    Copy-Item "$ReportDirectory/Results.json" "$reportCopy/Results.json"
    $oldCulture = [Globalization.CultureInfo]::CurrentCulture
    try {
        [Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')
        Write-Reports $reportCopy
    } finally { [Globalization.CultureInfo]::CurrentCulture = $oldCulture }
    $csv = Import-Csv "$reportCopy/Summary.csv"
    Assert (@($csv).Count -gt 0) 'CSV retains rows'
    Assert (-not ($csv.BuildMedianSeconds | Where-Object { $_ -like '*,*' })) 'CSV uses invariant decimal points'
    $json = Get-Content "$reportCopy/Results.json" -Raw
    Assert ($json | Test-Json -SchemaFile "$RepoRoot/Config/Results.schema.json") 'Report conforms to result schema'
}
Write-Host "$passed runner assertions passed. Fixtures: $directory"

