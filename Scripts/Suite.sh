usage() {
    cat <<'HELP'
Usage: bash Run.sh doctor|prepare|verify|run|report [options]
  --benchmark NAME[,NAME]    Default: all five
  --language NAME[,NAME]     Rux,Cpp,Rust,CSharpAot,CSharpJit
  --profile Smoke|Standard|Large (default Standard)
  --build-runs N --run-runs N --memory-runs N --warmups N
  --cpu auto|N --timeout-seconds N --size N --work N --seed N
  --rux-root PATH --rux PATH --cpp PATH --rust PATH --dotnet PATH
  --hyperfine PATH --machine FILE --output DIRECTORY
prepare restores and builds; verify also checks references; run measures.
report only reads an existing --output directory and regenerates summaries.
HELP
}
save_row() {
    printf '%s\n' "$ROW" > "$ROW_FILE.tmp"
    mv -- "$ROW_FILE.tmp" "$ROW_FILE"
    save_results
}
save_results() {
    local -a rows=("$OUTPUT"/Rows/*.json)
    if [[ -f ${rows[0]} ]]; then
        jq -s --slurpfile header "$OUTPUT/Run.json" \
            '$header[0] + {UpdatedAt:(now|todateiso8601),Results:.}' "${rows[@]}" > "$OUTPUT/Results.json.tmp"
    else
        jq '. + {UpdatedAt:(now|todateiso8601),Results:[]}' "$OUTPUT/Run.json" > "$OUTPUT/Results.json.tmp"
    fi
    mv -- "$OUTPUT/Results.json.tmp" "$OUTPUT/Results.json"
}
finish_suite() {
    local status=$?
    trap - EXIT INT TERM
    if [[ -n ${OUTPUT:-} && -f $OUTPUT/Run.json ]]; then
        if [[ $(jq -r .Status "$OUTPUT/Run.json") == Running ]]; then
            jq '.Status="Interrupted"' "$OUTPUT/Run.json" > "$OUTPUT/Run.json.tmp"
            mv -- "$OUTPUT/Run.json.tmp" "$OUTPUT/Run.json"
            local f
            for f in "$OUTPUT"/Rows/*.json; do
                [[ -f $f ]] || continue
                jq 'if .Status=="Building" or .Status=="Measuring" then .Status="Interrupted" else . end' "$f" > "$f.tmp"
                mv -- "$f.tmp" "$f"
            done
        fi
        save_results
        write_reports "$OUTPUT"
        printf 'Results: %s\n' "$OUTPUT"
    fi
    exit "$status"
}
case_failure() {
    local status=$?
    trap - ERR
    # A failed command substitution may have emptied ROW; recover the last durable checkpoint.
    ROW=$(cat "$ROW_FILE")
    ROW=$(jq --arg error "Command failed (exit $status). See retained logs." '.Status="Failed" | .Error=$error' <<< "$ROW")
    save_row
    exit "$status"
}
execute_case() {
    local name=$1 lang=$2 definition=$3 size=$4 work=$5 index=$6
    local dir="$OUTPUT/$name/$lang" expected reference elapsed bytes managed i v
    mkdir -p -- "$dir"
    ROW_FILE="$OUTPUT/Rows/$(printf '%03d' "$index").json"
    expected=$(cat "$OUTPUT/$name/Expected.json")
    ROW=$(jq -n --arg name "$name" --arg lang "$lang" --argjson size "$size" --argjson work "$work" --argjson seed "$SEED" \
        --arg validation "$(cat "$OUTPUT/$name/Validation.txt")" \
        '{Benchmark:$name,Language:$lang,Parameters:{Size:$size,Work:$work,Seed:$seed},Status:"Building",Error:null,
          BuildKind:(if $lang=="CSharpJit" then "ManagedBuild" else "NativeCompileAndLink" end),BuildCommand:null,Dependencies:[],
          ExecutableBytes:null,DeployableBytes:null,ManagedPayloadBytes:null,
          RuntimeRequirement:(if $lang=="CSharpJit" then ".NET 10 x64 shared runtime" else "OS native libraries" end),
          MemoryMetric:"LinuxMaximumRSS",Validation:$validation,BuildSeconds:[],ProcessSeconds:[],KernelSeconds:[],
          PeakMemoryBytes:[],Checksums:[]}')
    save_row
    trap case_failure ERR
    printf '%s / %s : preparing and validating\n' "$name" "$lang"
    prepare_case "$name" "$lang" "$dir/Prepare"
    ROW=$(jq --arg file "$BUILD_FILE" --argjson args "$(jq -n '$ARGS.positional' --args -- "${BUILD_ARGS[@]}")" \
        '.BuildCommand={File:$file,Arguments:$args}' <<< "$ROW")
    run_child "$dir/Prepare.Build.log" "$BUILD/Empty.txt" -1 "$BUILD_FILE" "${BUILD_ARGS[@]}"
    if [[ $COMMAND != prepare ]]; then verify_case "$name" "$dir" "$definition"; fi
    if [[ $COMMAND == run ]]; then
        for ((i=0;i<BUILD_RUNS;i++)); do
            prepare_case "$name" "$lang" "$dir/Build$i"
            measure_command "$dir/Build$i" '' -1 "$BUILD_FILE" "${BUILD_ARGS[@]}"
            ROW=$(jq --argjson value "$ELAPSED" '.BuildSeconds+=[$value]' <<< "$ROW"); save_row
        done
    fi
    bytes=$(find "$DEPLOY" -type f ! -name '*.pdb' ! -name '*.dbg' ! -name '*.debug' ! -name '*.lib' ! -name '*.exp' ! -name '*.obj' \
        -printf '%s\n' | awk '{sum+=$1} END {printf "%.0f",sum}')
    managed=null
    if [[ $lang == CSharp* ]]; then
        ROW=$(jq --argjson dependencies "$(jq '.libraries|keys' "$STAGE/Obj/project.assets.json")" '.Dependencies=$dependencies' <<< "$ROW")
    fi
    [[ $lang != CSharpJit ]] || managed=$(stat -c %s "$DEPLOY/$name.dll")
    ROW=$(jq --argjson exe "$(stat -c %s "$EXE")" --argjson total "$bytes" --argjson managed "$managed" \
        '.ExecutableBytes=$exe | .DeployableBytes=$total | .ManagedPayloadBytes=$managed' <<< "$ROW")
    if [[ $COMMAND == run ]]; then
        ROW=$(jq '.Status="Measuring"' <<< "$ROW"); save_row
        write_input "$dir/Input.txt" "$size" "$work" "$SEED" 0 1
        for ((i=0;i<WARMUPS;i++)); do
            run_child "$dir/Warmup$i.json" "$dir/Input.txt" "$CPU" "$EXE"
            validate_output "$dir/Warmup$i.json" "$name" "$size" "$work" "$SEED" 0 1 "$expected" "$definition"
            [[ $expected != null ]] || expected=$(jq -c '.Samples[0]' "$dir/Warmup$i.json")
        done
        for ((i=0;i<RUN_RUNS;i++)); do
            measure_command "$dir/Process$i" "$dir/Input.txt" "$CPU" "$EXE"
            validate_output "$dir/Process$i.Output.json" "$name" "$size" "$work" "$SEED" 0 1 "$expected" "$definition"
            v=$(jq -c '.Samples[0]' "$dir/Process$i.Output.json")
            [[ $expected != null ]] || expected=$v
            ROW=$(jq --argjson elapsed "$ELAPSED" --argjson sample "$v" '.ProcessSeconds+=[$elapsed] | .Checksums+=[$sample]' <<< "$ROW")
            save_row
        done
        printf '%s\n' "$expected" > "$OUTPUT/$name/Expected.json"
        write_input "$dir/KernelInput.txt" "$size" "$work" "$SEED" "$WARMUPS" "$RUN_RUNS"
        run_child "$dir/Kernel.json" "$dir/KernelInput.txt" "$CPU" "$EXE"
        validate_output "$dir/Kernel.json" "$name" "$size" "$work" "$SEED" "$WARMUPS" "$RUN_RUNS" "$expected" "$definition"
        ROW=$(jq --argjson times "$(jq '[.Samples[].Seconds]' "$dir/Kernel.json")" '.KernelSeconds=$times' <<< "$ROW")
        save_row
        for ((i=0;i<MEMORY_RUNS;i++)); do
            run_child "$dir/Memory$i.json" "$dir/Input.txt" "$CPU" /usr/bin/time -f %M -o "$dir/Memory$i.RSS.txt" "$EXE"
            validate_output "$dir/Memory$i.json" "$name" "$size" "$work" "$SEED" 0 1 "$expected" "$definition"
            bytes=$(cat "$dir/Memory$i.RSS.txt")
            [[ $bytes =~ ^[0-9]+$ ]]
            ROW=$(jq --argjson kb "$bytes" '.PeakMemoryBytes+=[($kb*1024)]' <<< "$ROW")
            save_row
        done
    fi
    local status=Prepared
    [[ $COMMAND != verify ]] || status=Verified
    [[ $COMMAND != run ]] || status=Completed
    ROW=$(jq --arg status "$status" '.Status=$status' <<< "$ROW"); save_row
    trap - ERR
}
main() {
    COMMAND=doctor; PROFILE=Standard; BENCHMARKS=''; LANGUAGES=''; CPU=auto; SEED=1; SIZE=0; WORK=0
    BUILD_RUNS=5; RUN_RUNS=10; MEMORY_RUNS=3; WARMUPS=2; TIMEOUT=1800
    RUX_ROOT=''; RUX=rux; CPP=clang++; RUST=rustc; DOTNET=dotnet; HYPERFINE=hyperfine; MACHINE=''; OUTPUT=''
    if (( $# > 0 )) && [[ $1 != --* ]]; then COMMAND=$1; shift; fi
    while (( $# > 0 )); do
        [[ $1 != --help ]] || { usage; return; }
        (( $# >= 2 )) || { die "Missing value for $1"; return 1; }
        case $1 in
            --benchmark) BENCHMARKS=$2;; --language) LANGUAGES=$2;; --profile) PROFILE=$2;;
            --build-runs) BUILD_RUNS=$2;; --run-runs) RUN_RUNS=$2;; --memory-runs) MEMORY_RUNS=$2;;
            --warmups) WARMUPS=$2;; --timeout-seconds) TIMEOUT=$2;; --cpu) CPU=$2;; --seed) SEED=$2;;
            --size) SIZE=$2;; --work) WORK=$2;; --rux-root) RUX_ROOT=$2;; --rux) RUX=$2;;
            --cpp) CPP=$2;; --rust) RUST=$2;; --dotnet) DOTNET=$2;; --hyperfine) HYPERFINE=$2;;
            --machine) MACHINE=$2;; --output) OUTPUT=$2;; *) die "Unknown option $1"; return 1;;
        esac
        shift 2
    done
    [[ $COMMAND =~ ^(doctor|prepare|verify|run|report)$ ]] || { usage; return 1; }
    need jq
    if [[ $COMMAND == report ]]; then
        [[ -n $OUTPUT ]] || { die 'report requires --output.'; return 1; }
        write_reports "$OUTPUT"; return
    fi
    [[ $PROFILE =~ ^(Smoke|Standard|Large)$ ]] || { die 'Invalid profile'; return 1; }
    local n spec variable minimum maximum value
    for spec in BUILD_RUNS:1:1000 RUN_RUNS:1:1000 MEMORY_RUNS:1:1000 WARMUPS:0:100 TIMEOUT:1:86400 SEED:0:1000000 SIZE:0:200000000 WORK:0:1000000; do
        IFS=: read -r variable minimum maximum <<< "$spec"; value="${!variable}"
        [[ $value =~ ^(0|[1-9][0-9]{0,8})$ ]] && (( value>=minimum && value<=maximum )) || { die "Invalid $variable"; return 1; }
    done
    [[ -n $LANGUAGES ]] || LANGUAGES=$(jq -r '.Languages|join(",")' "$ROOT/Config/Benchmarks.json")
    [[ -n $BENCHMARKS ]] || BENCHMARKS=$(jq -r '[.Benchmarks[].Name]|join(",")' "$ROOT/Config/Benchmarks.json")
    local -a names languages
    IFS=, read -r -a names <<< "$BENCHMARKS"; IFS=, read -r -a languages <<< "$LANGUAGES"
    for n in "${languages[@]}"; do jq -e --arg n "$n" '.Languages|index($n)!=null' "$ROOT/Config/Benchmarks.json" >/dev/null || { die "Unknown language $n"; return 1; }; done
    for n in "${names[@]}"; do jq -e --arg n "$n" '[.Benchmarks[].Name]|index($n)!=null' "$ROOT/Config/Benchmarks.json" >/dev/null || { die "Unknown benchmark $n"; return 1; }; done
    [[ $(printf '%s\n' "${names[@]}" | sort -u | wc -l) -eq ${#names[@]} && $(printf '%s\n' "${languages[@]}" | sort -u | wc -l) -eq ${#languages[@]} ]] || { die 'Duplicate selections'; return 1; }
    (( (SIZE==0 && WORK==0) || ${#names[@]}==1 )) || { die 'Custom size/work requires one benchmark.'; return 1; }
    mkdir -p -- "$BUILD"
    exec 9>"$BUILD/Runner.lock"; flock -n 9 || { die 'Another runner owns this build directory.'; return 1; }
    set_environment
    initialize_tools
    if [[ $COMMAND == doctor ]]; then printf '%s\n' "$VERSIONS" | jq .; printf 'Runtime CPU: %s\n' "$CPU"; return; fi
    stage_rux_packages
    [[ -n $OUTPUT ]] || OUTPUT="$ROOT/Results/$(date -u +%Y%m%dT%H%M%SZ)-Linux"
    OUTPUT=$(realpath -m -- "$OUTPUT")
    case "$OUTPUT/" in
        "$BUILD/Artifacts/"*|"$BUILD/Projects/"*|"$BUILD/RuxPackages/"*|"$BUILD/Tools/"*|"$BUILD/NuGet/"*|"$BUILD/DotnetHome/"*)
            die 'Results must not be placed inside a generated tool or build directory.'; return 1;;
    esac
    [[ ! -e $OUTPUT/Results.json ]] || { die "Results already exist: $OUTPUT"; return 1; }
    mkdir -p -- "$OUTPUT/Rows"
    : > "$OUTPUT/SourceHashes.jsonl"
    local file relative hash
    while IFS= read -r -d '' file; do
        relative="${file#"$ROOT/"}"; hash=$(sha256sum -- "$file"); hash="${hash%% *}"
        jq -n --arg path "$relative" --arg hash "$hash" '{($path):$hash}' >> "$OUTPUT/SourceHashes.jsonl"
    done < <(find "$ROOT/Benchmarks" "$ROOT/Shared" "$ROOT/Config" "$ROOT/Scripts" "$ROOT/Tests" \
        -type d \( -name obj -o -name bin -o -name target \) -prune -o -type f -print0; printf '%s\0' "$ROOT/Run.ps1" "$ROOT/Run.sh" "$ROOT/global.json")
    printf '%s\n' "$PACKAGE_HASHES" > "$OUTPUT/RuxPackageHashes.json"
    machine_metadata > "$OUTPUT/Machine.json"
    printf '%s\n' "$VERSIONS" > "$OUTPUT/Toolchains.json"
    jq -s 'add // {}' "$OUTPUT/SourceHashes.jsonl" > "$OUTPUT/SourceHashes.json"
    jq -n --arg run "$(basename -- "$OUTPUT")" --arg profile "$PROFILE" --arg command "$COMMAND" \
        --argjson builds "$BUILD_RUNS" --argjson runs "$RUN_RUNS" --argjson memories "$MEMORY_RUNS" \
        --argjson warmups "$WARMUPS" --argjson cpu "$CPU" --argjson timeout "$TIMEOUT" \
        --slurpfile machine "$OUTPUT/Machine.json" --slurpfile tools "$OUTPUT/Toolchains.json" \
        --slurpfile hashes "$OUTPUT/SourceHashes.json" --slurpfile packages "$OUTPUT/RuxPackageHashes.json" \
        '{SchemaVersion:1,RunId:$run,StartedAt:(now|todateiso8601),UpdatedAt:null,Platform:"Linux",Architecture:"x86-64",
          Profile:$profile,Command:$command,Status:"Running",Settings:{BuildRuns:$builds,RunRuns:$runs,MemoryRuns:$memories,
          Warmups:$warmups,Cpu:$cpu,TimeoutSeconds:$timeout,CpuPolicy:"PinnedRunsSerialBuilds",Isa:"x86-64-baseline",
          JitPolicy:"NoTieringNoConcurrentGC",FilesystemCache:"Warm"},Machine:$machine[0],Toolchains:$tools[0],
          SourceHashes:$hashes[0],RuxPackageHashes:$packages[0],Results:[]}' > "$OUTPUT/Run.json"
    trap finish_suite EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    save_results
    local name lang definition size work expected index=0 status failed=0
    for name in "${names[@]}"; do
        definition=$(jq -c --arg name "$name" '.Benchmarks[]|select(.Name==$name)' "$ROOT/Config/Benchmarks.json")
        size=$SIZE; work=$WORK
        (( size!=0 )) || size=$(jq -r --arg profile "$PROFILE" '.Profiles[$profile].Size' <<< "$definition")
        (( work!=0 )) || work=$(jq -r --arg profile "$PROFILE" '.Profiles[$profile].Work' <<< "$definition")
        (( size>=2 && size<=$(jq -r .MaxSize <<< "$definition") )) || { die "Invalid size: $name"; return 1; }
        [[ $(jq -r .PowerOfTwo <<< "$definition") != true ]] || (( (size & (size-1))==0 )) || { die 'FFT size must be power of two'; return 1; }
        mkdir -p -- "$OUTPUT/$name"
        expected=$(jq -c --arg name "$name" --argjson size "$size" --argjson work "$work" --argjson seed "$SEED" \
            '[.Cases[]|select(.Benchmark==$name and .Size==$size and .Work==$work and .Seed==$seed)][0] // null' "$ROOT/Tests/ReferenceCases.json")
        printf '%s\n' "$expected" > "$OUTPUT/$name/Expected.json"
        if [[ $expected == null ]]; then printf 'ReferenceCasesAndCrossLanguage' > "$OUTPUT/$name/Validation.txt"
        else printf 'IndependentReference' > "$OUTPUT/$name/Validation.txt"; fi
        for lang in "${languages[@]}"; do
            index=$((index+1))
            # A fresh subshell retains errexit; an if/function call would suppress it inside the entire function.
            set +e
            ( set -Ee; trap - EXIT; execute_case "$name" "$lang" "$definition" "$size" "$work" "$index" )
            status=$?
            set -e
            (( status==0 )) || failed=1
        done
    done
    status=Completed; (( failed==0 )) || status=Failed
    jq --arg status "$status" '.Status=$status' "$OUTPUT/Run.json" > "$OUTPUT/Run.json.tmp"
    mv -- "$OUTPUT/Run.json.tmp" "$OUTPUT/Run.json"
    return "$failed"
}

