#!/usr/bin/env bash

set -euo pipefail

readonly BASELINE_SHA="43a38e665d9bf52548161a3917e748bd1457ff55"

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(git -C "$script_dir" rev-parse --show-toplevel)
patch_file="$script_dir/target-baseline-generator.patch"
paired_runner="$script_dir/run-paired-visual-evidence.sh"
destination="$script_dir/target-baseline"
benchmark_destination="$script_dir/target-benchmark"
capture_benchmark=true

usage() {
    printf 'Usage: %s [--output-dir <create-only-visual-directory>]\n' "$0" >&2
    printf 'With no arguments, the immutable starting-SHA visual and one-time benchmark baselines are captured.\n' >&2
}

if (( $# == 0 )); then
    :
elif (( $# == 2 )) && [[ $1 == "--output-dir" ]]; then
    destination=$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$2")
    capture_benchmark=false
else
    usage
    exit 2
fi

capture_target_benchmark() {
if [[ $capture_benchmark == true ]]; then
    if [[ ! -e $benchmark_destination ]]; then
        benchmark_run="$temporary_root/benchmark-run"
        benchmark_staging="$temporary_root/benchmark-output"
        benchmark_stdout="$temporary_root/raw-benchmark-stdout.txt"
        mkdir "$benchmark_run" "$benchmark_staging"
        run_started_utc=$(python3 -c 'import datetime; print(datetime.datetime.now(datetime.timezone.utc).isoformat())')

        (
            cd "$baseline_worktree"
            BEUTL_REQUIRE_GPU=1 \
            dotnet run -c Release --no-build \
                --project "$baseline_worktree/.gpu-pass-baseline/Beutl.GpuPassTargetBaselineGenerator.csproj" \
                -- --benchmark --artifacts-dir "$benchmark_run"
        ) 2>&1 | tee "$benchmark_stdout"
        mv "$benchmark_stdout" "$benchmark_run/raw-benchmark-stdout.txt"

        run_completed_utc=$(python3 -c 'import datetime; print(datetime.datetime.now(datetime.timezone.utc).isoformat())')
        git -C "$baseline_worktree" diff --quiet || {
            printf 'Running the benchmark modified tracked files.\n' >&2
            exit 1
        }
        git -C "$baseline_worktree" diff --cached --binary --full-index > "$temporary_root/patched-after-benchmark.diff"
        [[ $(sha256_file "$temporary_root/patched-after-benchmark.diff") == "$patched_diff_sha" ]] || {
            printf 'The applied generator diff changed during benchmark capture.\n' >&2
            exit 1
        }

        python3 - "$benchmark_run" "$benchmark_staging" "$destination/manifest.json" \
            "$BASELINE_SHA" "$patch_sha" "$script_sha" "$paired_runner_sha" "$source_bundle_sha" \
            "$patched_diff_sha" "$run_started_utc" "$run_completed_utc" <<'PY'
import hashlib
import json
import pathlib
import re
import shutil
import sys

(
    run_root_text,
    output_root_text,
    visual_manifest_text,
    baseline_sha,
    patch_sha,
    script_sha,
    runner_sha,
    source_sha,
    diff_sha,
    started_utc,
    completed_utc,
) = sys.argv[1:]
run_root = pathlib.Path(run_root_text)
output_root = pathlib.Path(output_root_text)
visual_manifest_path = pathlib.Path(visual_manifest_text)
visual_manifest_bytes = visual_manifest_path.read_bytes()
visual = json.loads(visual_manifest_bytes)

results = run_root / "BenchmarkDotNet.Artifacts" / "results"
json_results = list(results.glob("*-report-full.json"))
markdown_results = list(results.glob("*-report-github.md"))
if len(json_results) != 1 or len(markdown_results) != 1:
    raise SystemExit("BenchmarkDotNet did not produce exactly one full JSON and one GitHub Markdown result")

expected_cases = ["NoEffectControl", "ShaderOpacityShader", "ShaderOpacityShaderBarrier"]
counter_files = sorted((run_root / "counters").glob("*.json"))
if [path.stem for path in counter_files] != expected_cases:
    raise SystemExit(f"Benchmark counter case set mismatch: {[path.stem for path in counter_files]}")
counter_cases = {}
for path in counter_files:
    value = json.loads(path.read_text(encoding="utf-8"))
    if value.get("caseName") != path.stem:
        raise SystemExit(f"Counter file case mismatch: {path}")
    if value.get("fingerprint") != visual.get("fingerprint"):
        raise SystemExit(f"Benchmark fingerprint differs from visual evidence before accepting timing: {path.stem}")
    if value.get("setupWarmupFrames") != 5:
        raise SystemExit(f"Benchmark setup warm-up count mismatch: {path.stem}")
    if value.get("seed") != 20040719 or not value.get("lastRequestCounters"):
        raise SystemExit(f"Benchmark seed or request counters are missing: {path.stem}")
    counter_cases[path.stem] = value

raw = json.loads(json_results[0].read_text(encoding="utf-8"))
benchmarks = raw.get("Benchmarks") or []
if len(benchmarks) != 3:
    raise SystemExit("BenchmarkDotNet JSON does not contain exactly three benchmark cases")
statistics = {}
for benchmark in benchmarks:
    full_name = benchmark.get("FullName", "")
    match = re.search(r'CaseName: "([^"]+)"', full_name)
    if match is None:
        raise SystemExit(f"BenchmarkDotNet case parameter is missing: {full_name}")
    case_name = match.group(1)
    values = benchmark.get("Statistics") or {}
    if case_name not in expected_cases or int(values.get("N", 0)) < 15:
        raise SystemExit(f"BenchmarkDotNet statistics are incomplete: {case_name}")
    statistics[case_name] = {
        "sampleCount": int(values["N"]),
        "medianNanoseconds": float(values["Median"]),
        "meanNanoseconds": float(values["Mean"]),
        "standardDeviationNanoseconds": float(values["StandardDeviation"]),
        "minimumNanoseconds": float(values["Min"]),
        "maximumNanoseconds": float(values["Max"]),
    }
if sorted(statistics) != expected_cases:
    raise SystemExit(f"BenchmarkDotNet statistics case set mismatch: {sorted(statistics)}")

fixed_sources = {
    "raw-benchmark-full.json": json_results[0],
    "raw-benchmark-github.md": markdown_results[0],
    "raw-benchmark-stdout.txt": run_root / "raw-benchmark-stdout.txt",
}
for name, source in fixed_sources.items():
    if not source.is_file() or source.stat().st_size == 0:
        raise SystemExit(f"Raw benchmark artifact is missing or empty: {source}")
    shutil.copyfile(source, output_root / name)

counters_path = output_root / "counters.json"
counters_path.write_text(
    json.dumps({"schemaVersion": 1, "cases": counter_cases}, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)

artifact_hashes = {
    path.name: hashlib.sha256(path.read_bytes()).hexdigest()
    for path in sorted(output_root.iterdir())
    if path.is_file()
}
host = raw.get("HostEnvironmentInfo") or {}
manifest = {
    "schemaVersion": 1,
    "baselineCodeSha": baseline_sha,
    "prePatchRepositoryState": "clean",
    "patchedDiffSha256": diff_sha,
    "visualManifestSha256": hashlib.sha256(visual_manifest_bytes).hexdigest(),
    "runStartedUtc": started_utc,
    "runCompletedUtc": completed_utc,
    "command": "docs/specs/004-gpu-pass-fusion/evidence/generate-target-baseline.sh",
    "benchmarkDotNetVersion": host.get("BenchmarkDotNetVersion"),
    "fingerprint": visual["fingerprint"],
    "evidenceTools": {
        "generatorPatchSha256": patch_sha,
        "generatorScriptSha256": script_sha,
        "pairedRunnerSha256": runner_sha,
        "generatorSourceBundleSha256": source_sha,
    },
    "configuration": {
        "seed": 20040719,
        "width": 192,
        "height": 108,
        "setupWarmupFrames": 5,
        "benchmarkWarmupIterations": 3,
        "measurementIterations": 15,
        "launchCount": 1,
        "invocationCount": 1,
        "lifetime": "persistent-root-external-target-canvas-processor-and-node-cache",
        "requestShape": "complete-target-frame-with-rgba16f-readback",
    },
    "cases": statistics,
    "artifactSha256": artifact_hashes,
    "scope": "minimum starting-SHA baseline; the 11-case paired confidence gate remains T112-T115",
}
if not isinstance(manifest["benchmarkDotNetVersion"], str) or not manifest["benchmarkDotNetVersion"]:
    raise SystemExit("BenchmarkDotNet version is missing from its raw JSON")
(output_root / "manifest.json").write_text(
    json.dumps(manifest, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
PY

        mkdir -p "$(dirname -- "$benchmark_destination")"
        mkdir "$benchmark_destination"
        cp "$benchmark_staging/manifest.json" "$benchmark_destination/manifest.json"
        cp "$benchmark_staging/raw-benchmark-full.json" "$benchmark_destination/raw-benchmark-full.json"
        cp "$benchmark_staging/raw-benchmark-github.md" "$benchmark_destination/raw-benchmark-github.md"
        cp "$benchmark_staging/raw-benchmark-stdout.txt" "$benchmark_destination/raw-benchmark-stdout.txt"
        cp "$benchmark_staging/counters.json" "$benchmark_destination/counters.json"
        printf 'Created immutable target benchmark at %s\n' "$benchmark_destination"
    fi

    python3 - "$benchmark_destination" "$destination/manifest.json" "$BASELINE_SHA" \
        "$patch_sha" "$script_sha" "$paired_runner_sha" "$source_bundle_sha" "$patched_diff_sha" <<'PY'
import hashlib
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
visual_path = pathlib.Path(sys.argv[2])
baseline_sha, patch_sha, script_sha, runner_sha, source_sha, diff_sha = sys.argv[3:]
if not root.is_dir():
    raise SystemExit(f"Immutable benchmark destination is not a directory: {root}")
manifest_path = root / "manifest.json"
if not manifest_path.is_file():
    raise SystemExit("Immutable benchmark manifest is missing")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
visual_bytes = visual_path.read_bytes()
visual = json.loads(visual_bytes)

expected = {
    "baselineCodeSha": baseline_sha,
    "prePatchRepositoryState": "clean",
    "patchedDiffSha256": diff_sha,
    "visualManifestSha256": hashlib.sha256(visual_bytes).hexdigest(),
}
for name, value in expected.items():
    if manifest.get(name) != value:
        raise SystemExit(f"Immutable benchmark provenance mismatch: {name}")
expected_tools = {
    "generatorPatchSha256": patch_sha,
    "generatorScriptSha256": script_sha,
    "pairedRunnerSha256": runner_sha,
    "generatorSourceBundleSha256": source_sha,
}
for name, value in expected_tools.items():
    if (manifest.get("evidenceTools") or {}).get(name) != value:
        raise SystemExit(f"Immutable benchmark tool hash mismatch: {name}")
if manifest.get("fingerprint") != visual.get("fingerprint"):
    raise SystemExit("Immutable benchmark and visual fingerprints differ")

hashes = manifest.get("artifactSha256") or {}
expected_files = {"manifest.json", *hashes.keys()}
actual_files = {path.name for path in root.iterdir() if path.is_file()}
if actual_files != expected_files:
    raise SystemExit(f"Immutable benchmark file set mismatch: {sorted(actual_files)}")
for name, expected_hash in hashes.items():
    path = root / name
    if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected_hash:
        raise SystemExit(f"Immutable benchmark artifact hash mismatch: {name}")

expected_cases = ["NoEffectControl", "ShaderOpacityShader", "ShaderOpacityShaderBarrier"]
if sorted((manifest.get("cases") or {}).keys()) != expected_cases:
    raise SystemExit("Immutable benchmark statistics case set is incomplete")
counters = json.loads((root / "counters.json").read_text(encoding="utf-8"))
counter_cases = counters.get("cases") or {}
if sorted(counter_cases.keys()) != expected_cases:
    raise SystemExit("Immutable benchmark counter case set is incomplete")
for case_name, value in counter_cases.items():
    if value.get("fingerprint") != visual.get("fingerprint") or not value.get("lastRequestCounters"):
        raise SystemExit(f"Immutable benchmark counter provenance is invalid: {case_name}")
print(f"Verified immutable target benchmark at {root}")
PY
fi
}

for command_name in git dotnet python3; do
    command -v "$command_name" >/dev/null 2>&1 || {
        printf 'Required command is unavailable: %s\n' "$command_name" >&2
        exit 1
    }
done

[[ -f $patch_file ]] || { printf 'Missing generator patch: %s\n' "$patch_file" >&2; exit 1; }
[[ -f $paired_runner ]] || { printf 'Missing paired runner: %s\n' "$paired_runner" >&2; exit 1; }

sha256_file() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{print $1}'
    else
        printf 'Neither sha256sum nor shasum is available.\n' >&2
        return 1
    fi
}

temporary_base=$(CDPATH= cd -- "${TMPDIR:-/tmp}" && pwd -P)
temporary_root=$(mktemp -d "$temporary_base/beutl-gpu-pass-baseline.XXXXXX")
temporary_root=$(CDPATH= cd -- "$temporary_root" && pwd -P)
baseline_worktree="$temporary_root/worktree"
staging_output="$temporary_root/output"
worktree_registered=false

cleanup() {
    local status=$?
    trap - EXIT INT TERM
    if [[ $worktree_registered == true && $baseline_worktree == "$temporary_root/worktree" ]]; then
        git -C "$repository_root" worktree remove --force "$baseline_worktree" >/dev/null 2>&1 || true
    fi
    case "$temporary_root" in
        "$temporary_base"/beutl-gpu-pass-baseline.*)
            rm -rf -- "$temporary_root"
            ;;
        *)
            printf 'Refusing to remove unexpected temporary path: %s\n' "$temporary_root" >&2
            ;;
    esac
    exit "$status"
}
trap cleanup EXIT INT TERM

git -C "$repository_root" cat-file -e "$BASELINE_SHA^{commit}"
git -C "$repository_root" worktree add --detach "$baseline_worktree" "$BASELINE_SHA"
worktree_registered=true

actual_sha=$(git -C "$baseline_worktree" rev-parse HEAD)
[[ $actual_sha == "$BASELINE_SHA" ]] || {
    printf 'Pinned worktree SHA mismatch: expected %s, found %s\n' "$BASELINE_SHA" "$actual_sha" >&2
    exit 1
}

prepatch_state=$(git -C "$baseline_worktree" status --porcelain=v1 --untracked-files=all)
[[ -z $prepatch_state ]] || {
    printf 'Pinned worktree was not clean before applying the generator patch.\n%s\n' "$prepatch_state" >&2
    exit 1
}

git -C "$baseline_worktree" apply --check --index "$patch_file"
git -C "$baseline_worktree" apply --index "$patch_file"

patch_paths=$(git -C "$baseline_worktree" apply --numstat "$patch_file" | awk -F '\t' '{print $3}' | LC_ALL=C sort)
staged_paths=$(git -C "$baseline_worktree" diff --cached --name-only | LC_ALL=C sort)
[[ -n $patch_paths && $patch_paths == "$staged_paths" ]] || {
    printf 'Applied patch path set differs from its declared path set.\nDeclared:\n%s\nStaged:\n%s\n' \
        "$patch_paths" "$staged_paths" >&2
    exit 1
}
git -C "$baseline_worktree" diff --quiet || {
    printf 'The generator patch left unstaged tracked changes.\n' >&2
    exit 1
}

patched_diff="$temporary_root/patched.diff"
git -C "$baseline_worktree" diff --cached --binary --full-index > "$patched_diff"
patched_diff_sha=$(sha256_file "$patched_diff")

source_index="$temporary_root/generator-source-index.txt"
while IFS= read -r source_path; do
    git -C "$baseline_worktree" ls-files -s -- "$source_path"
done <<< "$staged_paths" > "$source_index"

patch_sha=$(sha256_file "$patch_file")
script_sha=$(sha256_file "$script_dir/generate-target-baseline.sh")
paired_runner_sha=$(sha256_file "$paired_runner")
source_bundle_sha=$(sha256_file "$source_index")

dotnet restore "$baseline_worktree/.gpu-pass-baseline/Beutl.GpuPassTargetBaselineGenerator.csproj"
dotnet build "$baseline_worktree/.gpu-pass-baseline/Beutl.GpuPassTargetBaselineGenerator.csproj" \
    -c Release --no-restore

git -C "$baseline_worktree" diff --quiet || {
    printf 'Building the generator modified tracked files.\n' >&2
    exit 1
}
git -C "$baseline_worktree" diff --cached --binary --full-index > "$temporary_root/patched-after-build.diff"
[[ $(sha256_file "$temporary_root/patched-after-build.diff") == "$patched_diff_sha" ]] || {
    printf 'The applied generator diff changed during build.\n' >&2
    exit 1
}

mkdir "$staging_output"
BEUTL_BASELINE_REPO_ROOT="$baseline_worktree" \
BEUTL_BASELINE_PREPATCH_STATE=clean \
BEUTL_BASELINE_PATCHED_DIFF_SHA256="$patched_diff_sha" \
BEUTL_BASELINE_PATCH_SHA256="$patch_sha" \
BEUTL_BASELINE_GENERATOR_SCRIPT_SHA256="$script_sha" \
BEUTL_BASELINE_PAIRED_RUNNER_SHA256="$paired_runner_sha" \
BEUTL_BASELINE_SOURCE_BUNDLE_SHA256="$source_bundle_sha" \
BEUTL_REQUIRE_GPU=1 \
dotnet run -c Release --no-build \
    --project "$baseline_worktree/.gpu-pass-baseline/Beutl.GpuPassTargetBaselineGenerator.csproj" \
    -- --output-dir "$staging_output"

python3 - "$staging_output" "$BASELINE_SHA" "$patch_sha" "$script_sha" "$paired_runner_sha" \
    "$source_bundle_sha" "$patched_diff_sha" <<'PY'
import hashlib
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
baseline_sha, patch_sha, script_sha, runner_sha, source_sha, diff_sha = sys.argv[2:]
manifest_path = root / "manifest.json"
if not manifest_path.is_file():
    raise SystemExit("Generated manifest.json is missing")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

expected_provenance = {
    "baselineCodeSha": baseline_sha,
    "prePatchRepositoryState": "clean",
    "patchedDiffSha256": diff_sha,
}
for name, expected in expected_provenance.items():
    if manifest.get(name) != expected:
        raise SystemExit(f"Manifest provenance mismatch for {name}")

tools = manifest.get("evidenceTools") or {}
expected_tools = {
    "generatorPatchSha256": patch_sha,
    "generatorScriptSha256": script_sha,
    "pairedRunnerSha256": runner_sha,
    "generatorSourceBundleSha256": source_sha,
}
for name, expected in expected_tools.items():
    if tools.get(name) != expected:
        raise SystemExit(f"Manifest evidence-tool hash mismatch for {name}")

fingerprint = manifest.get("fingerprint")
if not isinstance(fingerprint, dict) or not fingerprint:
    raise SystemExit("Manifest fingerprint is missing")
for name, value in fingerprint.items():
    values = value if isinstance(value, list) else [value]
    if not values or any(not isinstance(item, str) or not item.strip() or "unknown" in item.lower() for item in values):
        raise SystemExit(f"Manifest fingerprint field is missing or unknown: {name}")

hashes = manifest.get("artifactSha256")
scenes = manifest.get("scenes")
if not isinstance(hashes, dict) or not hashes or not isinstance(scenes, list):
    raise SystemExit("Manifest artifact or scene table is missing")

allowed = {"manifest.json", *hashes.keys()}
actual = {path.name for path in root.iterdir() if path.is_file()}
if actual != allowed:
    raise SystemExit(f"Generated file set mismatch: expected {sorted(allowed)}, found {sorted(actual)}")
if any("/" in name or "\\" in name or not name.endswith(".rgba16f") for name in hashes):
    raise SystemExit("Manifest contains an unsafe or non-RGBA16F artifact name")

scene_by_blob = {scene.get("blob"): scene for scene in scenes if scene.get("blob") is not None}
if set(scene_by_blob) != set(hashes):
    raise SystemExit("Scene blob set does not match artifact hash set")
for name, expected_hash in hashes.items():
    path = root / name
    if not path.is_file():
        raise SystemExit(f"Missing generated artifact: {name}")
    actual_hash = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual_hash != expected_hash:
        raise SystemExit(f"Generated artifact hash mismatch: {name}")
    scene = scene_by_blob[name]
    expected_length = int(scene["blobWidth"]) * int(scene["blobHeight"]) * 8
    if path.stat().st_size != expected_length:
        raise SystemExit(f"RGBA16F payload is not tightly row-packed: {name}")

parity = [scene for scene in scenes if scene.get("role") == "parity"]
if not parity or any(not scene.get("controlSceneId") or not scene.get("nonVacuity") for scene in parity):
    raise SystemExit("Parity scenes are missing controls or non-vacuity evidence")
if len(manifest.get("allocationFailures") or []) != 2:
    raise SystemExit("Preview/delivery allocation-failure evidence is incomplete")
PY

if [[ -e $destination ]]; then
    python3 - "$staging_output" "$destination" <<'PY'
import pathlib
import sys

generated = pathlib.Path(sys.argv[1])
existing = pathlib.Path(sys.argv[2])
if not existing.is_dir():
    raise SystemExit(f"Create-only destination already exists and is not a directory: {existing}")
generated_files = sorted(path.name for path in generated.iterdir() if path.is_file())
existing_files = sorted(path.name for path in existing.iterdir() if path.is_file())
if generated_files != existing_files:
    raise SystemExit("Immutable baseline destination has a different file set")
for name in generated_files:
    if (generated / name).read_bytes() != (existing / name).read_bytes():
        raise SystemExit(f"Immutable baseline differs from regenerated evidence: {name}")
print(f"Verified existing immutable target baseline at {existing}")
PY
else
    mkdir -p "$(dirname -- "$destination")"
    mkdir "$destination"
    cp "$staging_output/manifest.json" "$destination/manifest.json"
    while IFS= read -r artifact; do
        cp "$staging_output/$artifact" "$destination/$artifact"
    done < <(python3 - "$staging_output/manifest.json" <<'PY'
import json
import pathlib
import sys

manifest = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
for name in sorted(manifest["artifactSha256"]):
    print(name)
PY
)
    printf 'Created immutable target baseline at %s\n' "$destination"
fi

capture_target_benchmark
