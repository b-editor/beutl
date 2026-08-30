# Feature 004 evidence

Everything here exists so a reader can re-run SC-007 and SC-008 and get a comparable answer, instead of taking
a number reported once in a conversation. Two things make a run comparable: an **environment fingerprint** that
says what machine and device produced it, and a **manifest** that says what was compared and under which rules.

## Environment fingerprint

`tests/Beutl.Benchmarks/Evidence/RenderEvidenceFingerprint.cs` captures the identity of the machine, GPU,
driver, and build a run was produced on, and reduces it to a **comparability key** — a SHA-256 over exactly the
fields that change what the renderer produces or how fast it produces it:

| Group | Fields |
|---|---|
| Device | Vulkan vendor/device id, device type and name, device and driver UUID, driver id/name/info/version, API version, enabled extensions |
| Limits | `maxAttachmentDimension` — feature 003's per-buffer clamp reads it, so two devices that disagree here render the same scene at different working densities |
| Platform | OS description and architecture, process architecture, runtime identifier, framework description |
| Build | build configuration, SkiaSharp managed and native versions, Silk.NET.Vulkan version |
| Backend | renderer backend, Skia backend, device-selection policy |

`beutlEngineAssemblyVersion` and `beutlEngineSourceRevision` are recorded but deliberately **outside** the key:
a cross-build paired run compares two engine builds on one machine, so the engine's own version must not be the
thing that makes the two sides look incomparable.

Capture starts no child process and calls into no platform UI framework, because it runs inside NUnit fixtures
and inside BenchmarkDotNet setup. On macOS the Vulkan identity is MoltenVK's view of the same Metal device
Skia draws with, so the Vulkan block identifies the GPU on every supported platform. A field that comes back
blank or literally "unknown" fails the capture rather than producing a fingerprint that reads as comparable;
the sole exception is `vulkanDriverInfo` on a CPU device, where a software rasterizer has no driver build to
report (`EvidenceFingerprintRules`).

## SC-007 — pixel parity

```bash
./docs/specs/004-gpu-pass-fusion/evidence/run-parity-evidence.sh
```

Requires a machine with a working Vulkan device (hardware, MoltenVK, or SwiftShader). Writes
`sc-007-parity-manifest.json`: the fingerprint, the commit, the thresholds, and one record per compared case
carrying its SSIM, minimum-window SSIM, linear-RGB MAE, alpha MAE, output scale, output dimensions, and — for
a workload that declares an antialiased crop — its edge-band mean error and per-channel maxima. Metrics are
recorded **before** the assertions run, so a workload that missed a threshold appears in the manifest as a
failure instead of vanishing from it. The script exits non-zero when the manifest carries no fingerprint, when
nothing was compared, or when any case failed.

The comparison is the **same-process fusion-disabled versus fusion-enabled A/B** in
`GpuPassFusionSameProcessParityHarness`, recorded as `comparisonMode: same-process-fusion-disabled-vs-enabled`.
Both sides run in one process on one device, so neither needs a committed device-specific reference blob.

**What this is not.** SC-007's first clause names a comparison against *provenance-verified current-main
references*. That is a different comparison, and it is not reproducible from this tree. It needs the same
content corpus rendered by a **pre-feature engine build**, and this branch's parity workloads are written
against this branch's recording contract (`void RenderNode.Process`, `RenderNodeContext`), so they cannot be
compiled against a pre-feature engine. Producing it requires a target-side generator — a corpus exporter
written against the target commit's own API — which this tree does not carry. The manifest schema reserves
`comparisonMode: differential-against-target-commit` for a run that does have one.

## SC-008 — performance

```bash
# Both sides from this build: baseline runs with fusion disabled, feature run with it enabled.
./docs/specs/004-gpu-pass-fusion/evidence/run-paired-benchmarks.sh --mode fusion

# Two engine builds, if the baseline ref carries a compatible benchmark harness.
./docs/specs/004-gpu-pass-fusion/evidence/run-paired-benchmarks.sh --mode worktree --baseline-ref <ref>
```

Writes `sc-008-paired-benchmark-manifest.json`. Exit code 0 means the manifest was written and its acceptance
passed, 2 means it was written and the acceptance failed, 1 means the run could not be completed.

The runner executes **baseline A → feature → baseline B** on one machine and hands the three BenchmarkDotNet
full reports to `PairedBenchmarkAnalyzer`, which implements the SC-008 method without deviation:

1. Each run must supply exactly **15** finite positive `Statistics.OriginalValues` samples per case — the raw
   values, not BenchmarkDotNet's outlier-classified summary, and with no outlier removal, clipping, or
   winsorization of the analyzer's own.
2. Per case, `median(B) / median(A)` is bootstrapped **100,000** times from the two 15-sample runs. Its
   linearly interpolated 95% interval must contain 1.0 and its symmetric factor
   `max(upper, 1 / lower)` must be at most **1.20**. Drift between the two baseline runs therefore fails the
   run instead of being attributed to the feature. Every case must clear this gate before any pooling.
3. The 30 baseline samples are pooled; the pool and the feature samples are independently resampled with
   replacement 100,000 times, and the 2.5th and 97.5th percentiles of
   `median(feature resample) / median(baseline resample)` are the 95% interval. The point estimate is
   `median(feature) / median(pooled baseline)`.
4. The primary case passes when its interval lies entirely below 1.0. Control and barrier cases pass when
   their interval's upper bound is at most that case's own unclipped repeat tolerance factor.
5. The three runs' fingerprints must share a comparability key. A result that cannot be shown to come from one
   machine is not an accepted result, whatever its interval says.

Seeding is `20040719` combined with the case name's FNV-1a 32-bit hash, xored with a fixed constant for the
repeat analysis. The resampler is xoshiro256** seeded through SplitMix64, written out in
`DeterministicBootstrapRandom` rather than delegated to `System.Random`, so the recorded interval does not
depend on one runtime's implementation detail.

**Which baseline you get.** `--mode fusion` measures this branch's renderer with the fusion optimizer switched
off against the same renderer with it on. That is a real paired measurement of the optimization, but it is a
**weaker claim** than SC-008's "post-feature / pre-feature" wording, and the manifest says so in
`comparisonMode`. `--mode worktree` gives the literal comparison, but needs a benchmark harness that compiles
against both engine builds; this tree's harness is written against this branch's recording contract, so the
script checks the baseline ref up front and reports exactly what is missing rather than measuring something
else. As of this commit no ancestor of this branch carries such a harness.

## Analysing existing runs

`analyze-paired` is a verb of the benchmark executable, so a manifest can be recomputed from reports that are
already on disk without re-measuring:

```bash
dotnet run -c Release --project tests/Beutl.Benchmarks -- analyze-paired \
    --baseline-a <dir> --feature <dir> --baseline-b <dir> --output <manifest.json> \
    [--primary-case <name>] [--control-case <name>]... [--comparison-mode <text>]
```

Each run directory must hold exactly one `*-report-full.json` and, to prove the runs are comparable, a
`counters/` directory written by `RenderPipelineBenchmarks`.

## The runs of record

Both manifests here were produced on one machine — Apple M3 under MoltenVK 1.4.0, `maxAttachmentDimension`
16384, .NET 10.0.11, macOS 26.6.2, Release — and each carries its own fingerprint, so a run elsewhere is
comparable only when the comparability keys match. A re-run overwrites the file. The SC-008 provenance records
each run's directory *name* and the SHA-256 of its report and counter files rather than an absolute path, which
would be machine-local and usually a temporary directory that no longer exists.

`sc-007-parity-manifest.json` — three same-process comparisons, 3/3 passed, byte-identical on this device
(SSIM 1.0, linear-RGB and alpha MAE 0, and an antialiased-crop edge-band error of 0 on the thin-stroke case).

`sc-008-paired-benchmark-manifest.json` — sixteen workloads, `overallAcceptancePassed: false`. **That is the
gate working, not the measurement failing.** `LayerCustomEffect`'s baseline-repeat 95% interval
`[0.8825, 0.9340]` does not contain 1.0: the machine itself moved between the two baseline runs, and SC-008's
rule that every case must clear the repeat gate before pooling therefore refuses the whole run. The per-case
ratios it records — `ShaderOpacityShader` at `0.760` with a 95% interval of `[0.678, 0.773]`,
`LongInvariantChain` at `0.456`, `NoEffectControl` at `0.965` — are informative but are **not** an accepted
result and must not be cited as one. Reproducing an accepted run needs a machine that is otherwise idle.

## Tests

`tests/Beutl.UnitTests/Engine/Graphics/Rendering/Evidence/` pins all of the above without needing a GPU: the
analyzer's constants, median and percentile definitions, seed derivation, bootstrap reproducibility, the
repeat-stability and sample-count gates, the fingerprint's comparability key and blank-field rules, and the
parity manifest's worst-result-wins accumulation.
