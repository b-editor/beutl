#!/usr/bin/env bash

set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
target_generator="$script_dir/generate-target-baseline.sh"
committed_target_root="$script_dir/target-baseline"
feature_worktree=""
feature_command=${BEUTL_GPU_PASS_FEATURE_EXPORT_COMMAND:-}
result_root=""
runner_worktree=""

usage() {
    cat >&2 <<EOF
Usage: $0 --feature-worktree <path> --output-dir <create-only-directory> [--feature-command <shell-command>]

The feature command runs in the feature worktree and must create manifest.json and
row-packed *.rgba16f files in \$BEUTL_GPU_PASS_EVIDENCE_OUTPUT_DIR. It receives:
  BEUTL_GPU_PASS_EVIDENCE_OUTPUT_DIR
  BEUTL_GPU_PASS_TARGET_OUTPUT_DIR
  BEUTL_GPU_PASS_BASELINE_MANIFEST
  BEUTL_GPU_PASS_EVIDENCE_MODE=feature
  BEUTL_REQUIRE_GPU=1

The command may instead be supplied through BEUTL_GPU_PASS_FEATURE_EXPORT_COMMAND.
EOF
}

require_clean_worktree() {
    local path=$1
    local label=$2
    local status
    status=$(git -C "$path" status --porcelain=v1 --untracked-files=all)
    [[ -z $status ]] || {
        printf '%s worktree must remain clean: %s\n%s\n' "$label" "$path" "$status" >&2
        exit 1
    }
}

while (( $# > 0 )); do
    case "$1" in
        --feature-worktree)
            (( $# >= 2 )) || { usage; exit 2; }
            feature_worktree=$2
            shift 2
            ;;
        --feature-command)
            (( $# >= 2 )) || { usage; exit 2; }
            feature_command=$2
            shift 2
            ;;
        --output-dir)
            (( $# >= 2 )) || { usage; exit 2; }
            result_root=$2
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            usage
            exit 2
            ;;
    esac
done

[[ -n $feature_worktree && -n $result_root && -n $feature_command ]] || {
    usage
    exit 2
}
[[ -x $target_generator ]] || {
    printf 'Target generator is missing or not executable: %s\n' "$target_generator" >&2
    exit 1
}
[[ -f $committed_target_root/manifest.json ]] || {
    printf 'Committed target baseline manifest is missing: %s/manifest.json\n' "$committed_target_root" >&2
    exit 1
}
for command_name in git python3 bash; do
    command -v "$command_name" >/dev/null 2>&1 || {
        printf 'Required command is unavailable: %s\n' "$command_name" >&2
        exit 1
    }
done

runner_worktree=$(git -C "$script_dir" rev-parse --show-toplevel)
require_clean_worktree "$runner_worktree" "Evidence tools before paired visual capture"
feature_worktree=$(git -C "$feature_worktree" rev-parse --show-toplevel)
feature_sha=$(git -C "$feature_worktree" rev-parse HEAD)
require_clean_worktree "$feature_worktree" "Feature before paired visual capture"
result_root=$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$result_root")
[[ ! -e $result_root ]] || {
    printf 'Create-only paired result directory already exists: %s\n' "$result_root" >&2
    exit 1
}
mkdir -p "$(dirname -- "$result_root")"
mkdir "$result_root"

target_output="$result_root/target"
feature_output="$result_root/feature"
reconciliation_record="$result_root/.semantic-refresh-reconciliation.json"

"$target_generator" --output-dir "$target_output"

python3 - "$target_output" "$committed_target_root" "$reconciliation_record" <<'PY'
import hashlib
import json
import pathlib
import shutil
import sys

regenerated_root = pathlib.Path(sys.argv[1])
committed_root = pathlib.Path(sys.argv[2])
record_path = pathlib.Path(sys.argv[3])

# Approved explicitly by docs/specs/004-gpu-pass-fusion/research.md; never infer
# this set from artifact hash differences because that would self-approve regressions.
APPROVED_SEMANTIC_REFRESH_SCENE_IDS = (
    "scene3d-with-2d-tail",
)

def sha256(payload):
    return hashlib.sha256(payload).hexdigest()

def load_and_validate(root, label):
    manifest_path = root / "manifest.json"
    try:
        manifest_bytes = manifest_path.read_bytes()
        manifest = json.loads(manifest_bytes)
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f"{label} manifest cannot be read: {error}") from error
    if not isinstance(manifest, dict):
        raise SystemExit(f"{label} manifest root must be an object")

    hashes = manifest.get("artifactSha256")
    scenes = manifest.get("scenes")
    if not isinstance(hashes, dict) or not hashes or not isinstance(scenes, list):
        raise SystemExit(f"{label} artifact or scene table is missing")
    expected_files = {"manifest.json", *hashes}
    actual_files = {path.name for path in root.iterdir() if path.is_file()}
    if actual_files != expected_files:
        raise SystemExit(f"{label} file set differs from its manifest")

    scene_by_id = {}
    blob_to_scene_id = {}
    for scene in scenes:
        if not isinstance(scene, dict):
            raise SystemExit(f"{label} contains a non-object scene")
        scene_id = scene.get("id")
        if not isinstance(scene_id, str) or not scene_id or scene_id in scene_by_id:
            raise SystemExit(f"{label} has a missing or duplicate scene id")
        scene_by_id[scene_id] = scene
        blob = scene.get("blob")
        if blob is not None:
            if not isinstance(blob, str) or blob in blob_to_scene_id:
                raise SystemExit(f"{label} has an invalid or duplicate scene blob: {scene_id}")
            blob_to_scene_id[blob] = scene_id
    if set(blob_to_scene_id) != set(hashes):
        raise SystemExit(f"{label} scene blob set differs from its artifact hash table")

    payloads = {}
    for name, expected_hash in hashes.items():
        if (not isinstance(name, str) or "/" in name or "\\" in name
                or not name.endswith(".rgba16f")):
            raise SystemExit(f"{label} contains an unsafe artifact name: {name}")
        path = root / name
        try:
            payload = path.read_bytes()
        except OSError as error:
            raise SystemExit(f"{label} artifact cannot be read: {name}: {error}") from error
        if sha256(payload) != expected_hash:
            raise SystemExit(f"{label} artifact hash mismatch: {name}")
        payloads[name] = payload
    return manifest, manifest_bytes, hashes, scene_by_id, payloads

(
    regenerated,
    regenerated_manifest_bytes,
    regenerated_hashes,
    regenerated_scenes,
    regenerated_payloads,
) = load_and_validate(regenerated_root, "regenerated target")
(
    committed,
    committed_manifest_bytes,
    committed_hashes,
    committed_scenes,
    committed_payloads,
) = load_and_validate(committed_root, "committed target baseline")

regenerated_tools = regenerated.get("evidenceTools")
committed_tools = committed.get("evidenceTools")
if not isinstance(regenerated_tools, dict) or not isinstance(committed_tools, dict):
    raise SystemExit("Regenerated or committed target evidence-tool table is missing")
if regenerated_tools != committed_tools:
    mismatches = sorted(
        name for name in set(regenerated_tools) | set(committed_tools)
        if regenerated_tools.get(name) != committed_tools.get(name)
    )
    raise SystemExit(
        "Regenerated evidence-tool hashes differ from the committed target baseline: "
        + ", ".join(mismatches))

if set(regenerated_scenes) != set(committed_scenes):
    raise SystemExit("Regenerated and committed target scene-id sets differ")
if set(regenerated_hashes) != set(committed_hashes):
    raise SystemExit("Regenerated and committed target artifact sets differ")
for scene_id in sorted(regenerated_scenes):
    if regenerated_scenes[scene_id].get("blob") != committed_scenes[scene_id].get("blob"):
        raise SystemExit(f"Regenerated and committed target scene blobs differ: {scene_id}")

approved_blobs = {}
for scene_id in APPROVED_SEMANTIC_REFRESH_SCENE_IDS:
    regenerated_scene = regenerated_scenes.get(scene_id)
    committed_scene = committed_scenes.get(scene_id)
    if regenerated_scene is None or committed_scene is None:
        raise SystemExit(f"Approved semantic-refresh scene is missing: {scene_id}")
    regenerated_blob = regenerated_scene.get("blob")
    committed_blob = committed_scene.get("blob")
    if not isinstance(regenerated_blob, str) or regenerated_blob != committed_blob:
        raise SystemExit(f"Approved semantic-refresh scene blob differs: {scene_id}")
    approved_blobs[regenerated_blob] = scene_id
if len(approved_blobs) != len(APPROVED_SEMANTIC_REFRESH_SCENE_IDS):
    raise SystemExit("Approved semantic-refresh scenes do not map to distinct artifacts")

for blob_name in sorted(regenerated_hashes):
    if blob_name not in approved_blobs and regenerated_payloads[blob_name] != committed_payloads[blob_name]:
        scene_id = next(
            scene["id"] for scene in regenerated_scenes.values() if scene.get("blob") == blob_name)
        raise SystemExit(
            f"Unapproved target artifact differs from the committed baseline: {scene_id} ({blob_name})")

refresh_records = []
for blob_name, scene_id in sorted(approved_blobs.items(), key=lambda item: item[1]):
    regenerated_scene = regenerated_scenes[scene_id]
    committed_scene = committed_scenes[scene_id]
    legacy_hash = sha256(regenerated_payloads[blob_name])
    refreshed_hash = committed_hashes[blob_name]
    if refreshed_hash == legacy_hash:
        raise SystemExit(
            f"Approved semantic refresh is absent for {scene_id}; run "
            "docs/specs/004-gpu-pass-fusion/evidence/"
            "refresh-intentional-visual-baselines.sh before paired evidence")
    shutil.copyfile(committed_root / blob_name, regenerated_root / blob_name)
    regenerated_hashes[blob_name] = refreshed_hash
    regenerated_scene["nonVacuity"] = committed_scene["nonVacuity"]
    refresh_records.append({
        "sceneId": scene_id,
        "artifact": blob_name,
        "legacyArtifactSha256": legacy_hash,
        "refreshedArtifactSha256": refreshed_hash,
    })

(regenerated_root / "manifest.json").write_text(
    json.dumps(regenerated, indent=2, ensure_ascii=True) + "\n",
    encoding="utf-8",
)
record = {
    "schemaVersion": 1,
    "regeneratedTargetManifestSha256": sha256(regenerated_manifest_bytes),
    "committedTargetBaselineManifestSha256": sha256(committed_manifest_bytes),
    "artifacts": refresh_records,
}
record_path.write_text(
    json.dumps(record, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
print("Reconciled approved semantic refreshes from the committed target baseline")
PY

BEUTL_GPU_PASS_EVIDENCE_OUTPUT_DIR="$feature_output" \
BEUTL_GPU_PASS_TARGET_OUTPUT_DIR="$target_output" \
BEUTL_GPU_PASS_BASELINE_MANIFEST="$target_output/manifest.json" \
BEUTL_GPU_PASS_EVIDENCE_MODE=feature \
BEUTL_REQUIRE_GPU=1 \
bash -c 'cd "$1" && exec bash -c "$2"' bash "$feature_worktree" "$feature_command"

require_clean_worktree "$feature_worktree" "Feature after visual export"
require_clean_worktree "$runner_worktree" "Evidence tools after visual export"

[[ -f $feature_output/manifest.json ]] || {
    printf 'Feature exporter did not create %s/manifest.json\n' "$feature_output" >&2
    exit 1
}

FEATURE_SHA="$feature_sha" FEATURE_COMMAND="$feature_command" \
python3 - "$target_output" "$feature_output" "$result_root/paired-result.json" \
    "$reconciliation_record" <<'PY'
import datetime
import hashlib
import json
import math
import os
import pathlib
import struct
import sys

target_root = pathlib.Path(sys.argv[1])
feature_root = pathlib.Path(sys.argv[2])
result_path = pathlib.Path(sys.argv[3])
reconciliation_path = pathlib.Path(sys.argv[4])

required_environment_fingerprint_fields = {
    "osDescription", "osVersion", "osBuild", "osArchitecture", "processArchitecture",
    "runtimeIdentifier", "frameworkDescription", "environmentVersion", "rendererBackend",
    "skiaBackend", "deviceSelection", "vulkanApiVersion", "vulkanVendorId", "vulkanDeviceId",
    "vulkanDeviceType", "vulkanDeviceName", "vulkanDeviceUuid", "vulkanDriverUuid",
    "vulkanDriverId", "vulkanDriverName", "vulkanDriverInfo", "vulkanDriverVersionRaw",
    "vulkanDriverVersionDecoded", "vulkanEnabledExtensions", "metalDeviceName", "metalRegistryId",
    "metalFeatureFamily", "metalDriver", "skiaSharpManagedVersion", "skiaSharpNativeVersion",
    "silkNetVulkanVersion",
}
source_provenance_field = "beutlEngineAssemblyVersion"
required_fingerprint_fields = required_environment_fingerprint_fields | {source_provenance_field}

def load_manifest(root, label):
    path = root / "manifest.json"
    if not path.is_file():
        raise SystemExit(f"{label} manifest is missing: {path}")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f"{label} manifest cannot be read: {error}") from error
    if not isinstance(value, dict):
        raise SystemExit(f"{label} manifest root must be an object")
    return value

def validate_fingerprint(manifest, label):
    fingerprint = manifest.get("fingerprint")
    if not isinstance(fingerprint, dict):
        raise SystemExit(f"{label} fingerprint is missing")
    missing = required_fingerprint_fields - set(fingerprint)
    extra = set(fingerprint) - required_fingerprint_fields
    if missing or extra:
        raise SystemExit(
            f"{label} fingerprint schema mismatch; missing={sorted(missing)}, extra={sorted(extra)}")
    for name in sorted(required_fingerprint_fields):
        value = fingerprint[name]
        values = value if isinstance(value, list) else [value]
        if not values or any(
            not isinstance(item, str) or not item.strip() or "unknown" in item.lower()
            for item in values
        ):
            raise SystemExit(f"{label} fingerprint field is missing or unknown: {name}")
    return fingerprint

target = load_manifest(target_root, "target")
feature = load_manifest(feature_root, "feature")
target_fingerprint = validate_fingerprint(target, "target")
feature_fingerprint = validate_fingerprint(feature, "feature")
target_code_sha = target.get("baselineCodeSha")
feature_code_sha = os.environ["FEATURE_SHA"]
feature_manifest_code_sha = feature.get("featureCodeSha")
feature_exporter_assembly_version = feature.get("exporterAssemblyVersion")
if not isinstance(target_code_sha, str) or target_code_sha not in target_fingerprint[source_provenance_field]:
    raise SystemExit("Target engine assembly provenance does not contain baselineCodeSha")
if feature_code_sha not in feature_fingerprint[source_provenance_field]:
    raise SystemExit("Feature engine assembly provenance does not contain the feature worktree SHA")
if feature_manifest_code_sha != feature_code_sha:
    raise SystemExit("Feature exporter manifest featureCodeSha does not match the feature worktree SHA")
if (not isinstance(feature_exporter_assembly_version, str)
        or feature_code_sha not in feature_exporter_assembly_version):
    raise SystemExit("Feature exporter assembly provenance does not contain the feature worktree SHA")

target_environment_fingerprint = {
    name: target_fingerprint[name] for name in required_environment_fingerprint_fields
}
feature_environment_fingerprint = {
    name: feature_fingerprint[name] for name in required_environment_fingerprint_fields
}

# Code identity is checked independently above: target and feature assembly versions
# must differ when their commits differ. This gate compares only the execution
# environment and deliberately runs before artifact decoding or any parity metric.
if target_environment_fingerprint != feature_environment_fingerprint:
    mismatches = [
        name for name in sorted(required_environment_fingerprint_fields)
        if target_fingerprint[name] != feature_fingerprint[name]
    ]
    raise SystemExit("Evidence environment fingerprint mismatch before parity comparison: " + ", ".join(mismatches))
print("Exact evidence environment fingerprint gate passed before parity comparison")

def validate_artifacts(root, manifest, label):
    hashes = manifest.get("artifactSha256")
    scenes = manifest.get("scenes")
    if not isinstance(hashes, dict) or not hashes or not isinstance(scenes, list):
        raise SystemExit(f"{label} artifact or scene table is missing")
    allowed = {"manifest.json", *hashes.keys()}
    actual = {path.name for path in root.iterdir() if path.is_file()}
    if actual != allowed:
        raise SystemExit(f"{label} file set differs from its manifest")
    for name, expected in hashes.items():
        if "/" in name or "\\" in name or not name.endswith(".rgba16f"):
            raise SystemExit(f"{label} contains an unsafe artifact name: {name}")
        path = root / name
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise SystemExit(f"{label} artifact hash mismatch: {name}")
    by_id = {}
    for scene in scenes:
        scene_id = scene.get("id")
        if not isinstance(scene_id, str) or not scene_id or scene_id in by_id:
            raise SystemExit(f"{label} has a missing or duplicate scene id")
        by_id[scene_id] = scene
        blob = scene.get("blob")
        if blob is not None:
            if blob not in hashes:
                raise SystemExit(f"{label} scene references an unhashed blob: {scene_id}")
            expected_length = int(scene["blobWidth"]) * int(scene["blobHeight"]) * 8
            if (root / blob).stat().st_size != expected_length:
                raise SystemExit(f"{label} blob length mismatch: {scene_id}")
    return by_id

target_scenes = validate_artifacts(target_root, target, "target")
feature_scenes = validate_artifacts(feature_root, feature, "feature")
if set(target_scenes) != set(feature_scenes):
    raise SystemExit("Target and feature scene-id sets differ")
if target.get("pixelFormat") != feature.get("pixelFormat"):
    raise SystemExit("Target and feature pixel formats differ")

try:
    reconciliation = json.loads(reconciliation_path.read_text(encoding="utf-8"))
except (OSError, json.JSONDecodeError) as error:
    raise SystemExit(f"Semantic-refresh reconciliation record cannot be read: {error}") from error
refresh_artifacts = reconciliation.get("artifacts")
if not isinstance(refresh_artifacts, list) or len(refresh_artifacts) != 1:
    raise SystemExit("Semantic-refresh reconciliation record must contain exactly one artifact")
refresh_scene_ids = set()
for item in refresh_artifacts:
    if not isinstance(item, dict):
        raise SystemExit("Semantic-refresh reconciliation contains a non-object artifact")
    scene_id = item.get("sceneId")
    blob = item.get("artifact")
    legacy_hash = item.get("legacyArtifactSha256")
    refreshed_hash = item.get("refreshedArtifactSha256")
    if not isinstance(scene_id, str) or scene_id in refresh_scene_ids or scene_id not in target_scenes:
        raise SystemExit("Semantic-refresh reconciliation contains an invalid scene id")
    refresh_scene_ids.add(scene_id)
    if target_scenes[scene_id].get("blob") != blob:
        raise SystemExit(f"Semantic-refresh artifact does not match its target scene: {scene_id}")
    if target.get("artifactSha256", {}).get(blob) != refreshed_hash:
        raise SystemExit(f"Semantic-refresh hash does not match the reconciled target: {scene_id}")
    if legacy_hash == refreshed_hash:
        raise SystemExit(f"Semantic-refresh record has identical legacy and refreshed hashes: {scene_id}")
if refresh_scene_ids != {"scene3d-with-2d-tail"}:
    raise SystemExit("Semantic-refresh reconciliation must contain only scene3d-with-2d-tail")

semantic_fields = (
    "category", "role", "controlSceneId", "blobWidth", "blobHeight", "logicalWidth",
    "logicalHeight", "outputScale", "maxWorkingScale", "requestedRegion", "empty", "parameters",
)
# The query record carries pipeline-specific advisory keys on each side (the legacy
# generator notes pull execution, the feature exporter notes deferred work), so only
# the shared measured semantics are compared.
query_semantic_fields = ("bounds", "insidePoint", "insideHit", "outsidePoint", "outsideHit")
for scene_id, target_scene in target_scenes.items():
    feature_scene = feature_scenes[scene_id]
    for name in semantic_fields:
        if target_scene.get(name) != feature_scene.get(name):
            raise SystemExit(f"Scene parameter mismatch before parity comparison: {scene_id}.{name}")
    target_query = target_scene.get("query")
    feature_query = feature_scene.get("query")
    if (target_query is None) != (feature_query is None):
        raise SystemExit(f"Scene query presence differs: {scene_id}")
    if target_query is not None:
        for name in query_semantic_fields:
            if target_query.get(name) != feature_query.get(name):
                raise SystemExit(f"Scene parameter mismatch before parity comparison: {scene_id}.query.{name}")
    if (target_scene.get("blob") is None) != (feature_scene.get("blob") is None):
        raise SystemExit(f"Scene blob presence differs: {scene_id}")

def allocation_failures_by_intent(manifest, label):
    records = manifest.get("allocationFailures")
    if not isinstance(records, list) or len(records) != 2:
        raise SystemExit(f"{label} allocation-failure evidence must contain exactly two records")
    by_intent = {}
    for record in records:
        if not isinstance(record, dict):
            raise SystemExit(f"{label} allocation-failure evidence contains a non-object record")
        intent = record.get("intent")
        if intent not in {"preview", "delivery"} or intent in by_intent:
            raise SystemExit(f"{label} allocation-failure evidence has an invalid or duplicate intent")
        by_intent[intent] = record
    return by_intent

target_failures = allocation_failures_by_intent(target, "target")
feature_failures = allocation_failures_by_intent(feature, "feature")
allocation_semantic_fields = (
    "intent", "injectionPoint", "maxWorkingScale", "outcome", "exceptionType",
)
for intent, target_failure in target_failures.items():
    feature_failure = feature_failures[intent]
    for name in allocation_semantic_fields:
        if target_failure.get(name) != feature_failure.get(name):
            raise SystemExit(f"Allocation-failure outcome mismatch: {intent}.{name}")
    target_message = target_failure.get("exceptionMessage")
    feature_message = feature_failure.get("exceptionMessage")
    if (target_message is None) != (feature_message is None):
        raise SystemExit(f"Allocation-failure exception-message presence differs: {intent}")

def decode_rgba16f(path, width, height):
    data = path.read_bytes()
    expected = width * height * 8
    if len(data) != expected:
        raise SystemExit(f"RGBA16F payload length mismatch: {path}")
    values = [item[0] for item in struct.iter_unpack("<e", data)]
    if any(not math.isfinite(value) for value in values):
        raise SystemExit(f"RGBA16F payload contains NaN or infinity: {path}")
    return values

def selected_pixels(width, height, region=None):
    if region is None:
        left, top, right, bottom = 0, 0, width, height
    else:
        left, top = region[0], region[1]
        right, bottom = left + region[2], top + region[3]
        if left < 0 or top < 0 or right > width or bottom > height or left >= right or top >= bottom:
            raise SystemExit(f"Invalid metric crop: {region} for {width}x{height}")
    return [y * width + x for y in range(top, bottom) for x in range(left, right)]

def metrics(reference, actual, pixels):
    if not pixels:
        raise SystemExit("Metric region selected no pixels")
    rgb_error = alpha_error = 0.0
    luma_reference = []
    luma_actual = []
    for pixel in pixels:
        offset = pixel * 4
        rgb_error += sum(abs(reference[offset + c] - actual[offset + c]) for c in range(3))
        alpha_error += abs(reference[offset + 3] - actual[offset + 3])
        luma_reference.append(
            0.2126 * reference[offset] + 0.7152 * reference[offset + 1] + 0.0722 * reference[offset + 2])
        luma_actual.append(
            0.2126 * actual[offset] + 0.7152 * actual[offset + 1] + 0.0722 * actual[offset + 2])
    count = len(pixels)
    mean_reference = sum(luma_reference) / count
    mean_actual = sum(luma_actual) / count
    variance_reference = sum((value - mean_reference) ** 2 for value in luma_reference) / count
    variance_actual = sum((value - mean_actual) ** 2 for value in luma_actual) / count
    covariance = sum(
        (a - mean_reference) * (b - mean_actual)
        for a, b in zip(luma_reference, luma_actual)
    ) / count
    c1 = 0.01 ** 2
    c2 = 0.03 ** 2
    ssim = (
        (2 * mean_reference * mean_actual + c1) * (2 * covariance + c2)
        / ((mean_reference ** 2 + mean_actual ** 2 + c1) * (variance_reference + variance_actual + c2))
    )
    return {
        "linearLightSsim": ssim,
        "linearRgbMae": rgb_error / (count * 3),
        "alphaMae": alpha_error / count,
    }

WINDOW_SIZE = 16
MINIMUM_WINDOWED_SSIM = 0.95
MAXIMUM_WINDOWED_ALPHA_MAE = 0.02
MAXIMUM_WINDOWED_RGBA_MAE = 0.05

def windowed_metrics(reference, actual, width, height):
    minimum_ssim = 1.0
    maximum_alpha_mae = 0.0
    maximum_rgba_mae = 0.0
    for top in range(0, height, WINDOW_SIZE):
        for left in range(0, width, WINDOW_SIZE):
            region = [
                left,
                top,
                min(WINDOW_SIZE, width - left),
                min(WINDOW_SIZE, height - top),
            ]
            window = metrics(reference, actual, selected_pixels(width, height, region))
            minimum_ssim = min(minimum_ssim, window["linearLightSsim"])
            maximum_alpha_mae = max(maximum_alpha_mae, window["alphaMae"])
            maximum_rgba_mae = max(
                maximum_rgba_mae,
                ((window["linearRgbMae"] * 3.0) + window["alphaMae"]) / 4.0,
            )
    return {
        "minimumSsim": minimum_ssim,
        "maximumAlphaMae": maximum_alpha_mae,
        "maximumRgbaMae": maximum_rgba_mae,
    }

def verify_localized_error_gate():
    size = 128
    reference = []
    actual = []
    for y in range(size):
        for x in range(size):
            value = 1.0 if (x + y) % 2 == 0 else 0.0
            reference.extend((value, value, value, 1.0))
            actual_value = 0.5 if x < 14 and y < 14 else value
            actual.extend((actual_value, actual_value, actual_value, 1.0))
    full = metrics(reference, actual, selected_pixels(size, size))
    if (full["linearLightSsim"] < 0.99
            or full["linearRgbMae"] > 0.02
            or full["alphaMae"] > 0.02):
        raise SystemExit("Localized-error self-test no longer passes the whole-image thresholds")
    localized = windowed_metrics(reference, actual, size, size)
    if localized["minimumSsim"] >= MINIMUM_WINDOWED_SSIM:
        raise SystemExit("Minimum-window SSIM self-test failed to reject a localized defect")

    reference = [0.0, 0.0, 0.0, 1.0] * (size * size)
    actual = []
    for y in range(size):
        for x in range(size):
            actual.extend((0.0, 0.0, 0.0, 0.0 if x < 14 and y < 14 else 1.0))
    full = metrics(reference, actual, selected_pixels(size, size))
    if (full["linearLightSsim"] < 0.99
            or full["linearRgbMae"] > 0.02
            or full["alphaMae"] > 0.02):
        raise SystemExit("Localized-alpha self-test no longer passes the whole-image thresholds")
    localized = windowed_metrics(reference, actual, size, size)
    if (localized["maximumAlphaMae"] <= MAXIMUM_WINDOWED_ALPHA_MAE
            or localized["maximumRgbaMae"] <= MAXIMUM_WINDOWED_RGBA_MAE):
        raise SystemExit("Window-local alpha/RGBA self-test failed to reject a localized defect")

verify_localized_error_gate()

def parse_crop(scene):
    text = (scene.get("parameters") or {}).get("edgeCrop")
    if text is None:
        return None
    try:
        parts = [int(value) for value in text.split(",")]
    except ValueError as error:
        raise SystemExit(f"Invalid edgeCrop for {scene['id']}: {text}") from error
    if len(parts) != 4:
        raise SystemExit(f"Invalid edgeCrop for {scene['id']}: {text}")
    return parts

results = []
for scene_id in sorted(target_scenes):
    target_scene = target_scenes[scene_id]
    feature_scene = feature_scenes[scene_id]
    target_blob = target_scene.get("blob")
    if target_blob is None:
        continue
    feature_blob = feature_scene.get("blob")
    width = int(target_scene["blobWidth"])
    height = int(target_scene["blobHeight"])
    reference = decode_rgba16f(target_root / target_blob, width, height)
    actual = decode_rgba16f(feature_root / feature_blob, width, height)
    full = metrics(reference, actual, selected_pixels(width, height))
    if full["linearLightSsim"] < 0.99 or full["linearRgbMae"] > 0.02 or full["alphaMae"] > 0.02:
        raise SystemExit(f"Full-image parity threshold failed for {scene_id}: {full}")
    windowed = windowed_metrics(reference, actual, width, height)
    if windowed["minimumSsim"] < MINIMUM_WINDOWED_SSIM:
        raise SystemExit(
            f"Minimum-window SSIM parity threshold failed for {scene_id}: {windowed['minimumSsim']}")
    if (windowed["maximumAlphaMae"] > MAXIMUM_WINDOWED_ALPHA_MAE
            or windowed["maximumRgbaMae"] > MAXIMUM_WINDOWED_RGBA_MAE):
        raise SystemExit(
            f"Window-local alpha/RGBA parity threshold failed for {scene_id}: {windowed}")
    scene_result = {
        "sceneId": scene_id,
        "fullImage": {
            **full,
            "minimumWindowedSsim": windowed["minimumSsim"],
            "maximumWindowedAlphaMae": windowed["maximumAlphaMae"],
            "maximumWindowedRgbaMae": windowed["maximumRgbaMae"],
        },
    }

    crop = parse_crop(target_scene)
    if crop is not None:
        crop_pixels = selected_pixels(width, height, crop)
        crop_result = metrics(reference, actual, crop_pixels)
        if (crop_result["linearLightSsim"] < 0.99
                or crop_result["linearRgbMae"] > 0.02
                or crop_result["alphaMae"] > 0.02):
            raise SystemExit(f"AA edge-crop parity threshold failed for {scene_id}: {crop_result}")
        edge_pixels = [pixel for pixel in crop_pixels if 0.0 < reference[pixel * 4 + 3] < 1.0]
        if not edge_pixels:
            raise SystemExit(f"AA reference crop has no nontrivial coverage pixels: {scene_id}")
        edge_sum = 0.0
        edge_maximum = [0.0, 0.0, 0.0, 0.0]
        for pixel in edge_pixels:
            offset = pixel * 4
            for channel in range(4):
                error = abs(reference[offset + channel] - actual[offset + channel])
                edge_sum += error
                edge_maximum[channel] = max(edge_maximum[channel], error)
        edge_mae = edge_sum / (len(edge_pixels) * 4)
        if edge_mae > 0.02 or max(edge_maximum) > 0.02:
            raise SystemExit(
                f"AA coverage-band threshold failed for {scene_id}: MAE={edge_mae}, max={edge_maximum}")
        scene_result["edgeCrop"] = {
            **crop_result,
            "region": crop,
            "coverageBandPixelCount": len(edge_pixels),
            "coverageBandRgbaMae": edge_mae,
            "coverageBandMaximumError": edge_maximum,
            "maximumErrorBound": 0.02,
        }
    results.append(scene_result)

result = {
    "schemaVersion": 1,
    "status": "passed",
    "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "targetCodeSha": target_code_sha,
    "featureCodeSha": feature_code_sha,
    "featureCommand": os.environ["FEATURE_COMMAND"],
    "featureExporterProvenance": {
        "featureCodeSha": feature_manifest_code_sha,
        "exporterAssemblyVersion": feature_exporter_assembly_version,
    },
    "targetManifestSha256": hashlib.sha256((target_root / "manifest.json").read_bytes()).hexdigest(),
    "featureManifestSha256": hashlib.sha256((feature_root / "manifest.json").read_bytes()).hexdigest(),
    "semanticRefresh": {
        "regeneratedTargetManifestSha256": reconciliation["regeneratedTargetManifestSha256"],
        "committedTargetBaselineManifestSha256": reconciliation[
            "committedTargetBaselineManifestSha256"
        ],
        "artifacts": [
            {**item, "parityRanAgainstRefreshedArtifact": True}
            for item in sorted(refresh_artifacts, key=lambda value: value["sceneId"])
        ],
    },
    "environmentFingerprint": target_environment_fingerprint,
    "sourceAssemblyVersions": {
        "target": target_fingerprint[source_provenance_field],
        "feature": feature_fingerprint[source_provenance_field],
    },
    "allocationFailures": {
        intent: {
            name: feature_failures[intent].get(name)
            for name in allocation_semantic_fields
        }
        for intent in sorted(feature_failures)
    },
    "thresholds": {
        "minimumLinearLightSsim": 0.99,
        "minimumWindowedSsim": MINIMUM_WINDOWED_SSIM,
        "windowSize": WINDOW_SIZE,
        "maximumLinearRgbMae": 0.02,
        "maximumAlphaMae": 0.02,
        "maximumWindowedAlphaMae": MAXIMUM_WINDOWED_ALPHA_MAE,
        "maximumWindowedRgbaMae": MAXIMUM_WINDOWED_RGBA_MAE,
        "maximumAaCoverageBandChannelError": 0.02,
    },
    "scenes": results,
}
result_path.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
reconciliation_path.unlink()
print(f"Paired visual evidence passed and was recorded at {result_path}")
PY
