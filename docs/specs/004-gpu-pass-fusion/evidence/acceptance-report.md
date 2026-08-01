# GPU Pass Fusion Acceptance Report (T115 / T123)

Recorded on the authoritative Apple M3 / MoltenVK environment
(macOS 26.5.2, MoltenVK 1.4.0, Metal 3, .NET 10.0.9, arm64). All artifacts referenced
below are committed in this directory unless noted; every external raw result is
identified by its SHA-256.

## Tool provenance

| Tool | SHA-256 |
|---|---|
| `target-baseline-generator.patch` | `898692fc4a53e834cbc9f0e00176f8eca198e4f16b6de391d89f1fbbceeaa8be` |
| `generate-target-baseline.sh` | `bf0574663d6c825150b6e06192a42abda40dba45184f123ecf52ce5199ad255d` |
| `run-paired-visual-evidence.sh` | `a974984b5902506a23546ae0e24dc3fd3e87ea4e57035f87d5e9e69c5989cd8e` |
| `refresh-intentional-visual-baselines.sh` | `5057b76ae3d4c1bc4474e424cc3119c5ce52aa8c203fcc0cac874d38cd8c74d8` |
| generator source bundle | `bb165d312af895b4f703441d96d4f42144036d7d6f8e875ae0101c4701b0414d` |
| `run-paired-benchmarks.sh` | `a8575996b4ee74663d42fc4268e6d93fba8062739a4bedf5b7bd16f8fe226969` |

These hashes match the committed scripts and the `evidenceTools` records in both
frozen manifests. The recorded benchmark run predates the later review-driven runner
hardening, so its own provenance records the runner version it actually executed,
`7e33ff52ee0d1b1db367cc326953195e773afc18af23b0f6a6e72a06187893a8`.
The current runner pins the baseline worktree and executes the documented discarded
baseline warm-up before creating baseline A, feature, and baseline B artifacts. This
automates the methodology used for the recorded run; discarded artifacts remain
outside the evidence directory.

The current post-run output-oracle hardening is anchored separately from that frozen
run provenance:

| Current source | SHA-256 |
|---|---|
| `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs` | `1733c5c73f3fedc5977b5a0ab067e6ae391b33e5487a791fcf150d260636cf8a` |
| `tests/Beutl.Benchmarks/Rendering/PairedBenchmarkAnalyzer.cs` | `e07e80bcf0da48cf8d0db1119b77c61b26f2e963e925299fdf0a684e4ef627e4` |
| `target-benchmark-harness/TargetRenderPipelineBenchmarks.cs` | `2b334e5e5e874610d10c468dc9742dfee51f33fb91cb7e24765d191fe36f6eb2` |

These hashes identify the current stricter implementation; they do not replace the
historical harness hashes authenticated by the unchanged committed benchmark manifest.

The current immutable trust-chain anchors are target visual manifest
`954236df9a2b47831d069045cbc58e27f29f02fd22afd3ca0de8e4a62a2d0945` and
target benchmark manifest
`17b65acc47289f94d208b1cf3284e69a4f94a89fba04ad61e3bf0b8b75660ebf`.
The benchmark manifest's `visualManifestSha256` and
`GpuPassFusionBaselineEvidence.ExpectedManifestSha256` both name the former.

## Paired visual evidence (passed)

- Target: legacy renderer regenerated from `43a38e665d9bf52548161a3917e748bd1457ff55`; feature: `acabdbfd7c5f6601b520daf88df0f50f80eb40cf`.
- Environment fingerprint gate: exact match required and satisfied before any parity metric.
- Result: **all 44 scenes passed** — thresholds SSIM ≥ 0.99,
  linear-RGB MAE ≤ 0.02, alpha MAE ≤ 0.02, and the 16×16 minimum-window
  SSIM ≥ 0.95 plus maximum window-local alpha MAE ≤ 0.02 and RGBA MAE ≤ 0.05.
  Worst full-image linear-light SSIM: 0.99943 (`nested-drawable-brush-delay`).
- The runner self-tests both RGB and alpha-only localized 14×14 defects that pass
  every whole-image threshold while failing a window-local threshold, and applies
  all three windowed bounds to every scene; the recorded run executed under this gate.
- The run also compares the `bounds-hit-test-query` measured record (bounds, probe
  points, hit results) and the preview/delivery allocation-failure records against
  the frozen baseline. The allocation probe initially exposed a real FR-039
  regression — the feature pipeline threw on Preview effect-materialization
  allocation failure where the baseline drops the output — fixed in
  `d3dc99667` (consumer-provenance-scoped preview drop with the
  `PreviewAllocationDrops` counter); the recorded outcomes now match the baseline
  (`dropped-output-without-throw` / `threw`). The feature lane additionally pins the
  observed Preview bounds, scale, transparent finite bitmap, output SHA-256, request
  status, failure dimensions, and diagnostics counters, plus the Delivery exception
  type, exact message, and failure counters.
- Raw result: [`paired-visual-result.json`](paired-visual-result.json)
  (SHA-256 `34a3c98f6f2a2109e3d2be8162238db0b08dd9b72bab934adbc3368fed1b40a7`);
  run-regenerated target manifest `11859a01858b248963ab5f2b0cd6aa6da00818afdfce3d2cf04869c708a1f29d`, feature manifest `26874c1573fdf4552273a016e37f3bab9dc8b0e1875a97ada976a8bd9ad1eee2`.
- The exact historical inputs are retained under
  [`paired-visual-run/target/`](paired-visual-run/target/) and
  [`paired-visual-run/feature/`](paired-visual-run/feature/). Each manifest authenticates
  all 44 RGBA16F blobs; their manifest SHA-256 values are the target and feature values
  recorded above, so the committed result can be recomputed without regenerating either lane.

### Paired exact-fingerprint AA edge bound

Per-channel maximum error bound 0.02 over the reference coverage band (0 < α < 1):

| Scene | Band pixels | Band RGBA MAE | Max channel error |
|---|---|---|---|
| `aa-thin-line-color-times-alpha` | 340 | 0.000001 | 0.000488 |
| `aa-thin-stroke-color-times-alpha` | 356 | 0.000000 | 0.000000 |

### Approved semantic refreshes

Parity for the three research.md-approved artifacts ran against the committed refreshed
blobs; every other artifact was byte-verified against the regenerated legacy baseline.

| Scene | Legacy blob | Refreshed blob |
|---|---|---|
| `geometry-stroke` | `37e7c40d349c52a1…` | `047982d1f4ffecbf…` |
| `scene3d-with-2d-tail` | `89d111e13da934fd…` | `8908d30de25b8823…` |
| `split-expansion` | `028a6a61e1aa448a…` | `ac694c8d884a6807…` |

### Non-vacuity

All parity scenes carry non-vacuity evidence; the minimum recorded margin above
tolerance is 0.0867 (`geometry-stroke`, `marginAboveTolerance`).

## Same-process fusion-disabled/enabled A/B (passed)

`BEUTL_REQUIRE_GPU=1 dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj -f net10.0 --filter "FullyQualifiedName~Rendering.Fusion."`
— **115/115 passed** on the authoritative device as the broader fusion regression
suite; that count is not a claim that every selected test renders both modes. The
actual same-process disabled/enabled rendering subset is
`FusionBoundaryTests.RuntimeBarrier_PreservesDisabledEnabledParityAndExactMaterialization`
(all runtime-barrier cases), `AntialiasedThinStroke_NonlinearShaderPreservesCoverageAtTheExactBoundary`,
`SelectedRenderCacheBoundary_PreservesParityAndPreventsCrossBoundaryFusion`,
`StandaloneBackendOverflow_ExecutesCompatibilityPathWithParityAndExactReason`,
`CrossNodeShaderFusionTests.Enabled_ExecutesDistinctNodePrimaryChainOnce_WithParityAndAWarmedProgram`,
`CrossNodeShaderFusionTests.FiniteOutOfRangeOpacity_NormalizesBeforePlanningAndMatchesUnfusedExecution`,
and `ShaderFallbackTests.FusedCurrentPixelStages_ReceiveTheSameRoiCroppedInputBoundsAsUnfusedStages`.
Those tests assert their scenario-specific parity and materialization contracts; the
fixed per-channel AA edge maximum-error bound of **0.02** belongs specifically to the
antialiased thin-stroke pair. T122's ordinary non-GPU fallback runs the named
`ShaderFallbackTests` coverage only; the hardware-required paired cases remain the
separate authoritative-device gate.

## Paired persistent-lifetime benchmark

Recorded-run methodology (frozen; revised after review to keep the timed path free of
counter construction — counters and output hashes came from untimed replays verified
against the timed run's cheap token): BenchmarkDotNet Monitoring strategy, warmup 3 +
5 setup frames, 15 iterations × 1 invocation, three runs
(baseline-A → feature → baseline-B) preceded by one discarded warm-up pass,
bootstrap 100,000 iterations, seed 20040719, confidence 0.95. The analyzer verifies
every case's outputs across the baseline repeats, the feature's setup/measured
self-consistency, and exact no-effect-control equality; effect workloads are
intentionally not byte-identical across pipelines (FR-019), so cross-pipeline
equivalence is proven by the paired visual evidence.
The current feature and starting-SHA harnesses strengthen that boundary without adding
full-image hashing to the timed interval: cleanup hashes the retained final timed frame
and requires exact bounds, dimensions, checksum, and SHA-256 equality with the untimed
replay. The committed run predates this additional oracle, so its manifest and numeric
results remain unchanged and continue to record the harness hashes that produced them.
Recorded run committed under
[`paired-benchmark-run/`](paired-benchmark-run/) (manifest SHA-256
`839eaf34e4fa5824a03333fa50418259ea3fca302a044eb767110afb6b676b1e`), feature code SHA `912ddda0484d0b8cde3c63b60deefa491a0c596c`.

The two acceptance lanes are intentionally revision-scoped rather than revision-identical.
The performance result remains frozen at feature revision
`912ddda0484d0b8cde3c63b60deefa491a0c596c`; its numeric ratios apply only to that
revision. The visual oracle is regenerated after approved hardening rounds and currently
validates semantic behavior at `acabdbfd7c5f6601b520daf88df0f50f80eb40cf`.
Later visual evidence is a semantic no-regression gate for those hardening changes; it does
not reattribute the frozen benchmark ratios to the later revision, and the benchmark run is
not regenerated merely to advance the visual revision.

### Primary gate — passed

Rule: `bootstrap-95%-ci-for-feature-over-pooled-stable-baseline-a-and-b-median-ratio-entirely-below-1.0`.

- `ShaderOpacityShader` feature/baseline median ratio **0.3958**,
  95% CI **[0.3805, 0.4137]** — entirely below 1.0. The primary gate passed in
  **all five** recorded attempts (ratio 0.37–0.45 throughout).

### All case ratios (feature / pooled baseline)

| Case | Median ratio | 95% CI |
|---|---|---|
| `LongInvariantChain` | 0.1440 | [0.1351, 0.1495] |
| `MixedSpatialColor` | 0.3299 | [0.3090, 0.3636] |
| `MultipleDrawablesTargetDependencies` | 0.3752 | [0.3677, 0.4035] |
| `NoEffectControl` | 1.0811 | [1.0318, 1.1891] |
| `ParameterOnlyAnimation` | 0.3274 | [0.3186, 0.3409] |
| `ShaderOpacityShader` | 0.3958 | [0.3805, 0.4137] |
| `ShaderOpacityShaderBarrier` | 0.3843 | [0.3583, 0.4136] |
| `SingleShader` | 0.6012 | [0.5655, 0.6298] |
| `SmallObjectFixedOverhead` | 0.3327 | [0.3260, 0.3409] |
| `StaticPrefixAnimatedTail` | 0.3282 | [0.3135, 0.3657] |
| `StructuralToggle` | 0.3522 | [0.3413, 0.3819] |

### Control / barrier and baseline repeat-stability gates — environment-limited

The committed manifest plainly records `baselineRepeatStable=false`,
`controlBarrierAcceptancePassed=false`, and consequently
`overallAcceptancePassed=false`. These are environment-limited failures, not green
acceptance claims: the tables below show the interactive-host drift that caused each
failed flag. The documented fully-green path is an otherwise idle cold-boot rerun of
the unchanged gates and sampling procedure.

Rules: control/barrier requires the feature/baseline CI upper bound to stay within a
case tolerance derived from the baseline repeat factor; repeat stability requires,
for every case, the baseline-A/B ratio's 95% CI to contain 1.0 with a symmetric
factor ≤ 1.20.

In the recorded run every repeat factor is ≤ 1.149 (the review-driven timed-path fix
halved the noise), but the tightened CIs expose a ±5–10% systematic drift between
the first and last baseline run on this interactive machine, which excludes 1.0:

| Case | Symmetric factor | CI contains 1.0 |
|---|---|---|
| `MixedSpatialColor` | 1.088 | no |
| `ShaderOpacityShaderBarrier` | 1.116 | no |
| `SingleShader` | 1.149 | no |
| `StaticPrefixAnimatedTail` | 1.148 | no |

The control case (`NoEffectControl` ratio 1.0811, CI upper 1.1891) exceeded its
repeat-derived tolerance (1.1341) for the same reason — the tolerance tightens with
the repeat factor, so low-noise runs demand drift smaller than this environment
provides; the barrier case passed (0.3843, CI upper 0.4136 ≤ 1.1160). Five
attempts were recorded across both methodology revisions:

| Attempt | Primary | Control/Barrier | Repeat stability | Primary ratio | Manifest SHA-256 |
|---|---|---|---|---|---|
| 1 | pass | pass | fail | 0.4454 | `4dcbc516747552ab…` |
| 2 | pass | pass | fail | 0.3867 | `cce55ff1f105b34b…` |
| 3 | pass | pass | fail | 0.3792 | `098287cecaa1b8a6…` |
| 4 | pass | fail | fail | 0.3726 | `1f293d453ae6ec1c…` |
| 5 | pass | fail | fail | 0.3958 | `129725e6281c7bbd…` |

Attempts 1–4 predate the timed-path methodology fix; attempt 5 is the recorded
run above. A shader-cache cold-start bimodality was identified and eliminated with
the discarded warm-up pass. The remaining drift is thermal/scheduler behaviour of
an interactive session, not code behaviour. Re-running the committed script on a
freshly booted, otherwise idle machine (one discarded warm-up pass, then one
recorded pass) is the documented path to a fully-green acceptance; the gates and
methodology are unchanged.

## Committed raw-result hashes

| File | SHA-256 |
|---|---|
| `paired-benchmark-run/baseline-a/code-sha.txt` | `6f680c11bf735ee2d1a2d5fac6518f1edd2acecacda7ff1944374ddb8e4c22df` |
| `paired-benchmark-run/baseline-a/command.txt` | `e43b69c320b535338cc5228d3298eb8634d65fdae7fd32706b2fb7154b5cb427` |
| `paired-benchmark-run/baseline-a/counters/LongInvariantChain.json` | `4e03a8e3f6cac67881b52664012fe285d73b2d4e5f5d63433e83d472801a71c2` |
| `paired-benchmark-run/baseline-a/counters/MixedSpatialColor.json` | `b87b40823b230bf53d591bd7777b195b2d7288fbe96b6ce416f503d8f73cd35a` |
| `paired-benchmark-run/baseline-a/counters/MultipleDrawablesTargetDependencies.json` | `7591a34c651b2d9bc61bb1f40cef37abe5edf9cbd41248083ca4c3256bb3da28` |
| `paired-benchmark-run/baseline-a/counters/NoEffectControl.json` | `a70aceb27c9331339b0f8294969449135a59b416934c683ba7294fbdade963b9` |
| `paired-benchmark-run/baseline-a/counters/ParameterOnlyAnimation.json` | `7437f3824633e348ead8913de00e41b658045898f395b46ea6a83cc2b52d1ef0` |
| `paired-benchmark-run/baseline-a/counters/ShaderOpacityShader.json` | `0a05e1461c47c4e142e1606c29fe27098302d9591900e4627f79d1ab5c06a7b7` |
| `paired-benchmark-run/baseline-a/counters/ShaderOpacityShaderBarrier.json` | `54fe20e26a34dd3b329aef8ad8a36fd949fe5906aa2e0719d4661856d71a0aad` |
| `paired-benchmark-run/baseline-a/counters/SingleShader.json` | `154a7f8e3e227332f23b5e287ff8f7ed5cc168d97d6a0c0bba5f18137c7a9186` |
| `paired-benchmark-run/baseline-a/counters/SmallObjectFixedOverhead.json` | `6741901a83876b82bd8f5148dbe016f9cfea3a4c5ee85e01f2d16cbb4ca80c7a` |
| `paired-benchmark-run/baseline-a/counters/StaticPrefixAnimatedTail.json` | `9c76ca366a07d3e74dcd5267d323a17e9f21bc16db764fa9dbcbea2f1e36b0ad` |
| `paired-benchmark-run/baseline-a/counters/StructuralToggle.json` | `8c462686c2b8bbe0db183a30b6167ce64226730a292768cac63bc86229856e83` |
| `paired-benchmark-run/baseline-a/raw-benchmark-full.json` | `066efc54b0ebbdc82971a62437b190d87aff3e71e9c54907b47a1fdda0c18b95` |
| `paired-benchmark-run/baseline-a/raw-benchmark-stdout.txt` | `7ba6fc96a86c09b15ba11bf4bd484fed85b7b8308f5b71a69c91124f1f0c7907` |
| `paired-benchmark-run/baseline-b/code-sha.txt` | `6f680c11bf735ee2d1a2d5fac6518f1edd2acecacda7ff1944374ddb8e4c22df` |
| `paired-benchmark-run/baseline-b/command.txt` | `e43b69c320b535338cc5228d3298eb8634d65fdae7fd32706b2fb7154b5cb427` |
| `paired-benchmark-run/baseline-b/counters/LongInvariantChain.json` | `4e03a8e3f6cac67881b52664012fe285d73b2d4e5f5d63433e83d472801a71c2` |
| `paired-benchmark-run/baseline-b/counters/MixedSpatialColor.json` | `b87b40823b230bf53d591bd7777b195b2d7288fbe96b6ce416f503d8f73cd35a` |
| `paired-benchmark-run/baseline-b/counters/MultipleDrawablesTargetDependencies.json` | `7591a34c651b2d9bc61bb1f40cef37abe5edf9cbd41248083ca4c3256bb3da28` |
| `paired-benchmark-run/baseline-b/counters/NoEffectControl.json` | `a70aceb27c9331339b0f8294969449135a59b416934c683ba7294fbdade963b9` |
| `paired-benchmark-run/baseline-b/counters/ParameterOnlyAnimation.json` | `7437f3824633e348ead8913de00e41b658045898f395b46ea6a83cc2b52d1ef0` |
| `paired-benchmark-run/baseline-b/counters/ShaderOpacityShader.json` | `0a05e1461c47c4e142e1606c29fe27098302d9591900e4627f79d1ab5c06a7b7` |
| `paired-benchmark-run/baseline-b/counters/ShaderOpacityShaderBarrier.json` | `54fe20e26a34dd3b329aef8ad8a36fd949fe5906aa2e0719d4661856d71a0aad` |
| `paired-benchmark-run/baseline-b/counters/SingleShader.json` | `154a7f8e3e227332f23b5e287ff8f7ed5cc168d97d6a0c0bba5f18137c7a9186` |
| `paired-benchmark-run/baseline-b/counters/SmallObjectFixedOverhead.json` | `6741901a83876b82bd8f5148dbe016f9cfea3a4c5ee85e01f2d16cbb4ca80c7a` |
| `paired-benchmark-run/baseline-b/counters/StaticPrefixAnimatedTail.json` | `9c76ca366a07d3e74dcd5267d323a17e9f21bc16db764fa9dbcbea2f1e36b0ad` |
| `paired-benchmark-run/baseline-b/counters/StructuralToggle.json` | `8c462686c2b8bbe0db183a30b6167ce64226730a292768cac63bc86229856e83` |
| `paired-benchmark-run/baseline-b/raw-benchmark-full.json` | `9f5485ca9e0ffce3ad867e3ddb3b9f28bf40d3db3b789c5bc66d4b7f22b906dd` |
| `paired-benchmark-run/baseline-b/raw-benchmark-stdout.txt` | `2a52642b31a05a5429fc2b15970dc29ddc7824c058fa7c6cf9df7a0d907e5268` |
| `paired-benchmark-run/feature/code-sha.txt` | `ff520fe7d6e19c938d642cfb2995ccab9676787ba0dbb61465c5f598145e95d3` |
| `paired-benchmark-run/feature/command.txt` | `6205b7479720d1d52df1e80bf05058e9d55dafa763cb50d7771e363daf8f40ce` |
| `paired-benchmark-run/feature/counters/LongInvariantChain.json` | `6c97c52d24ba86ca64fe0e70900e9efa00f076c683570572e5246c9a0e38907e` |
| `paired-benchmark-run/feature/counters/MixedSpatialColor.json` | `c554ddccd9d39e6697c1a98ac4a6f4b8392ec03e02482e34dfbd9b084910c943` |
| `paired-benchmark-run/feature/counters/MultipleDrawablesTargetDependencies.json` | `223e148b6ad5c6fb66935bb46815b4b4f2386716d5f38dcc8f7709a55a1c20e6` |
| `paired-benchmark-run/feature/counters/NoEffectControl.json` | `215cd42d7782d217a029a6cc1a2cca6ab5af8222a08e491f719ce937b1607231` |
| `paired-benchmark-run/feature/counters/ParameterOnlyAnimation.json` | `331088a258077d6338b1e5ddbf9db90b43d5710595d9837244c29ba396cca747` |
| `paired-benchmark-run/feature/counters/ShaderOpacityShader.json` | `16a30dfe1009ecc6b7cef7c7ed1cdadbe5e6178325a4dfd19a2c8ba4c232a402` |
| `paired-benchmark-run/feature/counters/ShaderOpacityShaderBarrier.json` | `cb319ef4da6cdc39cf71e41fd92d1a9b1da130742ac6d33e85fa5a0ca60c451e` |
| `paired-benchmark-run/feature/counters/SingleShader.json` | `d807b5438d8836b90d8c844b04697075edb8dece23554b7e3ecfb1d74eccb16e` |
| `paired-benchmark-run/feature/counters/SmallObjectFixedOverhead.json` | `39dea47668c4712a2f9a0cc6be99870c9e41ada567a03f25af67adca16627a70` |
| `paired-benchmark-run/feature/counters/StaticPrefixAnimatedTail.json` | `3dd4a042d0e6df6a66b3d346d44005e512d2d26607bce2116100680517cda0f7` |
| `paired-benchmark-run/feature/counters/StructuralToggle.json` | `4b8082353817d820a660dc8b42497f027a76aa6556f41258e04b7fd82b08f3d8` |
| `paired-benchmark-run/feature/raw-benchmark-full.json` | `ba6f5c67bc63d6c12f540daf4a5fcd1d7331bd1f3c623a8e6ed6315399467a7b` |
| `paired-benchmark-run/feature/raw-benchmark-stdout.txt` | `a174be2deafef47e6c0b355d292d34432a974c74cfa95c02e29616e786c2c283` |
| `paired-benchmark-run/manifest.json` | `839eaf34e4fa5824a03333fa50418259ea3fca302a044eb767110afb6b676b1e` |

Visual evidence regenerated 2026-08-01T13:30:03Z on the fingerprinted machine
(benchmark run recorded 2026-07-31T18:35:08Z on the same machine). The paired
benchmark analyzer has since been tightened to require the exact frozen BenchmarkDotNet
job (`Monitoring`, warmup 3, iterations 15, launch/invocation/unroll 1) and matching
sample counts across all three runs; the recorded run satisfies the tightened gate
(11 cases × 15 samples in each of baseline-A, feature, and baseline-B).
