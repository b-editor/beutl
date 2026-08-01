#!/usr/bin/env bash

set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(git -C "$script_dir" rev-parse --show-toplevel)
baseline_root="$script_dir/target-baseline"
baseline_manifest="$baseline_root/manifest.json"
benchmark_manifest="$script_dir/target-benchmark/manifest.json"
test_support="$repo_root/tests/Beutl.UnitTests/Engine/Graphics/Rendering/Baseline/GpuPassFusionBaselineTestSupport.cs"
benchmark_test="$repo_root/tests/Beutl.UnitTests/Engine/Graphics/Rendering/Baseline/GpuPassFusionBaselineTests.cs"
provenance_doc="$script_dir/target-baseline.md"
acceptance_report="$script_dir/acceptance-report.md"

for command_name in dotnet git python3; do
    command -v "$command_name" >/dev/null 2>&1 || {
        printf 'Required command is unavailable: %s\n' "$command_name" >&2
        exit 1
    }
done

[[ -z $(git -C "$repo_root" status --porcelain=v1 --untracked-files=all) ]] || {
    printf 'Visual-baseline refresh requires a clean repository: %s\n' "$repo_root" >&2
    exit 1
}
feature_code_sha=$(git -C "$repo_root" rev-parse HEAD)
[[ $feature_code_sha =~ ^[0-9a-f]{40}$ ]] || {
    printf 'Could not resolve the feature code SHA from repository: %s\n' "$repo_root" >&2
    exit 1
}
generator_script="$script_dir/generate-target-baseline.sh"
paired_runner="$script_dir/run-paired-visual-evidence.sh"
refresh_script="$script_dir/$(basename -- "$0")"
for evidence_tool in "$generator_script" "$paired_runner" "$refresh_script"; do
    evidence_tool_relative=${evidence_tool#"$repo_root"/}
    git -C "$repo_root" ls-files --error-unmatch -- "$evidence_tool_relative" >/dev/null || {
        printf 'Visual-baseline evidence tool is not tracked: %s\n' "$evidence_tool" >&2
        exit 1
    }
done
generator_script_sha=$(python3 -c \
    'import hashlib, pathlib, sys; print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())' \
    "$generator_script")
paired_runner_sha=$(python3 -c \
    'import hashlib, pathlib, sys; print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())' \
    "$paired_runner")
refresh_script_sha=$(python3 -c \
    'import hashlib, pathlib, sys; print(hashlib.sha256(pathlib.Path(sys.argv[1]).read_bytes()).hexdigest())' \
    "$refresh_script")

temporary_root=$(mktemp -d "${TMPDIR:-/tmp}/beutl-visual-refresh.XXXXXX")
feature_output="$temporary_root/feature"

cleanup() {
    rm -rf -- "$temporary_root"
}
trap cleanup EXIT

[[ -f $baseline_manifest ]] || {
    printf 'Target baseline manifest is missing: %s\n' "$baseline_manifest" >&2
    exit 1
}

(
    cd "$repo_root"
    BEUTL_GPU_PASS_EVIDENCE_OUTPUT_DIR="$feature_output" \
    BEUTL_GPU_PASS_BASELINE_MANIFEST="$baseline_manifest" \
    BEUTL_GPU_PASS_EVIDENCE_MODE=feature \
    BEUTL_REQUIRE_GPU=1 \
    dotnet run \
        --project "$repo_root/tests/Beutl.Benchmarks/Beutl.Benchmarks.csproj" \
        -c Release \
        -f net10.0 \
        -- \
        feature-visual-export
)

python3 - \
    "$baseline_root" \
    "$feature_output" \
    "$benchmark_manifest" \
    "$test_support" \
    "$benchmark_test" \
    "$provenance_doc" \
    "$acceptance_report" \
    "$feature_code_sha" \
    "$generator_script_sha" \
    "$paired_runner_sha" \
    "$refresh_script_sha" <<'PY'
import hashlib
import json
import os
import pathlib
import re
import sys

baseline_root = pathlib.Path(sys.argv[1])
feature_root = pathlib.Path(sys.argv[2])
benchmark_path = pathlib.Path(sys.argv[3])
test_support = pathlib.Path(sys.argv[4])
benchmark_test = pathlib.Path(sys.argv[5])
provenance_doc = pathlib.Path(sys.argv[6])
acceptance_report = pathlib.Path(sys.argv[7])
expected_feature_code_sha = sys.argv[8]
generator_script_sha = sys.argv[9]
paired_runner_sha = sys.argv[10]
refresh_script_sha = sys.argv[11]

published = {
    "geometry-stroke": "geometry-stroke.rgba16f",
    "scene3d-with-2d-tail": "scene3d-with-2d-tail.rgba16f",
    "split-expansion": "split-expansion.rgba16f",
}
expected_feature_hashes = {
    "geometry-stroke.rgba16f": "37e7c40d349c52a1a9bb8a7bec12e838e9b1ca2565b902230bc9262a8317ee45",
    "scene3d-with-2d-tail.rgba16f": "8908d30de25b882368b3d9f7e3d355c783ef5f0026b10f1c108e577f067331f6",
    "split-expansion.rgba16f": "028a6a61e1aa448a8d11337ccd6d0d73652e621507ebe71bd744e9ede814e8fc",
}
source_provenance_field = "beutlEngineAssemblyVersion"

def load_json(path, label):
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f"{label} cannot be read: {error}") from error
    if not isinstance(value, dict):
        raise SystemExit(f"{label} root must be an object")
    return value

def sha256(data):
    return hashlib.sha256(data).hexdigest()

def replace_once(text, pattern, replacement, label):
    result, count = re.subn(pattern, replacement, text, count=1)
    if count != 1:
        raise SystemExit(f"Could not update exactly one {label}")
    return result

baseline_path = baseline_root / "manifest.json"
feature_path = feature_root / "manifest.json"
old_manifest_hash = sha256(baseline_path.read_bytes())
baseline = load_json(baseline_path, "target baseline manifest")
feature = load_json(feature_path, "feature manifest")
benchmark = load_json(benchmark_path, "target benchmark manifest")
if benchmark.get("visualManifestSha256") != old_manifest_hash:
    raise SystemExit("Target benchmark manifest does not reference the current visual manifest")
if feature.get("featureCodeSha") != expected_feature_code_sha:
    raise SystemExit("Feature manifest code SHA does not match the clean repository HEAD")

if baseline.get("pixelFormat") != feature.get("pixelFormat"):
    raise SystemExit("Feature and target pixel formats differ")

baseline_tools = baseline.get("evidenceTools")
benchmark_tools = benchmark.get("evidenceTools")
if not isinstance(baseline_tools, dict) or not isinstance(benchmark_tools, dict):
    raise SystemExit("An evidence-tool hash table is missing")
baseline_tools["generatorScriptSha256"] = generator_script_sha
benchmark_tools["generatorScriptSha256"] = generator_script_sha
baseline_tools["pairedRunnerSha256"] = paired_runner_sha
benchmark_tools["pairedRunnerSha256"] = paired_runner_sha
baseline_tools["refreshScriptSha256"] = refresh_script_sha
benchmark_tools["refreshScriptSha256"] = refresh_script_sha

baseline_fingerprint = baseline.get("fingerprint")
feature_fingerprint = feature.get("fingerprint")
if not isinstance(baseline_fingerprint, dict) or not isinstance(feature_fingerprint, dict):
    raise SystemExit("An evidence fingerprint is missing")
if set(baseline_fingerprint) != set(feature_fingerprint):
    raise SystemExit("Feature and target fingerprint schemas differ")
feature_assembly_version = feature_fingerprint.get(source_provenance_field)
if not isinstance(feature_assembly_version, str) or expected_feature_code_sha.lower() not in feature_assembly_version.lower():
    raise SystemExit("Feature assembly provenance does not contain the clean repository HEAD")
environment_fields = set(baseline_fingerprint) - {source_provenance_field}
mismatches = sorted(
    name for name in environment_fields
    if baseline_fingerprint[name] != feature_fingerprint[name]
)
if mismatches:
    raise SystemExit(
        "Feature render does not match the frozen environment fingerprint: "
        + ", ".join(mismatches)
    )

baseline_scenes = {
    scene["id"]: scene
    for scene in baseline.get("scenes", [])
    if isinstance(scene, dict) and isinstance(scene.get("id"), str)
}
feature_scenes = {
    scene["id"]: scene
    for scene in feature.get("scenes", [])
    if isinstance(scene, dict) and isinstance(scene.get("id"), str)
}
if set(baseline_scenes) != set(feature_scenes):
    raise SystemExit("Feature and target scene sets differ")

semantic_fields = (
    "category",
    "role",
    "controlSceneId",
    "blob",
    "blobWidth",
    "blobHeight",
    "logicalWidth",
    "logicalHeight",
    "outputScale",
    "maxWorkingScale",
    "requestedRegion",
    "empty",
    "parameters",
)
for scene_id in published:
    target_scene = baseline_scenes[scene_id]
    live_scene = feature_scenes[scene_id]
    for field in semantic_fields:
        if target_scene.get(field) != live_scene.get(field):
            raise SystemExit(f"Scene contract differs at {scene_id}.{field}")

feature_hashes = feature.get("artifactSha256")
target_hashes = baseline.get("artifactSha256")
if not isinstance(feature_hashes, dict) or not isinstance(target_hashes, dict):
    raise SystemExit("An artifact hash table is missing")

published_payloads = {}
for scene_id, blob_name in published.items():
    payload = (feature_root / blob_name).read_bytes()
    live_scene = feature_scenes[scene_id]
    expected_length = int(live_scene["blobWidth"]) * int(live_scene["blobHeight"]) * 8
    if len(payload) != expected_length:
        raise SystemExit(f"Feature artifact length mismatch: {blob_name}")
    digest = sha256(payload)
    if digest != feature_hashes.get(blob_name):
        raise SystemExit(f"Feature artifact hash mismatch: {blob_name}")
    if digest != expected_feature_hashes[blob_name]:
        raise SystemExit(
            f"Feature artifact is not the approved S4 payload: {blob_name} "
            f"(expected {expected_feature_hashes[blob_name]}, found {digest})"
        )
    published_payloads[blob_name] = payload
    target_hashes[blob_name] = digest
    baseline_scenes[scene_id]["nonVacuity"] = live_scene.get("nonVacuity")

for blob_name, expected_hash in target_hashes.items():
    payload = published_payloads.get(blob_name)
    if payload is None:
        payload = (baseline_root / blob_name).read_bytes()
    if sha256(payload) != expected_hash:
        raise SystemExit(f"Candidate target artifact hash mismatch: {blob_name}")

manifest_text = json.dumps(baseline, indent=2, ensure_ascii=True) + "\n"
manifest_bytes = manifest_text.encode("utf-8")
manifest_hash = sha256(manifest_bytes)

benchmark["visualManifestSha256"] = manifest_hash
benchmark_text = json.dumps(benchmark, indent=2, ensure_ascii=True) + "\n"
benchmark_bytes = benchmark_text.encode("utf-8")
benchmark_hash = sha256(benchmark_bytes)

support_text = test_support.read_text(encoding="utf-8")
support_text = replace_once(
    support_text,
    r'(ExpectedManifestSha256 = ")[0-9a-f]{64}(")',
    rf"\g<1>{manifest_hash}\g<2>",
    "manifest trust anchor",
)

benchmark_test_text = benchmark_test.read_text(encoding="utf-8")
benchmark_test_text = replace_once(
    benchmark_test_text,
    r'(ExpectedTargetBenchmarkManifestSha256\s*=\s*")[0-9a-f]{64}(";)',
    rf"\g<1>{benchmark_hash}\g<2>",
    "benchmark manifest test trust anchor",
)

provenance_text = provenance_doc.read_text(encoding="utf-8")
provenance_text = replace_once(
    provenance_text,
    r'(\| Visual manifest \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{manifest_hash}\g<2>",
    "documented manifest hash",
)
provenance_text = replace_once(
    provenance_text,
    r'(\| Benchmark manifest \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{benchmark_hash}\g<2>",
    "documented benchmark manifest hash",
)
provenance_text = replace_once(
    provenance_text,
    r'(\| Generator script \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{generator_script_sha}\g<2>",
    "documented generator script hash",
)
provenance_text = replace_once(
    provenance_text,
    r'(\| Paired visual runner \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{paired_runner_sha}\g<2>",
    "documented paired runner hash",
)
provenance_text = replace_once(
    provenance_text,
    r'(\| Intentional refresh script \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{refresh_script_sha}\g<2>",
    "documented refresh script hash",
)

acceptance_text = acceptance_report.read_text(encoding="utf-8")
acceptance_text = replace_once(
    acceptance_text,
    r'(\| `generate-target-baseline\.sh` \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{generator_script_sha}\g<2>",
    "acceptance generator script hash",
)
acceptance_text = replace_once(
    acceptance_text,
    r'(\| `run-paired-visual-evidence\.sh` \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{paired_runner_sha}\g<2>",
    "acceptance paired runner hash",
)
acceptance_text = replace_once(
    acceptance_text,
    r'(\| `refresh-intentional-visual-baselines\.sh` \| `)[0-9a-f]{64}(` \|)',
    rf"\g<1>{refresh_script_sha}\g<2>",
    "acceptance refresh script hash",
)
acceptance_text = replace_once(
    acceptance_text,
    r'(The current immutable trust-chain anchors are target visual manifest\s+`)[0-9a-f]{64}(`)',
    rf"\g<1>{manifest_hash}\g<2>",
    "acceptance visual manifest trust anchor",
)
acceptance_text = replace_once(
    acceptance_text,
    r'(target benchmark manifest\s+`)[0-9a-f]{64}(`\.)',
    rf"\g<1>{benchmark_hash}\g<2>",
    "acceptance benchmark manifest trust anchor",
)

staged = []
try:
    for blob_name, payload in published_payloads.items():
        path = baseline_root / f".{blob_name}.refresh"
        path.write_bytes(payload)
        staged.append((path, baseline_root / blob_name))

    manifest_stage = baseline_root / ".manifest.json.refresh"
    manifest_stage.write_bytes(manifest_bytes)
    staged.append((manifest_stage, baseline_path))

    benchmark_stage = benchmark_path.with_name(".manifest.json.refresh")
    benchmark_stage.write_bytes(benchmark_bytes)
    staged.append((benchmark_stage, benchmark_path))

    support_stage = test_support.with_name(f".{test_support.name}.refresh")
    support_stage.write_text(support_text, encoding="utf-8")
    staged.append((support_stage, test_support))

    benchmark_test_stage = benchmark_test.with_name(f".{benchmark_test.name}.refresh")
    benchmark_test_stage.write_text(benchmark_test_text, encoding="utf-8")
    staged.append((benchmark_test_stage, benchmark_test))

    provenance_stage = provenance_doc.with_name(f".{provenance_doc.name}.refresh")
    provenance_stage.write_text(provenance_text, encoding="utf-8")
    staged.append((provenance_stage, provenance_doc))

    acceptance_stage = acceptance_report.with_name(f".{acceptance_report.name}.refresh")
    acceptance_stage.write_text(acceptance_text, encoding="utf-8")
    staged.append((acceptance_stage, acceptance_report))

    for source, destination in staged:
        os.replace(source, destination)
finally:
    for source, _ in staged:
        source.unlink(missing_ok=True)

print("Published the closed S4 baseline payload set:")
for scene_id, blob_name in published.items():
    disposition = "approved semantic refresh" if scene_id == "scene3d-with-2d-tail" else "restored legacy"
    print(f"  {scene_id}: {blob_name} {target_hashes[blob_name]} ({disposition})")
print(f"Updated manifest trust anchor: {manifest_hash}")
print(f"Updated benchmark evidence linkage: {benchmark_hash}")
print(f"Generator script SHA-256: {generator_script_sha}")
print(f"Paired runner SHA-256: {paired_runner_sha}")
print(f"Refresh script SHA-256: {refresh_script_sha}")
PY

printf '%s\n' \
    'Selective visual-baseline refresh completed.' \
    'Review the two legacy restorations, one approved refresh, manifest fields, trust anchors, and provenance table before committing.'
