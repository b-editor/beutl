#!/usr/bin/env bash
# SC-007: record same-process parity results and their environment.
# This does not compare against a pre-feature build; see README.md for that additional evidence.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
output="${repo_root}/docs/specs/004-gpu-pass-fusion/evidence/sc-007-parity-manifest.json"
configuration="Release"
filter="FullyQualifiedName=Beutl.UnitTests.Engine.Graphics.Rendering.Golden.GpuPassFusionScaleRegionTests.AntialiasedThinStroke_CurrentPixelBoundaryPreservesEdgeCoverage"
filter+="|FullyQualifiedName=Beutl.UnitTests.Engine.Graphics.Rendering.Golden.WholeSourceHeadFusionParityTests.MosaicClampEdge_MatchesStandaloneWholeSourcePass"
filter+="|FullyQualifiedName=Beutl.UnitTests.Engine.Graphics.Rendering.Golden.WholeSourceHeadFusionParityTests.ScriptOutputSizeUniforms_MatchAcrossDirectAndMaterializedExecution"

usage() {
    cat <<'USAGE'
Usage: run-parity-evidence.sh [--output <path>] [--configuration <Debug|Release>]

Runs the GPU pass-fusion same-process parity suite with evidence recording enabled and writes the SC-007
manifest. Exits non-zero when the manifest could not be produced, when it carries no environment fingerprint
(the run cannot be shown to be comparable to any other), when its workload set differs from the SC-007 suite,
or when a compared case missed its threshold.
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        --configuration) configuration="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument '$1'." >&2; usage >&2; exit 1 ;;
    esac
done

mkdir -p "$(dirname "$output")"
rm -f "$output"

echo "==> Running the parity suite with evidence recording"
BEUTL_GPU_PASS_FUSION_PARITY_MANIFEST="$output" \
    dotnet test "${repo_root}/tests/Beutl.UnitTests/Beutl.UnitTests.csproj" \
    -c "$configuration" \
    -f net10.0 \
    --filter "$filter"

if [[ ! -f "$output" ]]; then
    echo "The parity suite produced no manifest at '${output}'." >&2
    echo "Every parity case was skipped, which on this machine usually means no Vulkan device was available." >&2
    exit 1
fi

echo "==> ${output}"
python3 - "$output" <<'PY'
import json, sys

path = sys.argv[1]
with open(path, encoding="utf-8") as handle:
    manifest = json.load(handle)

fingerprint = manifest.get("environmentFingerprint")
expected_cases = {
    "Beutl.UnitTests.Engine.Graphics.Rendering.Golden.GpuPassFusionScaleRegionTests."
    "AntialiasedThinStroke_CurrentPixelBoundaryPreservesEdgeCoverage",
    "Beutl.UnitTests.Engine.Graphics.Rendering.Golden.WholeSourceHeadFusionParityTests."
    "MosaicClampEdge_MatchesStandaloneWholeSourcePass",
    "Beutl.UnitTests.Engine.Graphics.Rendering.Golden.WholeSourceHeadFusionParityTests."
    "ScriptOutputSizeUniforms_MatchAcrossDirectAndMaterializedExecution",
}
actual_cases = set(manifest["cases"])
actual_passed_count = sum(case["passed"] for case in manifest["cases"].values())
print(f"  comparisonMode : {manifest['comparisonMode']}")
print(f"  commit         : {manifest['beutlEngineSourceRevision']}")
print(f"  cases          : {manifest['passedCaseCount']}/{manifest['caseCount']} passed")
if fingerprint:
    print(f"  device         : {fingerprint['vulkanDeviceName']} "
          f"({fingerprint['vulkanDriverName']} {fingerprint['vulkanDriverInfo']})")
    print(f"  maxAttachment  : {fingerprint['maxAttachmentDimension']}")
    print(f"  comparability  : {fingerprint['comparabilityKey']}")

failures = []
if not fingerprint:
    failures.append(
        "no environment fingerprint: " + str(manifest.get("fingerprintUnavailableReason")))
if manifest["caseCount"] != len(expected_cases):
    failures.append(
        f"caseCount is {manifest['caseCount']}, expected {len(expected_cases)}")
if manifest["passedCaseCount"] != actual_passed_count:
    failures.append(
        f"passedCaseCount is {manifest['passedCaseCount']}, actual {actual_passed_count}")
missing_cases = sorted(expected_cases - actual_cases)
unexpected_cases = sorted(actual_cases - expected_cases)
if missing_cases or unexpected_cases:
    differences = []
    if missing_cases:
        differences.append("missing: " + ", ".join(missing_cases))
    if unexpected_cases:
        differences.append("unexpected: " + ", ".join(unexpected_cases))
    failures.append("SC-007 workload set differs (" + "; ".join(differences) + ")")
failed = [name for name, case in manifest["cases"].items() if not case["passed"]]
if failed or not manifest["allCasesPassed"]:
    failures.append("cases below threshold: " + ", ".join(failed))

for failure in failures:
    print(f"  FAIL: {failure}")
sys.exit(0 if not failures else 1)
PY
