#!/usr/bin/env bash

set -euo pipefail

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
target_generator="$script_dir/generate-target-baseline.sh"
feature_worktree=""
feature_command=${BEUTL_GPU_PASS_FEATURE_EXPORT_COMMAND:-}
result_root=""

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
for command_name in git python3 bash; do
    command -v "$command_name" >/dev/null 2>&1 || {
        printf 'Required command is unavailable: %s\n' "$command_name" >&2
        exit 1
    }
done

feature_worktree=$(git -C "$feature_worktree" rev-parse --show-toplevel)
feature_sha=$(git -C "$feature_worktree" rev-parse HEAD)
result_root=$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$result_root")
[[ ! -e $result_root ]] || {
    printf 'Create-only paired result directory already exists: %s\n' "$result_root" >&2
    exit 1
}
mkdir -p "$(dirname -- "$result_root")"
mkdir "$result_root"

target_output="$result_root/target"
feature_output="$result_root/feature"

"$target_generator" --output-dir "$target_output"

BEUTL_GPU_PASS_EVIDENCE_OUTPUT_DIR="$feature_output" \
BEUTL_GPU_PASS_TARGET_OUTPUT_DIR="$target_output" \
BEUTL_GPU_PASS_BASELINE_MANIFEST="$target_output/manifest.json" \
BEUTL_GPU_PASS_EVIDENCE_MODE=feature \
BEUTL_REQUIRE_GPU=1 \
bash -c 'cd "$1" && exec bash -c "$2"' bash "$feature_worktree" "$feature_command"

[[ -f $feature_output/manifest.json ]] || {
    printf 'Feature exporter did not create %s/manifest.json\n' "$feature_output" >&2
    exit 1
}

FEATURE_SHA="$feature_sha" FEATURE_COMMAND="$feature_command" \
python3 - "$target_output" "$feature_output" "$result_root/paired-result.json" <<'PY'
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
if not isinstance(target_code_sha, str) or target_code_sha not in target_fingerprint[source_provenance_field]:
    raise SystemExit("Target engine assembly provenance does not contain baselineCodeSha")
if feature_code_sha not in feature_fingerprint[source_provenance_field]:
    raise SystemExit("Feature engine assembly provenance does not contain the feature worktree SHA")

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

semantic_fields = (
    "category", "role", "controlSceneId", "blobWidth", "blobHeight", "logicalWidth",
    "logicalHeight", "outputScale", "maxWorkingScale", "requestedRegion", "empty", "parameters",
)
for scene_id, target_scene in target_scenes.items():
    feature_scene = feature_scenes[scene_id]
    for name in semantic_fields:
        if target_scene.get(name) != feature_scene.get(name):
            raise SystemExit(f"Scene parameter mismatch before parity comparison: {scene_id}.{name}")
    if (target_scene.get("blob") is None) != (feature_scene.get("blob") is None):
        raise SystemExit(f"Scene blob presence differs: {scene_id}")

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
    scene_result = {"sceneId": scene_id, "fullImage": full}

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
    "targetManifestSha256": hashlib.sha256((target_root / "manifest.json").read_bytes()).hexdigest(),
    "featureManifestSha256": hashlib.sha256((feature_root / "manifest.json").read_bytes()).hexdigest(),
    "environmentFingerprint": target_environment_fingerprint,
    "sourceAssemblyVersions": {
        "target": target_fingerprint[source_provenance_field],
        "feature": feature_fingerprint[source_provenance_field],
    },
    "thresholds": {
        "minimumLinearLightSsim": 0.99,
        "maximumLinearRgbMae": 0.02,
        "maximumAlphaMae": 0.02,
        "maximumAaCoverageBandChannelError": 0.02,
    },
    "scenes": results,
}
result_path.write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
print(f"Paired visual evidence passed and was recorded at {result_path}")
PY
