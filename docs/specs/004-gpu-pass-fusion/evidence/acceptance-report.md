# GPU Pass Fusion Acceptance Report (visual passed / T123 pending)

Recorded on the authoritative Apple M3 / MoltenVK environment
(macOS 26.5.2 build `25F84`, MoltenVK 1.4.0, Metal 3, .NET 10.0.9,
arm64). The current paired visual evidence passed. The final paired benchmark
manifest records `overallAcceptancePassed=false`, so T123 remains pending. No
threshold was relaxed and the failed benchmark was not reclassified as acceptance.

## Current trust anchors

| Artifact or tool | SHA-256 |
|---|---|
| `target-baseline/manifest.json` | `0e2fcd3033e2a18378420c727353cfd4640d3c94b0101bffb85da0d717d62fc5` |
| `target-benchmark/manifest.json` | `60e9ef1f43cdc82db5674e9b8abae9e770bd79245c6d47efa5b6d011bfb30656` |
| `paired-visual-result.json` | `86fd3e92f1bb578f2b404d41c35fea4a61a93b759f5061051a935e59acd91260` |
| paired visual target manifest | `5ad89761a3c2749818dc2fd1a9a908dfb1d8bbe1163927a6f6595183b8a02d5f` |
| paired visual feature manifest | `817861223adb7a25abce24cae7c3b38b25fa5713b7520c34685ab5f611a079af` |
| `paired-benchmark-run/manifest.json` | `f10ab3ba6f03f36621c9e4254d7cb9467481fce301245b75434855e75b2c9303` |
| `target-baseline-generator.patch` | `71da92c5fb25061ed0d588b10b47c539fe7c70d60ee396e8607259e11ddb071e` |
| `generate-target-baseline.sh` | `d45276861aab21bed36c4681067288905784df0fd6742e86dc57eb7fa829d752` |
| `run-paired-visual-evidence.sh` | `b62e3257e45deaaa7e5ca4931aa6ddf5fa39defcae2933d5981a4cd34bbfd401` |
| `run-paired-benchmarks.sh` | `76406cf3b5ffe4c5698559ebb2bbfa069ba757e5156f2a0fc709d038e9496af8` |
| `refresh-intentional-visual-baselines.sh` | `f9ff3831d63cf0f3ed864e20d15731a38b2402b6ac8e0a4c4c0a6860af72d1f2` |
| generator source bundle | `4e6d87526754753978fd2f88c025fe8002dabbd07c6282c39c389fafc8ad24ca` |

The current immutable trust-chain anchors are target visual manifest
`0e2fcd3033e2a18378420c727353cfd4640d3c94b0101bffb85da0d717d62fc5` and
target benchmark manifest
`60e9ef1f43cdc82db5674e9b8abae9e770bd79245c6d47efa5b6d011bfb30656`.

The target visual and target benchmark manifests both identify baseline commit
`83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53`. The benchmark manifest's
`visualManifestSha256` points to the current target visual manifest. The target
benchmark correctly retains paired-visual runner hash
`32b7713a007ec719d839335352ac2a75914b2d179512edd13943ff94f4c64b78`
from its capture rather than claiming that a later visual runner produced the archived
timings.

## Current harness source identity

The paired benchmark manifest embeds complete, sorted
`baselineHarnessFileSha256` and `featureHarnessFileSha256` maps. The load-bearing
entries are reproduced here.

| Current source | SHA-256 |
|---|---|
| `target-benchmark-harness/BenchmarkHarnessProvenance.cs` | `adc24f2ce0afbc60effeb7bd943aba232b1329cddba824802dbf73e9f8c7b534` |
| `target-benchmark-harness/Program.cs` | `7a774812c6fa71dc392554555a98cfc7d3fa905fa5258dcf552fa46cc2ccc4e0` |
| `target-benchmark-harness/TargetRenderPipelineBenchmarks.cs` | `96b82ec12b2650335b891cb4b099b7eb482b436ba50de9d59909d10156c38dae` |
| `target-benchmark-harness/TargetEvidenceFingerprint.cs` | `14a72d4426ec1dd313f991f1b2386317a6300edda6070a71d596e38bd613498a` |
| `target-benchmark-harness/Beutl.GpuPassTargetBenchmarkHarness.csproj` | `8378064e86bcefb3ae29a9176c6bb161e0e8c0f4a614e284f783bc2c9a3d0462` |
| `tests/Beutl.Benchmarks/Rendering/BenchmarkHarnessProvenance.cs` | `19a635cb15246f8b1ac0fa02c6eca11e3a61631651f0c598a1da99515f736788` |
| `tests/Beutl.Benchmarks/Rendering/EvidenceFingerprintRules.cs` | `995bc969b377ef2d291d30ab8f40954454ffaed28ba8cc62c86c815ed796eaf0` |
| `tests/Beutl.Benchmarks/Rendering/FeatureVisualEvidenceExporter.cs` | `4ce197a7130b89057784c7bc9a2af35494d5a6e88ef0cf835bda166f63a44145` |
| `tests/Beutl.Benchmarks/Rendering/PairedBenchmarkAnalyzer.cs` | `2629ab04b1e68cc7c72535a30d0877a93c294e301d14b4c2f9cd25d184ed2af6` |
| `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarkConfig.cs` | `8b9f9950e43a1972c096536131a027efda1e31d99df60edf2a1ce23105b5f19b` |
| `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarkScenes.cs` | `802d41a1756b6a4585d57486cbd42f25c885ee0a4168c1dbda7a395738de231a` |
| `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs` | `23b33a759d574127d1af70fd3a481e6dcaf6f76d0d0a6eca99f370efe7b8f02e` |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Evidence/PairedBenchmarkAnalyzerTests.cs` | `10e3b0c0779f1d1e362469d1266039fd51296b3896f1c818fc46fd906ef8aeeb` |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Evidence/PairedVisualEvidenceArchiveTests.cs` | `d757f216d24f41a527102f21f967107e74d4c864d01bbeaec61d239315b8032b` |
| `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Baseline/GpuPassFusionBaselineTests.cs` | `20843622db38dc9a7c9a966d65681d144b989611b3c65df7800038b208822a96` |

The analyzer binds the exact executed target and feature harness sources, each counter
file, and every setup/measured RGBA16F output blob. Its archive maps cover all 37
files in each lane; the committed paired benchmark contains 112 files including its
root manifest.

On 2026-08-10, the authenticated 112-file archive was reanalyzed after the final
Stack 3 propagation with full-sized overlapping and tail-anchored localized 16×16
windows; partial windows are retained only when an output dimension is smaller than 16.
The analyzer consumed the historical baseline A, feature, and baseline B results for
feature code `01c70637a6fd7d2e34eac18fdc9343dc5b0aaa7f`, their counters and
standard output, and all 66 setup/measured RGBA16F blobs without recollecting timings.
This reanalysis does not relabel the archive as final-Stack-3 or current-HEAD
performance evidence; its raw-producing engine provenance remains historical. The
analyzer produced the current manifest and exited with status 2 only
because the recorded primary, repeat-stability, control/barrier, and overall acceptance
gates remain false. Source provenance, archive integrity, full-image parity, and every
localized gate passed. Compared with the prior analysis, only `analyzedAtUtc` and the
current analyzer feature-harness source hash shown above changed; every raw artifact
hash, benchmark metric, and acceptance result remained identical.

## Paired visual evidence (passed)

The current run compares the pinned baseline with feature commit
`01c70637a6fd7d2e34eac18fdc9343dc5b0aaa7f`. The exact environment
fingerprint gate passed before parity was measured. All 44 scenes passed the
full-image and localized gates:

- linear-light SSIM at least 0.99;
- linear-RGB MAE and alpha MAE at most 0.02;
- every fixed 16×16 window SSIM at least 0.95;
- every fixed 16×16 window alpha MAE at most 0.02 and RGBA MAE at most 0.05.

The worst full-image SSIM was 0.9994304162 and the worst full-image linear-RGB
MAE was 0.0197561748, both for `nested-drawable-brush-delay`. The minimum
window SSIM was 0.9614714800 and maximum window RGBA MAE was 0.0406566560
for the same scene. The maximum window alpha MAE was 0.0043799530 for
`scale-two-control`. The minimum non-vacuity margin was 0.0894911996 for
`geometry-stroke`.

The antialiased edge controls also passed the fixed per-channel error bound of 0.02:

| Scene | Coverage pixels | Coverage-band RGBA MAE | Maximum channel error |
|---|---:|---:|---:|
| `aa-thin-line-color-times-alpha` | 340 | 0.000000667 | 0.000488281 |
| `aa-thin-stroke-color-times-alpha` | 356 | 0 | 0 |

The approved `scene3d-with-2d-tail` semantic refresh was applied through the
canonical refresh path. Its committed nonempty blob SHA-256 is
`8908d30de25b882368b3d9f7e3d355c783ef5f0026b10f1c108e577f067331f6`;
the regenerated legacy blob was
`89d111e13da934fd2c233e48ca07bcae7c41da0d26b6423c0c47d40dca38bede`.

Preview allocation failure is recorded as a successful request with a finite,
nonempty logical bitmap whose pixels are all transparent. Delivery is recorded as a
failed request with no partial output and the exact dimension-bound exception message.
The legacy target requested 172×92 while the feature requested 174×94; the result
records this geometry difference rather than requiring unrelated dimensions to match.

The result and complete input lanes are committed as
[`paired-visual-result.json`](paired-visual-result.json) and
[`paired-visual-run/`](paired-visual-run/). The result was generated at
`2026-08-09T12:10:03.711173Z` and has schema version 2.

## Final persistent-lifetime benchmark (formal acceptance failed)

The current archive was produced against the same baseline and feature revisions as the
visual run. It uses BenchmarkDotNet Monitoring strategy, one discarded baseline warm-up,
then baseline A, feature, and baseline B; each recorded lane has 3 warmups, 15 measured
iterations, one invocation, one launch, and unroll factor 1. The analyzer uses 100,000
deterministic bootstrap resamples at seed 20040719 and confidence level 0.95.

The analyzer completed at `2026-08-09T16:47:38.874227Z` and recorded:

| Formal gate | Result |
|---|---|
| Primary `ShaderOpacityShader` CI entirely below 1.0 | **failed** |
| Every baseline repeat stable within factor 1.20 with CI containing 1.0 | **failed** |
| Every control/barrier case within its repeat-derived tolerance | **failed** |
| `overallAcceptancePassed` | **false** |

The primary feature/pooled-baseline median ratio was 0.8388 with 95% CI
[0.6410, 1.0082]. The upper bound crosses 1.0, so it is not a passing performance
claim.

| Case | Feature / pooled baseline | 95% CI | Repeat factor | Repeat CI contains 1.0 | Control/barrier within tolerance |
|---|---:|---:|---:|:---:|:---:|
| `LongInvariantChain` | 0.4510 | [0.2906, 0.9072] | 2.429 | yes | n/a |
| `MixedSpatialColor` | 1.4091 | [0.9502, 2.1396] | 1.927 | yes | no |
| `MultipleDrawablesTargetDependencies` | 0.3663 | [0.2203, 0.5767] | 1.488 | yes | yes |
| `NoEffectControl` | 2.5578 | [1.2219, 3.5688] | 2.578 | yes | no |
| `ParameterOnlyAnimation` | 0.2930 | [0.1984, 0.3954] | 1.483 | yes | n/a |
| `ShaderOpacityShader` | 0.8388 | [0.6410, 1.0082] | 1.517 | yes | n/a |
| `ShaderOpacityShaderBarrier` | 0.3728 | [0.2396, 0.4892] | 3.283 | no | yes |
| `SingleShader` | 1.1663 | [0.6731, 1.4739] | 2.314 | yes | n/a |
| `SmallObjectFixedOverhead` | 0.5118 | [0.2493, 0.7587] | 1.813 | yes | n/a |
| `StaticPrefixAnimatedTail` | 3.6534 | [2.5641, 5.7052] | 1.762 | no | n/a |
| `StructuralToggle` | 1.1682 | [0.8548, 2.0601] | 2.041 | no | n/a |

The capture was not an idle-host run. A pre-existing unrelated Headless UI testhost
(PID 80622) remained CPU-active throughout, and an unrelated
`dotnet build tests/Beutl.UnitTests` process (PID 39462) overlapped the feature lane.
Neither process was stopped or reprioritized. These observations explain why the result
must not be generalized, but they do not turn the failed formal gates into a pass. The
committed manifest remains the authoritative result and T123 stays pending.

### Raw archive anchors

| Lane | Code SHA | BenchmarkDotNet result SHA-256 | Standard output SHA-256 |
|---|---|---|---|
| baseline A | `83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53` | `3c363df81652827b7b3774fdb42a6fd225a524e7d3f695c3a315ed3e0ffe09ba` | `1429d7aff747c1f69caadcf10e50597f5b472bba8ff623bd2ba0d01a3dc7cd2d` |
| feature | `01c70637a6fd7d2e34eac18fdc9343dc5b0aaa7f` | `7e3535cf5b257bb106ec7c37a354f8cf65d49d519f80830d6e95450dd9c9035c` | `cff5c7744c1fb518285c1b7793df1e723599a281d068784ede3587e4818f596a` |
| baseline B | `83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53` | `c9deda6d8e49adb0125f877875e87b2bd8c6056bed94294e22c79afa6e10cc9f` | `f0672fc8811d0304393dd35a84f90350e5ec8611e4dc7205a7c361a4803fc258` |

Each lane's `benchmarkDotNetArtifactSha256` map in
[`paired-benchmark-run/manifest.json`](paired-benchmark-run/manifest.json)
authenticates its code and command files, raw BenchmarkDotNet result and stdout,
11 schema-3 counter records, and 22 setup/measured RGBA16F output blobs. Those
per-file maps are the canonical complete raw-result hash inventory.

## Acceptance conclusion

Semantic parity, localized parity, non-vacuity, allocation-outcome validation, source
provenance, and archive-integrity gates passed. The final performance manifest did not
pass its primary, repeat-stability, or control/barrier gates. Therefore the evidence is
complete and honest, but formal performance acceptance is not established and T123
remains unchecked.
