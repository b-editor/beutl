# Target Baseline Evidence

## Status and scope

The immutable starting-SHA visual baseline and the minimum persistent-lifetime
BenchmarkDotNet baseline have been captured successfully. They describe the legacy
renderer at commit `43a38e665d9bf52548161a3917e748bd1457ff55`; the generator verified that the
repository was clean before applying its observational patch in a temporary pinned
worktree.

This evidence freezes the starting behavior and the minimum three-case performance
reference. It does not satisfy the final performance acceptance gate by itself. The
full eleven-case paired run, bootstrap confidence intervals, and feature-versus-target
acceptance ratios remain part of T112--T115.

## Reproduction

Run from the repository root on the fingerprinted machine:

```bash
docs/specs/004-gpu-pass-fusion/evidence/generate-target-baseline.sh
```

The driver creates a temporary worktree at the exact baseline SHA, checks that it is
clean, applies `target-baseline-generator.patch` only in that worktree, restores and
builds the out-of-tree generator, and validates the generated evidence before copying
it back. Existing committed evidence is immutable: the visual output must compare
byte-for-byte, while an existing benchmark set is validated by its manifest and
artifact hashes instead of rerunning timing measurements.

The paired visual driver is:

```bash
docs/specs/004-gpu-pass-fusion/evidence/run-paired-visual-evidence.sh \
  --feature-worktree /absolute/path/to/feature-worktree \
  --output-dir /absolute/path/to/result \
  --feature-command 'command that exports the feature manifest and RGBA16F files'
```

It rejects missing or non-identical execution-environment fingerprint fields before
decoding an RGBA16F artifact or computing a parity metric. Source identity is checked
separately: each engine assembly version must contain its own worktree SHA, so the
expected target/feature code-version difference cannot be mistaken for an environment
mismatch.

## Immutable provenance

| Item | SHA-256 |
|---|---|
| Visual manifest | `e3a1a6821715c88f927422c1f2bc9fa0f3e74c887bab2f85ac13b15e9b822618` |
| Benchmark manifest | `e04534c05ca58386251c6f793ea94c96f4df287e3cc65dbc52d1126c3d9a2a2f` |
| Generator patch / applied diff | `037315804fa9531bdef1b79e2db405e8a3813e4bc137527690f9f2d5cb4e728c` |
| Generator script | `05d33410a06cdd3a6fee91911b25a348fdc480ed249916e45fe75c653b40c4f7` |
| Paired visual runner | `1ac369986cf888ec32f39060f5d96d3c6758f881ecdb40f21f469926e253d413` |
| Generator source bundle | `d6e5f339d5d7214b0cb879aa5cf2cd717896879b942400928e77b38c9a62a19e` |

The visual manifest is
[`target-baseline/manifest.json`](target-baseline/manifest.json), and the benchmark
manifest is [`target-benchmark/manifest.json`](target-benchmark/manifest.json). Both
record the baseline SHA, clean pre-patch state, evidence-tool hashes, and the same
exact environment fingerprint.

## Evidence fingerprint

| Field | Captured value |
|---|---|
| OS | macOS 26.5.2, build `25F84` (`Unix 26.5.2`) |
| OS / process architecture | Arm64 / Arm64 |
| Runtime identifier | `osx-arm64` |
| .NET runtime | .NET 10.0.9, environment version `10.0.9` |
| Renderer / Skia backend | Metal / Metal |
| Device selection | `automatic-no-preferred-device` |
| GPU | Apple M3 integrated GPU, vendor `0x0000106b`, device `0x1a050209` |
| Vulkan API | 1.2.323 |
| Vulkan driver | MoltenVK 1.4.0, driver ID `DriverIDMoltenvk`, raw version `10400` |
| Metal device | Apple M3, registry ID `0x0000000100000462` |
| Metal feature family | `spdisplays_metal4` |
| SkiaSharp | managed 3.119.4, native 119.0 |
| Silk.NET Vulkan | 2.23.0 |
| Beutl.Engine | `2.99.99+43a38e665d9bf52548161a3917e748bd1457ff55` |

The manifests retain the complete Vulkan device/driver UUIDs, enabled-extension list,
library build metadata, and Metal driver string. A paired run requires equality of all
execution-environment fields, not only the abbreviated table above. The engine
assembly version is deliberately validated as source provenance against the respective
target or feature SHA instead of requiring two different commits to report the same
version.

## Visual baseline

The visual set uses seed `20040719` and contains 47 scene records: 21 parity scenes,
23 controls, and 3 metadata-only scenes. The 44 immutable image artifacts are
row-packed little-endian linear-sRGB premultiplied RGBA16F with eight bytes per pixel.
Every artifact's exact dimensions, byte length, and SHA-256 are stored in the visual
manifest.

Coverage includes the primary cross-node shader/opacity/shader chain, WholeSource and
Geometry boundaries, opaque custom readback, mixed spatial/color/LUT work, dynamic
split expansion, external materialization, multiple roots and root ordering, backdrop
ordering, nested DrawableBrush/delay, cold and warm cache behavior, output scales,
shifted/outside/empty ROI, no-preferred-device fallback, a nonempty 3D result with a
2D tail, bounds/hit-test metadata, and analytic antialiased thin-line/thin-stroke
coverage controls. Each parity scene was captured twice and had to produce identical
bytes, counters, and events. Non-vacuity deltas against controls are recorded per
scene.

The allocation-failure probes record the legacy behavior at the next effect
materialization allocation:

- preview intent drops the output without throwing and records one
  `PreviewAllocationDrops` event;
- delivery intent throws `InvalidOperationException` and records one
  `DeliveryAllocationThrows` event.

## Benchmark baseline

BenchmarkDotNet 0.15.8 ran one launch with 3 framework warm-up iterations and 15
measurement iterations per case. The fixture additionally rendered 5 explicit warm-up
frames before measurement. Each invocation renders and reads back one complete
192x108 RGBA16F target using a fixed seed of `20040719`.

The benchmark lifetime keeps the root, external target, canvas, processor, and node
cache alive across iterations. The persistent node cache exists, while output-cache
selection is disabled so every measurement executes the requested frame. Stable setup
checksums, final output SHA-256 values, request counters, events, and the exact
fingerprint are recorded in
[`target-benchmark/counters.json`](target-benchmark/counters.json).

| Case | N | Median | Mean | Standard deviation |
|---|---:|---:|---:|---:|
| `NoEffectControl` | 15 | 726.167 us | 924.928 us | 565.805 us |
| `ShaderOpacityShader` | 15 | 2,765.458 us | 3,072.378 us | 694.012 us |
| `ShaderOpacityShaderBarrier` | 15 | 3,749.583 us | 4,016.744 us | 1,000.848 us |

The last measured request counters provide the legacy structural reference:

| Case | Effect materializations | Opaque external executions | Legacy intermediate targets | Operations executed |
|---|---:|---:|---:|---:|
| `NoEffectControl` | 0 | 0 | 0 | 1 |
| `ShaderOpacityShader` | 2 | 2 | 4 | 4 |
| `ShaderOpacityShaderBarrier` | 3 | 3 | 6 | 5 |

BenchmarkDotNet reported multimodal-distribution and minimum-iteration-time warnings;
the shortest observations were below its recommended 100 ms iteration duration. The
warnings are preserved verbatim in the raw output. These values are a transparent
starting-SHA reference, not the final regression ratios or confidence intervals.

## Raw benchmark artifacts

| Artifact | SHA-256 |
|---|---|
| [`counters.json`](target-benchmark/counters.json) | `ca9ad14f63e40b92ba71b1fad46a615fe66e2537d4b4f9dda06a045989310240` |
| [`raw-benchmark-full.json`](target-benchmark/raw-benchmark-full.json) | `d842833fbbf0045a0c57c6d8323041859cd8ffe821670d3426b2541a4b3abd2d` |
| [`raw-benchmark-github.md`](target-benchmark/raw-benchmark-github.md) | `e95233ee691433059a8a9fef161d01c7687727b30ce41c916067a514f7614056` |
| [`raw-benchmark-stdout.txt`](target-benchmark/raw-benchmark-stdout.txt) | `fee2f1bc8b610a3e0ba3a1f5d229b83de62806b4369d7ab7f37590b786bb592f` |

The benchmark run started at `2026-07-19T07:58:07.936284+00:00` and completed at
`2026-07-19T07:58:27.988716+00:00`. The benchmark manifest binds these artifacts to
the visual manifest hash and evidence-tool hashes, preventing a timing result from a
different source, generator, or device fingerprint from being accepted silently.
