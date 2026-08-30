#!/usr/bin/env bash
#
# SC-008 — runs the paired render-pipeline benchmark as baseline A, feature, baseline B on one machine and
# analyses the result into a bootstrap confidence-interval manifest under this directory.
#
# The A/feature/B ordering is the drift control the criterion specifies: the analyzer first bootstraps
# median(B) / median(A) and refuses to pool the baseline unless that interval contains 1.0 with a symmetric
# factor of at most 1.20. Machine drift between the two baseline runs therefore fails the run instead of being
# attributed to the feature.
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
evidence_dir="${repo_root}/docs/specs/004-gpu-pass-fusion/evidence"
mode="fusion"
baseline_ref=""
output="${evidence_dir}/sc-008-paired-benchmark-manifest.json"
work_dir=""
filter='*RenderPipelineBenchmarks*'
primary_case="ShaderOpacityShader"
bootstrap_iterations="100000"

usage() {
    cat <<'USAGE'
Usage: run-paired-benchmarks.sh [--mode fusion|worktree] [--baseline-ref <git ref>]
                                [--output <path>] [--work-dir <path>]
                                [--filter <benchmark filter>] [--primary-case <name>]
                                [--bootstrap-iterations <n>]

Modes:
  fusion    (default) Both sides come from this working tree's build. The baseline runs are this branch's
            renderer with BEUTL_RENDER_BENCHMARK_FUSION_MODE=Disabled; the feature run has it Enabled. This
            measures the fusion optimizer inside this feature's renderer. It is a weaker claim than SC-008's
            "post-feature / pre-feature" wording, and the manifest records that in `comparisonMode`.

  worktree  The baseline runs come from a separate git worktree at --baseline-ref, so the two sides really are
            two engine builds. This requires the benchmark harness to exist and to compile at that ref; the
            script checks and reports exactly what is missing rather than silently measuring something else.

Exit codes: 0 the manifest was written and its acceptance passed; 2 written and acceptance failed;
1 the run could not be completed.
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --mode) mode="$2"; shift 2 ;;
        --baseline-ref) baseline_ref="$2"; shift 2 ;;
        --output) output="$2"; shift 2 ;;
        --work-dir) work_dir="$2"; shift 2 ;;
        --filter) filter="$2"; shift 2 ;;
        --primary-case) primary_case="$2"; shift 2 ;;
        --bootstrap-iterations) bootstrap_iterations="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument '$1'." >&2; usage >&2; exit 1 ;;
    esac
done

if [[ -z "$work_dir" ]]; then
    work_dir="$(mktemp -d "${TMPDIR:-/tmp}/beutl-sc008.XXXXXX")"
fi
mkdir -p "$work_dir"
echo "==> Working directory: ${work_dir}"

benchmark_project="${repo_root}/tests/Beutl.Benchmarks/Beutl.Benchmarks.csproj"
harness_relative_path="tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs"
baseline_project="$benchmark_project"
baseline_fusion_mode="Disabled"
feature_fusion_mode="Enabled"
comparison_mode="in-tree-fusion-disabled-baseline-vs-fusion-enabled-feature"
baseline_worktree=""

case "$mode" in
    fusion)
        if [[ -n "$baseline_ref" ]]; then
            echo "--baseline-ref only applies to --mode worktree." >&2
            exit 1
        fi
        ;;
    worktree)
        if [[ -z "$baseline_ref" ]]; then
            echo "--mode worktree requires --baseline-ref." >&2
            exit 1
        fi
        if ! git -C "$repo_root" rev-parse --verify "${baseline_ref}^{commit}" >/dev/null 2>&1; then
            echo "'${baseline_ref}' is not a commit in this repository." >&2
            exit 1
        fi
        if ! git -C "$repo_root" cat-file -e "${baseline_ref}:${harness_relative_path}" 2>/dev/null; then
            cat >&2 <<EOF
'${baseline_ref}' does not carry ${harness_relative_path}.

A cross-build paired measurement needs a benchmark harness that compiles against BOTH engine builds: the
baseline ref's renderer and this branch's. This tree's harness is written against this branch's recording
contract (void RenderNode.Process, RenderNodeContext, ContainerRenderNode), so it cannot be compiled against a
pre-feature engine. Supply a --baseline-ref that carries a compatible harness, or use --mode fusion.
EOF
            exit 1
        fi
        baseline_worktree="${work_dir}/baseline-worktree"
        echo "==> Creating a baseline worktree at ${baseline_ref}"
        git -C "$repo_root" worktree add --detach "$baseline_worktree" "$baseline_ref" >/dev/null
        baseline_project="${baseline_worktree}/tests/Beutl.Benchmarks/Beutl.Benchmarks.csproj"
        baseline_fusion_mode="Enabled"
        comparison_mode="cross-build-baseline-${baseline_ref}-vs-feature-worktree"
        ;;
    *)
        echo "Unknown mode '${mode}'." >&2
        usage >&2
        exit 1
        ;;
esac

cleanup() {
    if [[ -n "$baseline_worktree" && -d "$baseline_worktree" ]]; then
        git -C "$repo_root" worktree remove --force "$baseline_worktree" >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

run_side() {
    local label="$1" project="$2" fusion_mode="$3"
    local run_dir="${work_dir}/${label}"
    rm -rf "$run_dir"
    mkdir -p "${run_dir}/counters"
    echo "==> ${label} (fusion=${fusion_mode})"
    BEUTL_RENDER_BENCHMARK_ARTIFACTS="$run_dir" \
    BEUTL_RENDER_BENCHMARK_COUNTERS="${run_dir}/counters" \
    BEUTL_RENDER_BENCHMARK_FUSION_MODE="$fusion_mode" \
        dotnet run -c Release --project "$project" -- --filter "$filter" \
        > "${run_dir}/stdout.txt" 2>&1 || {
            echo "The ${label} benchmark run failed; see ${run_dir}/stdout.txt" >&2
            tail -40 "${run_dir}/stdout.txt" >&2
            exit 1
        }
}

# Baseline, feature, baseline: the repeat pair brackets the feature run so drift shows up as an unstable
# baseline rather than as an effect.
run_side "baseline-a" "$baseline_project" "$baseline_fusion_mode"
run_side "feature" "$benchmark_project" "$feature_fusion_mode"
run_side "baseline-b" "$baseline_project" "$baseline_fusion_mode"

echo "==> Analysing"
mkdir -p "$(dirname "$output")"
set +e
dotnet run -c Release --project "$benchmark_project" -- analyze-paired \
    --baseline-a "${work_dir}/baseline-a" \
    --feature "${work_dir}/feature" \
    --baseline-b "${work_dir}/baseline-b" \
    --output "$output" \
    --primary-case "$primary_case" \
    --comparison-mode "$comparison_mode" \
    --bootstrap-iterations "$bootstrap_iterations"
analysis_status=$?
set -e

echo "==> Raw runs kept in ${work_dir}"
exit "$analysis_status"
