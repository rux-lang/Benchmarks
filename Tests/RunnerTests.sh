#!/usr/bin/env bash
set -Eeuo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
BUILD="$ROOT/Build"
source "$ROOT/Scripts/Runner.sh"
TIMEOUT=10
CPP="${CXX:-clang++}"
directory="$BUILD/Tests With Spaces/$(date +%s)-$$"
mkdir -p "$directory"
: > "$BUILD/Empty.txt"
exe="$directory/Process Probe"
"$CPP" -std=c++2c -O2 "$ROOT/Tests/Fixtures/ProcessProbe.cpp" -o "$exe"
CPU=$(awk '/Cpus_allowed_list/ {print $2}' /proc/self/status)
CPU="${CPU%%,*}"; CPU="${CPU%%-*}"
run_child "$directory/Echo.txt" "$BUILD/Empty.txt" "$CPU" "$exe" echo "spaces ' quotes \" slash\\"
[[ $(cat "$directory/Echo.txt") == "spaces ' quotes \" slash\\" ]]
run_child "$directory/Affinity.txt" "$BUILD/Empty.txt" "$CPU" "$exe" affinity
[[ $(cat "$directory/Affinity.txt") == 1 ]]
run_child "$directory/Memory.txt" "$BUILD/Empty.txt" "$CPU" /usr/bin/time -f %M -o "$directory/RSS.txt" "$exe" memory
(( $(cat "$directory/RSS.txt") >= 65536 ))
if run_child "$directory/Fail.txt" "$BUILD/Empty.txt" "$CPU" "$exe" fail; then exit 1; fi
if need benchmark-nonexistent-tool-73aa; then exit 1; fi
if remove_generated "$ROOT"; then exit 1; fi
TIMEOUT=1
if run_child "$directory/Timeout.txt" "$BUILD/Empty.txt" "$CPU" "$exe" spawn; then exit 1; fi
child=$(cat "$directory/Timeout.txt")
# A reparented zombie is terminated; /proc may persist until its new parent reaps it.
if [[ -r /proc/$child/stat ]]; then [[ $(awk '{print $3}' "/proc/$child/stat") == Z ]]; fi
TIMEOUT=10
HYPERFINE="${HYPERFINE:-hyperfine}"
measure_command "$directory/Quoted" '' "$CPU" "$exe" echo "spaces ' quotes"
[[ $(cat "$directory/Quoted.Output.json") == "spaces ' quotes" ]]
jq -n '{Results:[{BuildSeconds:[4,1,3,2],ProcessSeconds:[],KernelSeconds:[1],PeakMemoryBytes:[]}]}' |
    jq -f "$ROOT/Scripts/Statistics.jq" |
    jq -e '.Results[0].Statistics | .Compilation.Median==2.5 and .Process==null and .Kernel.StandardDeviation==null' >/dev/null
definition=$(jq -c '.Benchmarks[]|select(.Name=="MatrixMultiply")' "$ROOT/Config/Benchmarks.json")
printf '%s\n' '{"Protocol":1,"Benchmark":"MatrixMultiply","Size":32,"Work":1,"Seed":1,"Warmups":0,"Samples":[{"Seconds":0.01,"Sum":-0.03125,"Weighted":-43.5}]}' > "$directory/Valid.json"
validate_output "$directory/Valid.json" MatrixMultiply 32 1 1 0 1 '{"Sum":-0.03125,"Weighted":-43.5}' "$definition"
for bad in '"NaN"' true null; do
    jq --argjson bad "$bad" '.Samples[0].Seconds=$bad' "$directory/Valid.json" > "$directory/Invalid.json"
    if validate_output "$directory/Invalid.json" MatrixMultiply 32 1 1 0 1 null "$definition"; then exit 1; fi
done
jq -n '{Label:"Test machine",Overrides:{MemoryBytes:123},Notes:"Regression fixture"}' > "$directory/Machine.json"
if bash "$ROOT/Run.sh" verify --benchmark PrimeSieve --language Cpp --cpp "$exe" \
    --machine "$directory/Machine.json" --output "$directory/Failure Suite" > "$directory/Failure.log" 2>&1; then exit 1; fi
jq -e '.Status=="Failed" and .Results[0].Status=="Failed" and .Results[0].ExecutableBytes==null
    and (.Results[0].BuildSeconds|length)==0 and .Machine.Effective.MemoryBytes==123
    and .Machine.Detected.MemoryBytes!=123' "$directory/Failure Suite/Results.json" >/dev/null
setsid env BENCHMARK_TEST_SLOW_BUILD=1 bash "$ROOT/Run.sh" verify --benchmark PrimeSieve --language Cpp \
    --cpp "$exe" --output "$directory/Interrupted Suite" > "$directory/Interrupted.log" 2>&1 &
suite_pid=$!
started=false
for ((i=0;i<200;i++)); do
    if [[ -f "$directory/Interrupted Suite/PrimeSieve/Cpp/Prepare.Build.log" ]] &&
        [[ $(cat "$directory/Interrupted Suite/PrimeSieve/Cpp/Prepare.Build.log") == *BuildStarted* ]]; then
        started=true; break
    fi
    kill -0 "$suite_pid" 2>/dev/null || break
    sleep 0.1
done
kill -TERM -- "-$suite_pid" 2>/dev/null || true
wait "$suite_pid" 2>/dev/null || true
[[ $started == true ]]
flock -n "$BUILD/Runner.lock" true
compiler_pid=$(awk '/BuildStarted/ {print $2}' "$directory/Interrupted Suite/PrimeSieve/Cpp/Prepare.Build.log")
if [[ -r /proc/$compiler_pid/stat ]]; then [[ $(awk '{print $3}' "/proc/$compiler_pid/stat") == Z ]]; fi
jq -e '(.Status=="Interrupted" or .Status=="Failed") and (.Results[0].Status=="Interrupted" or .Results[0].Status=="Failed")
    and (.Results[0].BuildSeconds|length)==0 and .Results[0].ExecutableBytes==null' \
    "$directory/Interrupted Suite/Results.json" >/dev/null
printf 'Linux runner assertions passed. Fixtures: %s\n' "$directory"

