#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('doctor','prepare','verify','run','report')][string]$Command = 'doctor',
    [string[]]$Benchmark = @(), [string[]]$Language = @(),
    [ValidateSet('Smoke','Standard','Large')][string]$Profile = 'Standard',
    [ValidateRange(1,1000)][int]$BuildRuns = 5, [ValidateRange(1,1000)][int]$RunRuns = 10,
    [ValidateRange(1,1000)][int]$MemoryRuns = 3, [ValidateRange(0,100)][int]$Warmups = 2,
    [ValidateRange(1,86400)][int]$TimeoutSeconds = 1800,
    [ValidateRange(-1,63)][int]$Cpu = -1,
    [ValidateRange(0,1000000)][int]$Seed = 1,
    [ValidateRange(0,200000000)][int]$Size = 0, [ValidateRange(0,1000000)][int]$Work = 0,
    [string]$RuxRoot = '', [string]$Rux = 'rux', [string]$Cpp = 'clang++',
    [string]$Rust = 'rustc', [string]$Dotnet = 'dotnet', [string]$Hyperfine = 'hyperfine',
    [string]$Machine = '', [string]$Output = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Command = $Command.ToLowerInvariant()
$Profile = [Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($Profile.ToLowerInvariant())
if (-not $IsWindows) { throw 'Use bash Run.sh on Linux. This runner uses native Windows measurements.' }
$RepoRoot = $PSScriptRoot
$BuildRoot = Join-Path $RepoRoot 'Build'
. (Join-Path $RepoRoot 'Scripts/Runner.ps1')
. (Join-Path $RepoRoot 'Scripts/Report.ps1')
. (Join-Path $RepoRoot 'Scripts/Suite.ps1')
$savedEnvironment = @{}
foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) { $savedEnvironment[$entry.Key] = $entry.Value }
$runnerLock = $null
try {
    if ($Command -ne 'report') {
        [IO.Directory]::CreateDirectory($BuildRoot) | Out-Null
        $runnerLock = [IO.File]::Open((Join-Path $BuildRoot 'Runner.lock'), [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    }
    Invoke-Suite
}
catch { Write-Error ($_.ToString() + [Environment]::NewLine + $_.ScriptStackTrace) -ErrorAction Continue; exit 1 }
finally {
    if ($runnerLock) { $runnerLock.Dispose() }
    foreach ($name in @([Environment]::GetEnvironmentVariables().Keys)) {
        if (-not $savedEnvironment.ContainsKey($name)) { [Environment]::SetEnvironmentVariable($name, $null) }
    }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}

