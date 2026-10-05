die() { printf '%s\n' "$*" >&2; return 1; }
need() { command -v -- "$1" >/dev/null 2>&1 || die "Missing tool '$1'. See Docs/Ubuntu.md."; }
remove_generated() {
    local path=$1 resolved
    resolved=$(realpath -m -- "$path")
    [[ $resolved == "$BUILD/"* && $resolved != "$BUILD/" ]] || { die "Cleanup outside Build refused: $path"; return 1; }
    if [[ -e $path ]]; then
        [[ -z $(find "$path" -type l -print -quit) ]] || { die "Cleanup through a symlink refused: $path"; return 1; }
        rm -rf -- "$path"
    fi
}
quote_command() {
    local result='' arg
    for arg in "$@"; do
        # Hyperfine shell=none parses shell words on Unix without executing a shell.
        arg="${arg//\'/\'\\\'\'}"
        result+="'$arg' "
    done
    printf '%s' "$result"
}
run_child() (
    # A scoped subshell forwards interruption to GNU timeout, which owns the child group.
    trap - EXIT ERR
    local child=''
    stop_child() {
        local code=$1
        trap - INT TERM
        if [[ -n $child ]]; then kill -TERM "$child" 2>/dev/null || true; wait "$child" 2>/dev/null || true; fi
        exit "$code"
    }
    trap 'stop_child 130' INT
    trap 'stop_child 143' TERM
    local log=$1 input=$2 pinned=$3; shift 3
    mkdir -p -- "$(dirname -- "$log")"
    local -a prefix=(timeout --signal=TERM --kill-after=5 "$TIMEOUT")
    [[ $pinned == -1 ]] || prefix+=(taskset -c "$pinned")
    local status=0
    "${prefix[@]}" "$@" < "$input" > "$log" 2> "$log.stderr" 9>&- &
    child=$!
    wait "$child" || status=$?
    child=''
    if (( status != 0 )); then
        printf 'Exit %s: %s (log: %s)\n' "$status" "$1" "$log" >&2
        cat -- "$log.stderr" >&2
        return "$status"
    fi
)
initialize_tools() {
    [[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || { die 'Native Linux x86-64 is required.'; return 1; }
    for tool in jq timeout taskset lscpu flock realpath sha256sum; do need "$tool" || return; done
    need "$HYPERFINE" || return
    [[ -x /usr/bin/time ]] || { die 'Install GNU time (/usr/bin/time).'; return 1; }
    /usr/bin/time --version | head -n 1 | grep -q GNU || { die '/usr/bin/time must be GNU time.'; return 1; }
    mkdir -p -- "$BUILD/Doctor"
    : > "$BUILD/Empty.txt"
    local allowed first
    allowed=$(awk '/Cpus_allowed_list/ {print $2}' /proc/self/status)
    first="${allowed%%,*}"; first="${first%%-*}"
    [[ $CPU != auto ]] || CPU=$first
    [[ $CPU =~ ^[0-9]+$ ]] || { die 'CPU must be auto or a logical CPU number.'; return 1; }
    taskset -c "$CPU" true || return
    VERSIONS='{}'
    local key tool version hash
    for key in Hyperfine Rux Cpp Rust Dotnet; do
        case $key in
            Hyperfine) tool=$HYPERFINE ;;
            Rux) [[ ,$LANGUAGES, == *,Rux,* ]] || continue; tool=$RUX ;;
            Cpp) [[ ,$LANGUAGES, == *,Cpp,* ]] || continue; tool=$CPP ;;
            Rust) [[ ,$LANGUAGES, == *,Rust,* ]] || continue; tool=$RUST ;;
            Dotnet) [[ $LANGUAGES == *CSharp* ]] || continue; tool=$DOTNET ;;
        esac
        need "$tool" || return
        tool=$(command -v -- "$tool")
        case $key in
            Hyperfine) HYPERFINE=$tool;; Rux) RUX=$tool;; Cpp) CPP=$tool;; Rust) RUST=$tool;; Dotnet) DOTNET=$tool;;
        esac
        run_child "$BUILD/Doctor/$key.txt" "$BUILD/Empty.txt" -1 "$tool" --version || return
        version=$(cat "$BUILD/Doctor/$key.txt")
        hash=$(sha256sum -- "$tool"); hash="${hash%% *}"
        VERSIONS=$(jq --arg key "$key" --arg path "$tool" --arg version "$version" --arg hash "$hash" \
            '.[$key]={Path:$path,Version:$version,Sha256:$hash}' <<< "$VERSIONS")
    done
    if [[ $LANGUAGES == *CSharp* ]]; then
        export DOTNET_ROOT="$(dirname -- "$(readlink -f -- "$DOTNET")")"
        export DOTNET_ROOT_X64="$DOTNET_ROOT"
        run_child "$BUILD/Doctor/Dotnet.details" "$BUILD/Empty.txt" -1 "$DOTNET" --info || return
        VERSIONS=$(jq --rawfile details "$BUILD/Doctor/Dotnet.details" '.Dotnet.Details=$details' <<< "$VERSIONS")
    fi
    if [[ ,$LANGUAGES, == *,Rust,* ]]; then
        run_child "$BUILD/Doctor/Rust.details" "$BUILD/Empty.txt" -1 "$RUST" -vV || return
        VERSIONS=$(jq --rawfile details "$BUILD/Doctor/Rust.details" '.Rust.Details=$details' <<< "$VERSIONS")
    fi
    if [[ ,$LANGUAGES, == *,Rux,* ]]; then
        [[ -n $RUX_ROOT ]] || RUX_ROOT="$(dirname -- "$ROOT")/Rux"
        [[ -f $RUX_ROOT/Packages/Core/Rux.toml ]] || { die 'Supply --rux-root with a matching Rux checkout.'; return 1; }
        RUX_ROOT=$(realpath -- "$RUX_ROOT")
    fi
    ALLOWED_CPUS=$allowed
}
set_environment() {
    export LC_ALL=C
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_NOLOGO=1
    export DOTNET_CLI_HOME="$BUILD/DotnetHome" NUGET_PACKAGES="$BUILD/NuGet" MSBUILDDISABLENODEREUSE=1
    export CARGO_INCREMENTAL=0 RUSTC_WRAPPER='' RUSTC_WORKSPACE_WRAPPER=''
    export DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 DOTNET_gcServer=0 DOTNET_gcConcurrent=0
    export DOTNET_EnableHWIntrinsic=0 DOTNET_EnableAVX=0 DOTNET_EnableFMA=0 DOTNET_EnableSSE3=0
}
stage_rux_packages() {
    PACKAGE_HASHES='{}'
    [[ ,$LANGUAGES, == *,Rux,* ]] || return 0
    local target="$BUILD/RuxPackages" name source file relative hash line dep
    remove_generated "$target" || return
    mkdir -p -- "$target"
    local -a pending=(Core Allocator Collections Io Math Time)
    local -A copied=()
    local index=0
    : > "$BUILD/PackageHashes.jsonl"
    while (( index < ${#pending[@]} )); do
        name="${pending[index]}"; index=$((index+1))
        [[ -z ${copied[$name]:-} ]] || continue
        copied[$name]=1; source="$RUX_ROOT/Packages/$name"
        [[ -f $source/Rux.toml ]] || { die "Missing Rux package: $source"; return 1; }
        cp -R -- "$source" "$target/$name"
        while IFS= read -r -d '' file; do
            relative="${file#"$RUX_ROOT/"}"; hash=$(sha256sum -- "$file"); hash="${hash%% *}"
            jq -n --arg path "$relative" --arg hash "$hash" '{($path):$hash}' >> "$BUILD/PackageHashes.jsonl"
        done < <(find "$source" -type f -print0 | sort -z)
        while IFS= read -r line; do
            if [[ $line =~ ^([A-Za-z_][A-Za-z_0-9]*)\ =\ \{\ Namespace\ =\ \"Rux\",\ Version\ =\ \"[^\"]+\"(.*)\ \} ]]; then
                dep="${BASH_REMATCH[1]}"; pending+=("$dep")
                printf '%s = { Path = "../%s"%s }\n' "$dep" "$dep" "${BASH_REMATCH[2]}"
            else printf '%s\n' "$line"
            fi
        done < "$source/Rux.toml" > "$target/$name/Rux.toml"
    done
    PACKAGE_HASHES=$(jq -s 'add // {}' "$BUILD/PackageHashes.jsonl")
}
prepare_case() {
    local name=$1 lang=$2 log=$3
    STAGE="$BUILD/Artifacts/$name/$lang"
    remove_generated "$STAGE" || return
    mkdir -p -- "$STAGE"
    local source="$ROOT/Benchmarks/$name" project aot
    EXE="$STAGE/$name"; DEPLOY=$STAGE
    case $lang in
        Cpp)
            BUILD_FILE=$CPP
            BUILD_ARGS=(-std=c++2c -O3 -DNDEBUG -g0 -march=x86-64 -mtune=generic
                -fno-fast-math -ffp-contract=off "$source/Cpp/Main.cpp" -o "$EXE") ;;
        Rust)
            BUILD_FILE=$RUST
            BUILD_ARGS=(--edition=2024 -C opt-level=3 -C target-cpu=x86-64 -C debuginfo=0 -C strip=symbols
                "$source/Rust/src/main.rs" -o "$EXE") ;;
        Rux)
            project="$BUILD/Projects/$name/Rux"
            remove_generated "$project" || return
            mkdir -p -- "$(dirname -- "$project")"
            cp -R -- "$source/Rux" "$project"
            cp -- "$ROOT/Shared/Rux/Benchmark.rux" "$project/Src/Benchmark.rux"
            BUILD_FILE=$RUX; BUILD_ARGS=(--manifest "$project/Rux.toml" --color=never build --release --quiet)
            DEPLOY="$project/Bin/Release/Linux/x86-64"; EXE="$DEPLOY/$name" ;;
        CSharpAot|CSharpJit)
            aot=false; [[ $lang != CSharpAot ]] || aot=true
            local -a properties=("-p:BaseIntermediateOutputPath=$STAGE/Obj/"
                "-p:MSBuildProjectExtensionsPath=$STAGE/Obj/" "-p:BaseOutputPath=$STAGE/Bin/"
                "-p:PublishAot=$aot" "-p:SelfContained=$aot")
            project="$source/CSharp/$name.csproj"
            run_child "$log.Restore.log" "$BUILD/Empty.txt" -1 "$DOTNET" restore "$project" -r linux-x64 \
                --disable-build-servers "${properties[@]}" || return
            BUILD_FILE=$DOTNET; DEPLOY="$STAGE/Publish"; EXE="$DEPLOY/$name"
            BUILD_ARGS=(publish "$project" -c Release -r linux-x64 --no-restore --disable-build-servers
                -o "$DEPLOY" "${properties[@]}") ;;
        *) die "No adapter for $lang"; return 1 ;;
    esac
}
write_input() { printf '1 %s %s %s %s %s\n' "$2" "$3" "$4" "$5" "$6" > "$1"; }
validate_output() {
    local path=$1 name=$2 size=$3 work=$4 seed=$5 warm=$6 samples=$7 expected=$8 definition=$9
    jq -e --arg name "$name" --argjson size "$size" --argjson work "$work" --argjson seed "$seed" \
        --argjson warm "$warm" --argjson samples "$samples" --argjson expected "$expected" --argjson def "$definition" \
        'def numeric: type=="number" and isfinite;
         ($expected // .Samples[0]) as $e |
         .Protocol==1 and .Benchmark==$name and .Size==$size and .Work==$work and .Seed==$seed
         and .Warmups==$warm and (.Samples|type)=="array" and (.Samples|length)==$samples
         and all(.Samples[];
            (.Seconds|numeric) and .Seconds>=0 and (.Sum|numeric) and (.Weighted|numeric)
            and ((.Sum-$e.Sum)|fabs) <= (if $def.Integer then 0 else $def.AbsoluteTolerance+$def.RelativeTolerance*($e.Sum|fabs) end)
            and ((.Weighted-$e.Weighted)|fabs) <= (if $def.Integer then 0 else $def.AbsoluteTolerance+$def.RelativeTolerance*($e.Weighted|fabs) end))' \
        "$path" >/dev/null || { die "Invalid result or checksum: $path"; return 1; }
}
verify_case() {
    local name=$1 dir=$2 definition=$3 reference size work seed index=0 status
    while IFS= read -r reference; do
        size=$(jq -r .Size <<< "$reference"); work=$(jq -r .Work <<< "$reference"); seed=$(jq -r .Seed <<< "$reference")
        write_input "$dir/Verify$index.txt" "$size" "$work" "$seed" 0 1
        run_child "$dir/Verify$index.json" "$dir/Verify$index.txt" "$CPU" "$EXE" || return
        validate_output "$dir/Verify$index.json" "$name" "$size" "$work" "$seed" 0 1 "$reference" "$definition" || return
        index=$((index+1))
    done < <(jq -c --arg name "$name" '.Cases[] | select(.Benchmark==$name)' "$ROOT/Tests/ReferenceCases.json")
    printf '1 0 1 1 0 1\n' > "$dir/Invalid.txt"
    status=0
    run_child "$dir/Invalid.json" "$dir/Invalid.txt" "$CPU" "$EXE" 2>/dev/null || status=$?
    (( status != 0 && status != 124 && status != 137 )) || { die "Invalid input not rejected: $EXE"; return 1; }
}
measure_command() {
    local prefix=$1 input=$2 pinned=$3; shift 3
    local -a hf=(--shell=none --runs 1 --style none --export-json "$prefix.Timing.json" --output "$prefix.Output.json")
    [[ -z $input ]] || hf+=(--input "$input")
    hf+=(-- "$(quote_command "$@")")
    run_child "$prefix.Hyperfine.log" "$BUILD/Empty.txt" "$pinned" "$HYPERFINE" "${hf[@]}" || return
    ELAPSED=$(jq -er '.results | select(length==1) | .[0].times | select(length==1) | .[0] | select(type=="number" and isfinite and .>=0)' "$prefix.Timing.json") || return
}
machine_metadata() {
    local cpu os memory power manual='{}' features
    local storage='null' filesystem='null'
    storage=$(lsblk --json --bytes -o NAME,MODEL,SIZE,ROTA,TRAN,TYPE 2>/dev/null) || storage='null'
    filesystem=$(findmnt --json -T "$ROOT" -o TARGET,FSTYPE,SOURCE 2>/dev/null) || filesystem='null'
    cpu=$(lscpu -J)
    os=$(cat /etc/os-release)
    memory=$(awk '/MemTotal:/ {printf "%.0f", $2*1024}' /proc/meminfo)
    power=$(cat /sys/devices/system/cpu/cpu"$CPU"/cpufreq/scaling_governor 2>/dev/null || true)
    features=$(awk -F: '/^flags/ {print $2; exit}' /proc/cpuinfo)
    [[ -z $MACHINE ]] || manual=$(jq -e . "$MACHINE")
    jq -n --argjson cpu "$cpu" --arg os "$os" --arg kernel "$(uname -r)" --argjson memory "$memory" \
        --arg power "$power" --arg features "$features" --arg allowed "$ALLOWED_CPUS" --argjson selected "$CPU" --argjson manual "$manual" \
        --argjson storage "$storage" --argjson filesystem "$filesystem" \
        '{Detected:{Cpu:$cpu,OS:$os,Kernel:$kernel,Architecture:"x86-64",MemoryBytes:$memory,
          PowerPolicy:(if $power=="" then null else $power end),CpuFeatures:($features|split(" ")|map(select(length>0))),
          AllowedCpus:$allowed,SelectedCpu:$selected,Storage:$storage,Filesystem:$filesystem},Manual:$manual,Warnings:[]} |
          .Effective=(.Detected + ($manual.Overrides // {}))'
}
write_reports() {
    local directory=$1
    jq -f "$ROOT/Scripts/Statistics.jq" "$directory/Results.json" > "$directory/Results.json.tmp"
    mv -- "$directory/Results.json.tmp" "$directory/Results.json"
    jq -r '["Benchmark","Language","Profile","Status","Size","Work","Seed","BuildMedianSeconds","ProcessMedianSeconds","KernelMedianSeconds",
            "PeakMemoryMedianBytes","ExecutableBytes","DeployableBytes","MemoryMetric","RuntimeRequirement"],
       (.Profile as $profile | .Results[] | [.Benchmark,.Language,$profile,.Status,.Parameters.Size,.Parameters.Work,.Parameters.Seed,
        .Statistics.Compilation.Median,.Statistics.Process.Median,.Statistics.Kernel.Median,.Statistics.Memory.Median,
        .ExecutableBytes,.DeployableBytes,.MemoryMetric,.RuntimeRequirement]) | @csv' "$directory/Results.json" > "$directory/Summary.csv"
    jq -r -f "$ROOT/Scripts/Report.jq" "$directory/Results.json" > "$directory/Report.md"
}

