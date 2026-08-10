# Target Baseline Evidence

## Status and scope

The immutable starting-SHA visual baseline and the minimum persistent-lifetime
BenchmarkDotNet baseline have been captured successfully. They describe the legacy
renderer at commit `83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53`; the generator verified that the
repository was clean before applying its observational patch in a temporary pinned
worktree.

This evidence freezes the starting behavior and the minimum three-case performance
reference. It does not satisfy the final performance acceptance gate by itself. The
full eleven-case paired run, bootstrap confidence intervals, and feature-versus-target
acceptance ratios remain part of the final T123 gate.

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

The paired runner reconciles the semantic refresh approved in `research.md` from
the committed baseline before parity: `scene3d-with-2d-tail`. Every other regenerated legacy artifact must remain
byte-identical to its committed counterpart.

## Immutable provenance

| Item | SHA-256 |
|---|---|
| Visual manifest | `0e2fcd3033e2a18378420c727353cfd4640d3c94b0101bffb85da0d717d62fc5` |
| Benchmark manifest | `60e9ef1f43cdc82db5674e9b8abae9e770bd79245c6d47efa5b6d011bfb30656` |
| Generator patch | `71da92c5fb25061ed0d588b10b47c539fe7c70d60ee396e8607259e11ddb071e` |
| Applied generator diff | `82bfb0daf6e434898336e2b1376d042de4f169a04c5b08e1e392aac777e1ce4c` |
| Generator script | `d45276861aab21bed36c4681067288905784df0fd6742e86dc57eb7fa829d752` |
| Paired visual runner | `b62e3257e45deaaa7e5ca4931aa6ddf5fa39defcae2933d5981a4cd34bbfd401` |
| Paired benchmark runner | `76406cf3b5ffe4c5698559ebb2bbfa069ba757e5156f2a0fc709d038e9496af8` |
| Intentional refresh script | `f9ff3831d63cf0f3ed864e20d15731a38b2402b6ac8e0a4c4c0a6860af72d1f2` |
| Generator source index | `4e6d87526754753978fd2f88c025fe8002dabbd07c6282c39c389fafc8ad24ca` |

The visual manifest is
[`target-baseline/manifest.json`](target-baseline/manifest.json), and the benchmark
manifest is [`target-benchmark/manifest.json`](target-benchmark/manifest.json). Both
record the baseline SHA, clean pre-patch state, and the same exact environment fingerprint.
The visual manifest records the current visual toolchain. The benchmark manifest preserves
its capture-time tool hashes while linking the current visual manifest; a selective visual
refresh never restamps archived timing data with a tool that did not produce it.

The selective intentional-refresh tool may run after a macOS reboot. It therefore treats
`metalRegistryId` as a recorded diagnostic rather than a persistent device identity when
comparing a live refresh with the frozen manifest. Every stable OS/runtime/backend/device/driver
field—including the Vulkan device and driver UUIDs—must still match exactly, and each publishable
RGBA16F payload must match its independently approved SHA-256. The paired target/feature runner is
unchanged: both live worktrees must still report byte-identical values for every fingerprint field.

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
| Metal device | Apple M3, registry ID `0x00000001000003a8` |
| Metal feature family | `spdisplays_metal4` |
| SkiaSharp | managed 3.119.4, native 119.0 |
| Silk.NET Vulkan | 2.23.0 |
| Beutl.Engine | `2.99.99+83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53` |

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
| `NoEffectControl` | 15 | 1,734.417 us | 2,059.372 us | 912.508 us |
| `ShaderOpacityShader` | 15 | 5,098.333 us | 5,860.350 us | 2,453.499 us |
| `ShaderOpacityShaderBarrier` | 15 | 11,677.167 us | 17,014.270 us | 16,184.679 us |

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
| [`counters.json`](target-benchmark/counters.json) | `1289b8629c0874a223632a6a76709eccf4543c766b85d902d1194c1a32a59dd2` |
| [`raw-benchmark-full.json`](target-benchmark/raw-benchmark-full.json) | `ad56d6721c9737dd8e3cde552d9af049abbe90de7b859a3be0d641918a5c974e` |
| [`raw-benchmark-github.md`](target-benchmark/raw-benchmark-github.md) | `65dd0004766c721242af92ef7b5a09de8f36b0778d16ced7336a8281e4879f08` |
| [`raw-benchmark-stdout.txt`](target-benchmark/raw-benchmark-stdout.txt) | `3f072aa98acd578fb0071e40aca2fe19d37ee0104f3e2b9effbd032bf0766d9e` |

The benchmark run started at `2026-08-09T07:43:38.318434+00:00` and completed at
`2026-08-09T07:44:39.929024+00:00`. The benchmark manifest binds these artifacts to
the visual manifest hash and evidence-tool hashes, preventing a timing result from a
different source, generator, or device fingerprint from being accepted silently.
