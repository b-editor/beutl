#!/usr/bin/env bash

set -euo pipefail

readonly BASELINE_SHA="83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53"

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repository_root=$(git -C "$script_dir" rev-parse --show-toplevel)
patch_file="$script_dir/target-baseline-generator.patch"
paired_runner="$script_dir/run-paired-visual-evidence.sh"
benchmark_runner="$script_dir/run-paired-benchmarks.sh"
refresh_script="$script_dir/refresh-intentional-visual-baselines.sh"
destination="$script_dir/target-baseline"
benchmark_destination="$script_dir/target-benchmark"
capture_benchmark=true
self_test=false

usage() {
    printf 'Usage: %s [--output-dir <create-only-visual-directory> | --self-test]\n' "$0" >&2
    printf 'With no arguments, the immutable starting-SHA visual and one-time benchmark baselines are captured.\n' >&2
}

if (( $# == 0 )); then
    :
elif (( $# == 1 )) && [[ $1 == "--self-test" ]]; then
    capture_benchmark=false
    self_test=true
elif (( $# == 2 )) && [[ $1 == "--output-dir" ]]; then
    destination=$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$2")
    capture_benchmark=false
else
    usage
    exit 2
fi

validate_target_benchmark_json() {
    python3 - "$@" <<'PY'
import copy
import hashlib
import json
import math
import pathlib
import re
import statistics as py_statistics
import sys

EXPECTED_CASES = ["NoEffectControl", "ShaderOpacityShader", "ShaderOpacityShaderBarrier"]
EXPECTED_JOB_ID = "TargetBaselinePersistentGpu"
EXPECTED_JOB_FIELDS = {
    "InvocationCount": "1",
    "IterationCount": "15",
    "LaunchCount": "1",
    "RunStrategy": "Monitoring",
    "UnrollFactor": "1",
    "WarmupCount": "3",
}


def require_finite_number(value, field, case_name):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(f"BenchmarkDotNet {field} is not finite for {case_name}")
    return float(value)


def parse_case_name(benchmark):
    full_name = benchmark.get("FullName")
    if not isinstance(full_name, str):
        raise ValueError("BenchmarkDotNet FullName is missing")
    match = re.search(r'CaseName: "([^"]+)"', full_name)
    if match is None:
        raise ValueError(f"BenchmarkDotNet case parameter is missing: {full_name}")
    return match.group(1)


def parse_job_metadata(benchmark, case_name):
    display = benchmark.get("DisplayInfo")
    if not isinstance(display, str):
        raise ValueError(f"BenchmarkDotNet DisplayInfo is missing for {case_name}")
    separator = display.find(": ")
    parameters = display.rfind(" [CaseName=")
    if separator < 0 or parameters <= separator + 2:
        raise ValueError(f"BenchmarkDotNet job metadata is malformed for {case_name}: {display}")
    job_display = display[separator + 2:parameters]
    match = re.fullmatch(r"([^(),]+)\(([^()]*)\)", job_display)
    if match is None:
        raise ValueError(f"BenchmarkDotNet job metadata is malformed for {case_name}: {job_display}")
    if match.group(1) != EXPECTED_JOB_ID:
        raise ValueError(
            f"BenchmarkDotNet job id mismatch for {case_name}: "
            f"expected {EXPECTED_JOB_ID}, found {match.group(1)}"
        )

    fields = {}
    for item in match.group(2).split(","):
        name, separator, value = item.strip().partition("=")
        if not separator or not name or not value or name in fields:
            raise ValueError(f"BenchmarkDotNet job metadata is malformed for {case_name}: {job_display}")
        fields[name] = value
    if set(fields) != set(EXPECTED_JOB_FIELDS):
        raise ValueError(
            f"BenchmarkDotNet job field set mismatch for {case_name}: "
            f"expected {sorted(EXPECTED_JOB_FIELDS)}, found {sorted(fields)}"
        )
    for name, expected in EXPECTED_JOB_FIELDS.items():
        if fields[name] != expected:
            raise ValueError(
                f"BenchmarkDotNet job metadata mismatch for {case_name}: "
                f"{name} expected {expected}, found {fields[name]}"
            )
    return {
        "benchmarkWarmupIterations": int(fields["WarmupCount"]),
        "measurementIterations": int(fields["IterationCount"]),
        "launchCount": int(fields["LaunchCount"]),
        "invocationCount": int(fields["InvocationCount"]),
        "unrollFactor": int(fields["UnrollFactor"]),
        "runStrategy": fields["RunStrategy"],
    }


def validate(raw):
    benchmarks = raw.get("Benchmarks") if isinstance(raw, dict) else None
    if not isinstance(benchmarks, list) or len(benchmarks) != len(EXPECTED_CASES):
        raise ValueError("BenchmarkDotNet JSON does not contain exactly three benchmark cases")

    configuration = None
    cases = {}
    for benchmark in benchmarks:
        if not isinstance(benchmark, dict):
            raise ValueError("BenchmarkDotNet benchmark entry is malformed")
        case_name = parse_case_name(benchmark)
        if case_name not in EXPECTED_CASES or case_name in cases:
            raise ValueError(f"BenchmarkDotNet case set is invalid: {case_name}")
        job_configuration = parse_job_metadata(benchmark, case_name)
        if configuration is None:
            configuration = job_configuration
        elif configuration != job_configuration:
            raise ValueError(f"BenchmarkDotNet job metadata differs between cases: {case_name}")

        values = benchmark.get("Statistics")
        if not isinstance(values, dict):
            raise ValueError(f"BenchmarkDotNet statistics are missing for {case_name}")
        original_values = values.get("OriginalValues")
        expected_samples = job_configuration["measurementIterations"]
        if not isinstance(original_values, list) or len(original_values) != expected_samples:
            observed = len(original_values) if isinstance(original_values, list) else "missing"
            raise ValueError(
                f"BenchmarkDotNet case {case_name} must contain exactly {expected_samples} "
                f"measured samples; found {observed}"
            )
        if isinstance(values.get("N"), bool) or values.get("N") != expected_samples:
            raise ValueError(
                f"BenchmarkDotNet case {case_name} Statistics.N must equal exactly "
                f"{expected_samples}; found {values.get('N')}"
            )
        for index, value in enumerate(original_values):
            measured = require_finite_number(value, f"OriginalValues[{index}]", case_name)
            if measured <= 0:
                raise ValueError(f"BenchmarkDotNet measured sample is not positive for {case_name}")

        cases[case_name] = {
            "sampleCount": len(original_values),
            "medianNanoseconds": require_finite_number(values.get("Median"), "Median", case_name),
            "meanNanoseconds": require_finite_number(values.get("Mean"), "Mean", case_name),
            "standardDeviationNanoseconds": require_finite_number(
                values.get("StandardDeviation"), "StandardDeviation", case_name
            ),
            "minimumNanoseconds": require_finite_number(values.get("Min"), "Min", case_name),
            "maximumNanoseconds": require_finite_number(values.get("Max"), "Max", case_name),
        }

    if sorted(cases) != EXPECTED_CASES:
        raise ValueError(f"BenchmarkDotNet statistics case set mismatch: {sorted(cases)}")
    return {"configuration": configuration, "cases": cases}


def make_fixture(sample_count=15, job_overrides=None):
    fields = copy.copy(EXPECTED_JOB_FIELDS)
    if job_overrides:
        fields.update(job_overrides)
    job = EXPECTED_JOB_ID + "(" + ", ".join(f"{name}={value}" for name, value in fields.items()) + ")"
    values = [float(index + 1) for index in range(sample_count)]
    return {
        "Benchmarks": [
            {
                "FullName": f'Synthetic.CompleteTargetFrame(CaseName: "{case_name}")',
                "DisplayInfo": f"Synthetic.CompleteTargetFrame: {job} [CaseName={case_name}]",
                "Statistics": {
                    "OriginalValues": values,
                    "N": sample_count,
                    "Median": py_statistics.median(values),
                    "Mean": py_statistics.mean(values),
                    "StandardDeviation": py_statistics.pstdev(values),
                    "Min": min(values),
                    "Max": max(values),
                },
            }
            for case_name in EXPECTED_CASES
        ]
    }


def expect_rejected(label, fixture):
    try:
        validate(fixture)
    except ValueError:
        return
    raise SystemExit(f"Target benchmark metadata self-test failed to reject {label}")


mode = sys.argv[1] if len(sys.argv) > 1 else ""
if mode in ("validate", "validate-run"):
    if len(sys.argv) != 4:
        raise SystemExit(f"{mode} mode requires input and normalized JSON paths")
    input_path = pathlib.Path(sys.argv[2])
    if mode == "validate-run":
        json_results = list((input_path / "BenchmarkDotNet.Artifacts" / "results").glob("*-report-full.json"))
        if len(json_results) != 1:
            raise SystemExit("BenchmarkDotNet did not produce exactly one full JSON result")
        input_path = json_results[0]
    raw_bytes = input_path.read_bytes()
    raw = json.loads(raw_bytes)
    normalized = validate(raw)
    normalized["rawSha256"] = hashlib.sha256(raw_bytes).hexdigest()
    pathlib.Path(sys.argv[3]).write_text(
        json.dumps(normalized, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
elif mode == "self-test":
    if len(sys.argv) != 4:
        raise SystemExit("self-test mode requires the archived raw and manifest JSON paths")
    valid = validate(make_fixture())
    if valid["configuration"] != {
        "benchmarkWarmupIterations": 3,
        "measurementIterations": 15,
        "launchCount": 1,
        "invocationCount": 1,
        "unrollFactor": 1,
        "runStrategy": "Monitoring",
    }:
        raise SystemExit("Target benchmark metadata self-test did not derive the frozen job configuration")
    expect_rejected("20 measured samples", make_fixture(sample_count=20))
    expect_rejected(
        "IterationCount drift",
        make_fixture(sample_count=20, job_overrides={"IterationCount": "20"}),
    )
    for field, value in (
        ("WarmupCount", "4"),
        ("LaunchCount", "2"),
        ("InvocationCount", "2"),
        ("UnrollFactor", "2"),
        ("RunStrategy", "Throughput"),
    ):
        expect_rejected(f"{field} drift", make_fixture(job_overrides={field: value}))
    archived_raw_bytes = pathlib.Path(sys.argv[2]).read_bytes()
    archived = validate(json.loads(archived_raw_bytes))
    archived["rawSha256"] = hashlib.sha256(archived_raw_bytes).hexdigest()
    manifest = json.loads(pathlib.Path(sys.argv[3]).read_text(encoding="utf-8"))
    if (manifest.get("artifactSha256") or {}).get("raw-benchmark-full.json") != archived["rawSha256"]:
        raise SystemExit("Archived target benchmark manifest does not authenticate the validated raw JSON bytes")
    if manifest.get("cases") != archived["cases"]:
        raise SystemExit("Archived target benchmark statistics differ from the validated raw result")
    manifest_configuration = manifest.get("configuration") or {}
    for name in (
        "benchmarkWarmupIterations",
        "measurementIterations",
        "launchCount",
        "invocationCount",
    ):
        if manifest_configuration.get(name) != archived["configuration"][name]:
            raise SystemExit(f"Archived target benchmark configuration differs from raw metadata: {name}")
    print("Target benchmark metadata self-test and archived-result verification passed")
else:
    raise SystemExit(f"Unknown target benchmark validation mode: {mode}")
PY
}

if [[ $self_test == true ]]; then
    command -v python3 >/dev/null 2>&1 || {
        printf 'Required command is unavailable: python3\n' >&2
        exit 1
    }
    validate_target_benchmark_json self-test \
        "$benchmark_destination/raw-benchmark-full.json" \
        "$benchmark_destination/manifest.json"
    exit 0
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

        validated_benchmark="$temporary_root/validated-target-benchmark.json"
        validate_target_benchmark_json validate-run "$benchmark_run" "$validated_benchmark"

        python3 - "$benchmark_run" "$benchmark_staging" "$destination/manifest.json" "$validated_benchmark" \
            "$BASELINE_SHA" "$patch_sha" "$script_sha" "$paired_runner_sha" "$benchmark_runner_sha" "$refresh_script_sha" "$source_bundle_sha" \
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
    validated_benchmark_text,
    baseline_sha,
    patch_sha,
    script_sha,
    runner_sha,
    benchmark_runner_sha,
    refresh_sha,
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
validated = json.loads(pathlib.Path(validated_benchmark_text).read_text(encoding="utf-8"))
benchmark_configuration = validated["configuration"]
statistics = validated["cases"]

results = run_root / "BenchmarkDotNet.Artifacts" / "results"
json_results = list(results.glob("*-report-full.json"))
markdown_results = list(results.glob("*-report-github.md"))
if len(json_results) != 1 or len(markdown_results) != 1:
    raise SystemExit("BenchmarkDotNet did not produce exactly one full JSON and one GitHub Markdown result")
if hashlib.sha256(json_results[0].read_bytes()).hexdigest() != validated.get("rawSha256"):
    raise SystemExit("BenchmarkDotNet raw JSON changed after its job metadata was validated")

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
        "benchmarkRunnerSha256": benchmark_runner_sha,
        "generatorPatchSha256": patch_sha,
        "generatorScriptSha256": script_sha,
        "pairedRunnerSha256": runner_sha,
        "refreshScriptSha256": refresh_sha,
        "generatorSourceBundleSha256": source_sha,
    },
    "configuration": {
        "seed": 20040719,
        "width": 192,
        "height": 108,
        "setupWarmupFrames": 5,
        **benchmark_configuration,
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

    validated_historical_benchmark="$temporary_root/validated-immutable-target-benchmark.json"
    validate_target_benchmark_json validate \
        "$benchmark_destination/raw-benchmark-full.json" \
        "$validated_historical_benchmark"

    python3 - "$benchmark_destination" "$destination/manifest.json" "$BASELINE_SHA" \
        "$validated_historical_benchmark" <<'PY'
import hashlib
import json
import pathlib
import re
import sys

root = pathlib.Path(sys.argv[1])
visual_path = pathlib.Path(sys.argv[2])
baseline_sha = sys.argv[3]
validated = json.loads(pathlib.Path(sys.argv[4]).read_text(encoding="utf-8"))
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
}
for name, value in expected.items():
    if manifest.get(name) != value:
        raise SystemExit(f"Immutable benchmark provenance mismatch: {name}")
sha256_pattern = re.compile(r"[0-9a-f]{64}")
for name in ("patchedDiffSha256", "visualManifestSha256"):
    if sha256_pattern.fullmatch(str(manifest.get(name) or "")) is None:
        raise SystemExit(f"Immutable benchmark provenance hash is invalid: {name}")
historical_tools = manifest.get("evidenceTools") or {}
for name in (
    "benchmarkRunnerSha256",
    "generatorPatchSha256",
    "generatorScriptSha256",
    "pairedRunnerSha256",
    "refreshScriptSha256",
    "generatorSourceBundleSha256",
):
    if sha256_pattern.fullmatch(str(historical_tools.get(name) or "")) is None:
        raise SystemExit(f"Immutable benchmark historical tool hash is invalid: {name}")
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
if validated.get("rawSha256") != hashes.get("raw-benchmark-full.json"):
    raise SystemExit("Immutable benchmark manifest does not authenticate the validated raw JSON bytes")

expected_cases = ["NoEffectControl", "ShaderOpacityShader", "ShaderOpacityShaderBarrier"]
manifest_cases = manifest.get("cases") or {}
if sorted(manifest_cases.keys()) != expected_cases:
    raise SystemExit("Immutable benchmark statistics case set is incomplete")
if manifest_cases != validated.get("cases"):
    raise SystemExit("Immutable benchmark statistics differ from the validated raw BenchmarkDotNet result")
configuration = manifest.get("configuration") or {}
validated_configuration = validated.get("configuration") or {}
for name in (
    "benchmarkWarmupIterations",
    "measurementIterations",
    "launchCount",
    "invocationCount",
):
    if configuration.get(name) != validated_configuration.get(name):
        raise SystemExit(f"Immutable benchmark configuration differs from raw BenchmarkDotNet metadata: {name}")
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
[[ -f $benchmark_runner ]] || { printf 'Missing paired benchmark runner: %s\n' "$benchmark_runner" >&2; exit 1; }
[[ -f $refresh_script ]] || { printf 'Missing intentional-refresh script: %s\n' "$refresh_script" >&2; exit 1; }

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
benchmark_runner_sha=$(sha256_file "$benchmark_runner")
refresh_script_sha=$(sha256_file "$refresh_script")
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
BEUTL_BASELINE_BENCHMARK_RUNNER_SHA256="$benchmark_runner_sha" \
BEUTL_BASELINE_SOURCE_BUNDLE_SHA256="$source_bundle_sha" \
BEUTL_REQUIRE_GPU=1 \
dotnet run -c Release --no-build \
    --project "$baseline_worktree/.gpu-pass-baseline/Beutl.GpuPassTargetBaselineGenerator.csproj" \
    -- --output-dir "$staging_output"

python3 - "$staging_output/manifest.json" "$refresh_script_sha" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
manifest = json.loads(path.read_text(encoding="utf-8"))
tools = manifest.get("evidenceTools")
if not isinstance(tools, dict):
    raise SystemExit("Generated manifest evidenceTools is missing")
tools["refreshScriptSha256"] = sys.argv[2]
path.write_text(json.dumps(manifest, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
PY

python3 - "$staging_output" "$BASELINE_SHA" "$patch_sha" "$script_sha" "$paired_runner_sha" "$benchmark_runner_sha" \
    "$refresh_script_sha" "$source_bundle_sha" "$patched_diff_sha" <<'PY'
import hashlib
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
baseline_sha, patch_sha, script_sha, runner_sha, benchmark_runner_sha, refresh_sha, source_sha, diff_sha = sys.argv[2:]
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
    "benchmarkRunnerSha256": benchmark_runner_sha,
    "generatorPatchSha256": patch_sha,
    "generatorScriptSha256": script_sha,
    "pairedRunnerSha256": runner_sha,
    "refreshScriptSha256": refresh_sha,
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
import hashlib
import json
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

generated_manifest = json.loads((generated / "manifest.json").read_text(encoding="utf-8"))
existing_manifest = json.loads((existing / "manifest.json").read_text(encoding="utf-8"))
generated_hashes = generated_manifest.get("artifactSha256")
existing_hashes = existing_manifest.get("artifactSha256")
if not isinstance(generated_hashes, dict) or not isinstance(existing_hashes, dict):
    raise SystemExit("An immutable baseline artifact hash table is missing")
if set(generated_hashes) != set(existing_hashes):
    raise SystemExit("Generated and committed artifact sets differ")

# Keep this allowlist synchronized with the approved divergences in research.md.
approved_semantic_refreshes = {
    "scene3d-with-2d-tail.rgba16f",
}
if not approved_semantic_refreshes <= set(existing_hashes):
    raise SystemExit("The approved semantic-refresh artifact set is incomplete")

def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

for name in sorted(existing_hashes):
    generated_path = generated / name
    existing_path = existing / name
    if sha256(generated_path) != generated_hashes[name]:
        raise SystemExit(f"Regenerated legacy artifact hash mismatch: {name}")
    if sha256(existing_path) != existing_hashes[name]:
        raise SystemExit(f"Committed artifact hash mismatch: {name}")
    if name not in approved_semantic_refreshes and generated_path.read_bytes() != existing_path.read_bytes():
        raise SystemExit(f"Non-approved immutable baseline differs from regenerated evidence: {name}")

generated_scenes = {
    scene.get("id"): scene
    for scene in generated_manifest.get("scenes", [])
    if isinstance(scene, dict) and isinstance(scene.get("id"), str)
}
existing_scenes = {
    scene.get("id"): scene
    for scene in existing_manifest.get("scenes", [])
    if isinstance(scene, dict) and isinstance(scene.get("id"), str)
}
if set(generated_scenes) != set(existing_scenes):
    raise SystemExit("Generated and committed scene sets differ")
for scene_id in sorted(generated_scenes):
    generated_scene = dict(generated_scenes[scene_id])
    existing_scene = dict(existing_scenes[scene_id])
    blob = generated_scene.get("blob")
    if blob != existing_scene.get("blob"):
        raise SystemExit(f"Generated and committed scene blob mappings differ: {scene_id}")
    if blob in approved_semantic_refreshes:
        generated_scene.pop("nonVacuity", None)
        existing_scene.pop("nonVacuity", None)
    if generated_scene != existing_scene:
        raise SystemExit(f"Generated and committed scene contracts differ: {scene_id}")
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
