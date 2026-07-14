#!/usr/bin/env bash
set -euo pipefail

readonly legacy_commit=8dad1ba3
readonly provenance_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repo_root="$(git -C "$provenance_dir" rev-parse --show-toplevel)"
readonly references="$repo_root/tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/References/004-parity-strong"
readonly worktree="$(mktemp -d "${TMPDIR:-/tmp}/beutl-004-parity-strong.XXXXXX")"

cleanup() {
    git -C "$repo_root" worktree remove --force "$worktree" >/dev/null 2>&1 || true
    rm -rf "$worktree"
}
trap cleanup EXIT

(
    cd "$references"
    shasum -a 256 -c "$provenance_dir/sha256sums.txt"
)

git -C "$repo_root" worktree add --detach "$worktree" "$legacy_commit"
git -C "$worktree" apply --unidiff-zero "$provenance_dir/legacy-generator.patch"

BEUTL_REQUIRE_GPU=1 dotnet test \
    "$worktree/tests/Beutl.UnitTests/Beutl.UnitTests.csproj" \
    -f net10.0 \
    -p:LegacyStrongProbe=true \
    --settings "$worktree/coverlet.runsettings" \
    --filter FullyQualifiedName~FreezeStrongLegacyReference

readonly generated="$worktree/tests/Beutl.UnitTests/Engine/Graphics/Rendering/Golden/References/004-parity-strong-probe"
for reference in "$references"/*.rgbaf16.deflate; do
    name="$(basename "$reference")"
    cmp "$generated/$name" "$reference"
done

echo "Verified: all 004-parity-strong references are byte-identical to independent legacy renders from $legacy_commit."
