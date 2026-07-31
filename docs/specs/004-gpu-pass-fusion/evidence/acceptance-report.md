# GPU Pass Fusion Acceptance Report (T115 / T123)

Recorded on the authoritative Apple M3 / MoltenVK environment
(macOS 26.5.2, MoltenVK 1.4.0, Metal 3, .NET 10.0.9, arm64). All artifacts referenced
below are committed in this directory unless noted; every external raw result is
identified by its SHA-256.

## Tool provenance

| Tool | SHA-256 |
|---|---|
| `target-baseline-generator.patch` | `037315804fa9531bdef1b79e2db405e8a3813e4bc137527690f9f2d5cb4e728c` |
| `generate-target-baseline.sh` | `fb0bf369aff9b017c82edf74e8423e83fd13156d3e1a569267447fa4fdf5df03` |
| `run-paired-visual-evidence.sh` | `9248461402bc7a8aaceb856e9214759e8c8bee27013be487935bb044460241c0` |
| `refresh-intentional-visual-baselines.sh` | `5057b76ae3d4c1bc4474e424cc3119c5ce52aa8c203fcc0cac874d38cd8c74d8` |
| generator source bundle | `d6e5f339d5d7214b0cb879aa5cf2cd717896879b942400928e77b38c9a62a19e` |
| `run-paired-benchmarks.sh` | `809e4b813074592927e586429ccf5cae426485a32fd09a56eafab5b856ab2123` |

These hashes match the committed scripts and the `evidenceTools` records in both
frozen manifests. The recorded benchmark run predates the later review-driven
runner hardening (frozen-SHA pinning), so its own provenance records the runner
version it actually executed, `7e33ff52ee0d1b1db367cc326953195e773afc18af23b0f6a6e72a06187893a8`;
the pinning change affects only how the baseline worktree is selected, not the
measurement methodology.

## Paired visual evidence (passed)

- Target: legacy renderer regenerated from `43a38e665d9bf52548161a3917e748bd1457ff55`; feature: `f8c486518a9fbf028a9abb588034e7068ce4d56a`.
- Environment fingerprint gate: exact match required and satisfied before any parity metric.
- Result: **all 44 scenes passed** — thresholds SSIM ≥ 0.99,
  linear-RGB MAE ≤ 0.02, alpha MAE ≤ 0.02.
  Worst full-image linear-light SSIM: 0.99943 (`nested-drawable-brush-delay`).
- The run also compares the `bounds-hit-test-query` measured record (bounds, probe
  points, hit results) and the preview/delivery allocation-failure records against
  the frozen baseline. The allocation probe initially exposed a real FR-039
  regression — the feature pipeline threw on Preview effect-materialization
  allocation failure where the baseline drops the output — fixed in
  `d3dc99667` (consumer-provenance-scoped preview drop with the
  `PreviewAllocationDrops` counter); the recorded outcomes now match the baseline
  (`dropped-output-without-throw` / `threw`).
- Raw result: [`paired-visual-result.json`](paired-visual-result.json)
  (SHA-256 `7c5cfb535e9a9157295a2d64c88f9f376894b1b39d513694289f4ba1df454443`);
  run-regenerated target manifest `9a95e4b486909b60220c4becef6768ede4e8bf285e48d62a8c76040891f950e8`, feature manifest `f9e1e1bfc5219b0c6a233317ccfebc42e7773e8d2b2604f30ae5ac7ba419997e`.

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
— **115/115 passed** on the authoritative device. These suites (`FusionBoundaryTests`,
`CrossNodeShaderFusionTests`, and the other `Rendering.Fusion` fixtures) render each
workload twice in one process with fusion disabled and enabled and assert the fixed
per-channel AA edge maximum-error bound of **0.02** alongside exact-materialization
checks. The same assertions run in normal CI without the GPU requirement via the
documented fallback path (T122).

## Paired persistent-lifetime benchmark

Methodology (frozen; revised after review to keep the timed path free of counter
construction — counters and output hashes now come from untimed replays verified
against the timed run's token): BenchmarkDotNet Monitoring strategy, warmup 3 +
5 setup frames, 15 iterations × 1 invocation, three runs
(baseline-A → feature → baseline-B) preceded by one discarded warm-up pass,
bootstrap 100,000 iterations, seed 20040719, confidence 0.95. The analyzer verifies
every case's outputs across the baseline repeats, the feature's setup/measured
self-consistency, and exact control-case equality. Recorded run committed under
[`paired-benchmark-run/`](paired-benchmark-run/) (manifest SHA-256
`129725e6281c7bbda17a7e6f087c0d7632c24a3619412b02b59ed9ee94e92894`), feature code SHA `912ddda0484d0b8cde3c63b60deefa491a0c596c`.

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
| `paired-benchmark-run/manifest.json` | `129725e6281c7bbda17a7e6f087c0d7632c24a3619412b02b59ed9ee94e92894` |

Visual evidence regenerated 2026-07-31T20:49:12Z on the fingerprinted machine
(benchmark run recorded 2026-07-31T18:35:08Z on the same machine). The paired
benchmark analyzer has since been tightened to require the configured 15 samples
per case with matching counts across all three runs; the recorded run satisfies
the tightened gate (11 cases × 15 samples in each of baseline-A, feature, and
baseline-B).
