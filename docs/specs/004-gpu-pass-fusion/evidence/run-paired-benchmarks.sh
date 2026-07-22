#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 <baseline-worktree> <feature-worktree> <empty-output-directory>" >&2
    echo "Both worktrees must be clean. The feature worktree supplies both the feature benchmark and the external starting-SHA harness." >&2
}

if [[ $# -ne 3 ]]; then
    usage
    exit 64
fi

resolve_existing_directory() {
    local path="$1"
    if [[ ! -d "$path" ]]; then
        echo "Directory does not exist: $path" >&2
        exit 66
    fi
    (cd "$path" && pwd -P)
}

sha256_file() {
    local path="$1"
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$path" | awk '{print $1}'
    elif command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$path" | awk '{print $1}'
    else
        echo "Neither shasum nor sha256sum is available." >&2
        exit 69
    fi
}

require_clean_worktree() {
    local path="$1"
    local label="$2"
    git -C "$path" rev-parse --is-inside-work-tree >/dev/null
    local status
    status="$(git -C "$path" status --short)"
    if [[ -n "$status" ]]; then
        echo "$label worktree is not clean: $path" >&2
        echo "$status" >&2
        exit 65
    fi
}

find_full_result() {
    local artifacts="$1"
    local -a files=()
    while IFS= read -r path; do
        files+=("$path")
    done < <(find "$artifacts" -type f -iname '*full*.json' -print | sort)
    if [[ ${#files[@]} -ne 1 ]]; then
        echo "Expected exactly one BenchmarkDotNet full JSON result under $artifacts; found ${#files[@]}." >&2
        exit 65
    fi
    printf '%s\n' "${files[0]}"
}

run_benchmark() {
    local label="$1"
    local worktree="$2"
    local command="$3"
    local run_root="$4"
    local artifacts="$run_root/bdn"
    local counters="$run_root/counters"
    local stdout="$run_root/raw-benchmark-stdout.txt"

    mkdir -p "$artifacts" "$counters"
    printf '%s\n' "$command" >"$run_root/command.txt"
    git -C "$worktree" rev-parse HEAD >"$run_root/code-sha.txt"
    echo "Running $label benchmark at $(git -C "$worktree" rev-parse HEAD)."
    (
        cd "$worktree"
        BEUTL_RENDER_BENCHMARK_ARTIFACTS="$artifacts" \
        BEUTL_RENDER_BENCHMARK_COUNTERS="$counters" \
        BEUTL_REQUIRE_GPU=1 \
        bash -lc "$command"
    ) 2>&1 | tee "$stdout"

    local result
    result="$(find_full_result "$artifacts")"
    printf '%s\n' "$result" >"$run_root/full-result-path.txt"
    local counter_count
    counter_count="$(find "$counters" -maxdepth 1 -type f -name '*.json' | wc -l | tr -d ' ')"
    if [[ "$counter_count" != "11" ]]; then
        echo "$label benchmark produced $counter_count counter files; all 11 are required." >&2
        exit 65
    fi
}

baseline_worktree="$(resolve_existing_directory "$1")"
feature_worktree="$(resolve_existing_directory "$2")"
require_clean_worktree "$baseline_worktree" "Baseline"
require_clean_worktree "$feature_worktree" "Feature"

mkdir -p "$3"
output_root="$(resolve_existing_directory "$3")"
if [[ -n "$(find "$output_root" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
    echo "Output directory must be empty: $output_root" >&2
    exit 65
fi

temporary_build_root="$(mktemp -d "${TMPDIR:-/tmp}/beutl-gpu-pass-benchmark-build.XXXXXX")"
trap 'rm -rf -- "$temporary_build_root"' EXIT
baseline_harness="$feature_worktree/docs/specs/004-gpu-pass-fusion/evidence/target-benchmark-harness"
baseline_harness_project="$baseline_harness/Beutl.GpuPassTargetBenchmarkHarness.csproj"
baseline_engine_project="$baseline_worktree/src/Beutl.Engine/Beutl.Engine.csproj"
[[ -f "$baseline_harness_project" ]] || {
    echo "External starting-SHA benchmark harness is missing: $baseline_harness_project" >&2
    exit 66
}
[[ -f "$baseline_engine_project" ]] || {
    echo "Starting-SHA Engine project is missing: $baseline_engine_project" >&2
    exit 66
}
printf -v quoted_baseline_engine '%q' "$baseline_engine_project"
printf -v quoted_baseline_harness_project '%q' "$baseline_harness_project"
printf -v quoted_baseline_build '%q' "$temporary_build_root/baseline"
printf -v quoted_feature_build '%q' "$temporary_build_root/feature"
default_baseline_command="BEUTL_BASELINE_ENGINE_PROJECT=$quoted_baseline_engine dotnet run -c Release --artifacts-path $quoted_baseline_build --project $quoted_baseline_harness_project -p:BaselineEngineProject=$quoted_baseline_engine -- --filter '*TargetRenderPipelineBenchmarks*'"
default_feature_command="dotnet run -c Release --artifacts-path $quoted_feature_build --project tests/Beutl.Benchmarks/Beutl.Benchmarks.csproj -- --filter '*RenderPipelineBenchmarks*'"
baseline_command="${BEUTL_BASELINE_BENCHMARK_COMMAND:-$default_baseline_command}"
feature_command="${BEUTL_FEATURE_BENCHMARK_COMMAND:-$default_feature_command}"
baseline_sha="$(git -C "$baseline_worktree" rev-parse HEAD)"
feature_sha="$(git -C "$feature_worktree" rev-parse HEAD)"
runner_path="$(cd "$(dirname "$0")" && pwd -P)/$(basename "$0")"
runner_sha256="$(sha256_file "$runner_path")"

mkdir -p "$output_root/baseline-a" "$output_root/feature" "$output_root/baseline-b"
run_benchmark "baseline A" "$baseline_worktree" "$baseline_command" "$output_root/baseline-a"
run_benchmark "feature" "$feature_worktree" "$feature_command" "$output_root/feature"
run_benchmark "baseline B repeat" "$baseline_worktree" "$baseline_command" "$output_root/baseline-b"

baseline_result="$(cat "$output_root/baseline-a/full-result-path.txt")"
baseline_repeat_result="$(cat "$output_root/baseline-b/full-result-path.txt")"
feature_result="$(cat "$output_root/feature/full-result-path.txt")"
analyzer_project="$feature_worktree/tests/Beutl.Benchmarks/Beutl.Benchmarks.csproj"

dotnet run -c Release --artifacts-path "$temporary_build_root/analyzer" --project "$analyzer_project" -- paired-self-test
dotnet run -c Release --artifacts-path "$temporary_build_root/analyzer" --project "$analyzer_project" -- \
    paired-analyze \
    --baseline-results "$baseline_result" \
    --baseline-repeat-results "$baseline_repeat_result" \
    --feature-results "$feature_result" \
    --baseline-counters "$output_root/baseline-a/counters" \
    --baseline-repeat-counters "$output_root/baseline-b/counters" \
    --feature-counters "$output_root/feature/counters" \
    --baseline-stdout "$output_root/baseline-a/raw-benchmark-stdout.txt" \
    --baseline-repeat-stdout "$output_root/baseline-b/raw-benchmark-stdout.txt" \
    --feature-stdout "$output_root/feature/raw-benchmark-stdout.txt" \
    --baseline-sha "$baseline_sha" \
    --feature-sha "$feature_sha" \
    --baseline-command "$baseline_command" \
    --baseline-repeat-command "$baseline_command" \
    --feature-command "$feature_command" \
    --runner-sha256 "$runner_sha256" \
    --baseline-harness "$baseline_harness" \
    --output "$output_root/manifest.json"

echo "Paired benchmark evidence written to $output_root/manifest.json"
