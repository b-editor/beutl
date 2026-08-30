#!/usr/bin/env bash
#
# SC-007 — records what the same-process parity suite compared, on what device, at what commit, and with what
# result, into a manifest under this directory.
#
# Both sides of every comparison run in one process on one device, so no committed device-specific reference
# blob is needed or used. This is NOT the comparison against a pre-feature build that SC-007's first clause
# names; see README.md for what that additionally requires.
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
output="${repo_root}/docs/specs/004-gpu-pass-fusion/evidence/sc-007-parity-manifest.json"
configuration="Release"
filter="FullyQualifiedName~WholeSourceHeadFusionParityTests|FullyQualifiedName~GpuPassFusionScaleRegionTests"

usage() {
    cat <<'USAGE'
Usage: run-parity-evidence.sh [--output <path>] [--configuration <Debug|Release>] [--filter <vstest filter>]

Runs the GPU pass-fusion same-process parity suite with evidence recording enabled and writes the SC-007
manifest. Exits non-zero when the manifest could not be produced, when it carries no environment fingerprint
(the run cannot be shown to be comparable to any other), or when a compared case missed its threshold.
USAGE
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output) output="$2"; shift 2 ;;
        --configuration) configuration="$2"; shift 2 ;;
        --filter) filter="$2"; shift 2 ;;
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
if manifest["caseCount"] == 0:
    failures.append("no case was compared")
if not manifest["allCasesPassed"]:
    failed = [name for name, case in manifest["cases"].items() if not case["passed"]]
    failures.append("cases below threshold: " + ", ".join(failed))

for failure in failures:
    print(f"  FAIL: {failure}")
sys.exit(0 if not failures else 1)
PY
