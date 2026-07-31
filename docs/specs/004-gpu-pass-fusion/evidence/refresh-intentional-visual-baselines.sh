#!/usr/bin/env bash

set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(git -C "$script_dir" rev-parse --show-toplevel)
baseline_root="$script_dir/target-baseline"
baseline_manifest="$baseline_root/manifest.json"
benchmark_manifest="$script_dir/target-benchmark/manifest.json"
test_support="$repo_root/tests/Beutl.UnitTests/Engine/Graphics/Rendering/Baseline/GpuPassFusionBaselineTestSupport.cs"
provenance_doc="$script_dir/target-baseline.md"

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
refresh_script="$script_dir/$(basename -- "$0")"
refresh_script_relative=${refresh_script#"$repo_root"/}
git -C "$repo_root" ls-files --error-unmatch -- "$refresh_script_relative" >/dev/null || {
    printf 'Visual-baseline refresh script is not tracked: %s\n' "$refresh_script" >&2
    exit 1
}
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
    "$provenance_doc" \
    "$feature_code_sha" \
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
provenance_doc = pathlib.Path(sys.argv[5])
expected_feature_code_sha = sys.argv[6]
refresh_script_sha = sys.argv[7]

selected = {
    "geometry-stroke": "geometry-stroke.rgba16f",
    "scene3d-with-2d-tail": "scene3d-with-2d-tail.rgba16f",
    "split-expansion": "split-expansion.rgba16f",
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
for scene_id in selected:
    target_scene = baseline_scenes[scene_id]
    live_scene = feature_scenes[scene_id]
    for field in semantic_fields:
        if target_scene.get(field) != live_scene.get(field):
            raise SystemExit(f"Scene contract differs at {scene_id}.{field}")

feature_hashes = feature.get("artifactSha256")
target_hashes = baseline.get("artifactSha256")
if not isinstance(feature_hashes, dict) or not isinstance(target_hashes, dict):
    raise SystemExit("An artifact hash table is missing")

selected_payloads = {}
for scene_id, blob_name in selected.items():
    payload = (feature_root / blob_name).read_bytes()
    live_scene = feature_scenes[scene_id]
    expected_length = int(live_scene["blobWidth"]) * int(live_scene["blobHeight"]) * 8
    if len(payload) != expected_length:
        raise SystemExit(f"Feature artifact length mismatch: {blob_name}")
    digest = sha256(payload)
    if digest != feature_hashes.get(blob_name):
        raise SystemExit(f"Feature artifact hash mismatch: {blob_name}")
    selected_payloads[blob_name] = payload
    target_hashes[blob_name] = digest
    baseline_scenes[scene_id]["nonVacuity"] = live_scene.get("nonVacuity")

for blob_name, expected_hash in target_hashes.items():
    payload = selected_payloads.get(blob_name)
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

staged = []
try:
    for blob_name, payload in selected_payloads.items():
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

    provenance_stage = provenance_doc.with_name(f".{provenance_doc.name}.refresh")
    provenance_stage.write_text(provenance_text, encoding="utf-8")
    staged.append((provenance_stage, provenance_doc))

    for source, destination in staged:
        os.replace(source, destination)
finally:
    for source, _ in staged:
        source.unlink(missing_ok=True)

print("Refreshed exactly these intended-semantic artifacts:")
for scene_id, blob_name in selected.items():
    print(f"  {scene_id}: {blob_name} {target_hashes[blob_name]}")
print(f"Updated manifest trust anchor: {manifest_hash}")
print(f"Updated benchmark evidence linkage: {benchmark_hash}")
print(f"Refresh script SHA-256: {refresh_script_sha}")
PY

printf '%s\n' \
    'Selective visual-baseline refresh completed.' \
    'Review the three blob changes, manifest fields, trust anchor, and provenance table before committing.'
