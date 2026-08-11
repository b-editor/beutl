# Evidence Contract

Renderer-wide fusion is accepted only with provenance-locked visual evidence, direct plan and execution-shape assertions, failure/lifetime coverage, and paired production-representative benchmarks. Effect-local timing percentages and an implementation-wide telemetry stream are not acceptance evidence.

## Evidence seams

Feature 004 does not add a request-wide diagnostic recorder, completed-request snapshot, event stream, or renderer-owned diagnostic state. It also adds no public provider, sink, writer, or telemetry schema to `IRenderer` or `RenderNodeRenderer`. Correctness invariants remain enforced by the recorder, compiler, executor, request owner, caches, and target pool themselves rather than by a second verification subsystem.

Friend tests and in-tree benchmarks may observe only the narrow state already owned by the component under test:

- the immutable recorded graph, `CompiledRenderRequest`, `ExecutionIslandPlan`, island boundaries, and compiled shader runs before execution;
- component-local `StructuralPlanCacheStatistics`, `ProgramCacheStatistics`, and `RenderTargetPoolStatistics`;
- test-owned target factories, program factories, callback probes, synchronization probes, and failure injectors;
- rendered outputs, published cache entries, surfaced exceptions, and final disposal state.

These observations are read-only and test-scoped. They must not change planning decisions, retain request resources, become cache identity, or be required by production rendering.

### Planning and execution proof

Every execution-shape workload first asserts the compiled topology directly: reachable islands, boundary reasons, shader-run stage order, materialization demands, cache substitutions, and target dependencies. It then executes the request and asserts pixels plus the relevant component statistics or test-owned probe counts. The primary cross-node workload qualifies as one pass only when its plan contains one GPU-pass island with one fused shader run, no prohibited boundary, and execution completes through that island without an extra materialization or synchronization probe.

Exact physical-pass claims apply only to planner-controlled work. A fragment marked as opaque external remains a hard boundary, and a runtime-dynamic callback may determine work only during execution; neither case permits a claim about physical passes or flushes inside the callback. Tests still assert the visible outer boundary, callback-entry behavior, resource lifetime, and output parity without inventing internal counts.

### Lifetime and failure proof

Resource and failure evidence is asserted at the owning component:

- every request-owned acquisition is returned, disposed, or transferred atomically to an accepted cache payload exactly once;
- target-pool leases reject stale or double release, and renderer disposal releases retained pool/program/plan state;
- failed or partial outputs are not published to the render cache;
- cleanup continues after a cleanup fault while preserving the first primary exception;
- retained contexts, handles, sessions, inputs, canvases, and resource tokens reject use after their scope closes;
- metadata-only Bounds and HitTest requests stop before pixel execution and do not mutate persistent frame caches or frame render counts.

There is no universal per-fragment outcome ledger in the final design. Coverage comes from the transaction, planner, cache, executor, pool, and failure-matrix tests that own each invariant, plus the end-to-end visual and benchmark gates below.

## Baseline provenance

The behavioral baseline is target code SHA:

```text
83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53
```

Before scheduling behavior changes, create a new target-specific golden/provenance category with an out-of-tree generator and a paired visual-evidence driver. The committed visual-baseline tooling consists only of:

- `docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch`;
- `docs/specs/004-gpu-pass-fusion/evidence/generate-target-baseline.sh`;
- `docs/specs/004-gpu-pass-fusion/evidence/run-paired-visual-evidence.sh`;
- `docs/specs/004-gpu-pass-fusion/evidence/refresh-intentional-visual-baselines.sh`.

The script creates a temporary clean worktree pinned to the exact baseline SHA, applies the generator patch there, runs it, and copies only immutable raw linear premultiplied RGBA16F blobs plus their manifest into the evidence set. The generator source is never added to or compiled from `tests/` on the feature branch. The manifest contains:

- baseline code SHA and clean repository state;
- SHA-256 hashes of the generator patch, generator script, paired visual-evidence driver, intentional-refresh script, and every blob;
- scene name, dimensions, scale, requested region, seed, and parameter values;
- an exact evidence fingerprint containing OS and version, architecture, graphics backend/API, device vendor/model/identifier, driver, graphics-stack versions, and .NET runtime version;
- workload shape, compiled-plan summary, and any applicable component-local cache/pool statistics;
- allocation-failure behavior for preview and delivery paths;
- benchmark command/environment/raw result reference.

A paired target-baseline comparison is valid only when the baseline and feature runs have byte-identical environment fingerprints. Source provenance is validated separately against each worktree: code SHA and an engine informational/assembly version that embeds that SHA are expected to differ and are not environment-fingerprint fields. `run-paired-visual-evidence.sh` runs the pinned baseline and feature worktrees, compares every required OS/runtime/graphics fingerprint field before invoking the parity oracle, validates each run's source provenance against its own SHA, and records both result sets. The feature manifest's `featureCodeSha` must equal the feature worktree SHA and its `exporterAssemblyVersion` must embed that SHA, independently of the loaded Engine assembly version; both exporter fields are persisted in the paired result. A missing/unknown environment field, environment mismatch, or source-provenance mismatch is a hard evidence-run error, never a skip or a reason to select another device's blob; rerun both worktrees under one matching environment. The evidence runner uses immutable `AssertExisting` behavior. Missing files or hash mismatches fail and are never generated by the implementation under test.

Normal CI does not use a committed device-specific blob as an unconditional visual oracle. It verifies the evidence manifest schema and every patch/script/blob hash on every run, then performs functional visual parity with fusion disabled versus enabled in the same process, backend, device, and runtime. Friend tests select an internal request `FusionMode`; production and public renderer options expose only enabled planning, nested requests inherit the mode, and structural-plan identity includes it so the two schedules cannot alias. CI never silently selects a foreign-fingerprint blob. The dedicated paired evidence run against the pinned starting SHA remains required separately and cannot be replaced by the same-process comparison.

The donor's `004-parity-strong` eight blobs may be imported under a clearly supplemental category only after its historical reproduction script byte-verifies them. Donor `004-baseline` and `004-parity` may inform scene selection but do not replace target provenance. Donor `004-review` is post-redesign evidence and is not a legacy baseline.

## Visual parity

At output scale 1.0, representative baseline comparisons require all of:

- linear-light SSIM >= 0.99;
- linear RGB mean absolute error <= 0.02;
- alpha mean absolute error <= 0.02.

The source/output format is lossless raw linear premultiplied RGBA16F. Metrics must not compare gamma-encoded screenshots. Normal-CI fusion-disabled/enabled comparisons apply the same metrics to their same-process pair.

Antialiased thin-line/thin-stroke workloads additionally compute these metrics over a tight crop containing the coverage edge and record per-channel maximum absolute error over the nontrivial coverage band. The edge-crop SSIM/mean thresholds are the same as above. Normal-CI same-process fusion-disabled/enabled comparisons use the fixed device-independent per-channel maximum-error bound `0.02`; the dedicated paired workflow may additionally apply a tighter bound established from repeated paired-baseline runs and stored in the exact matching fingerprinted manifest. Whole-image averages alone cannot accept an AA coverage case, and normal CI never imports a maximum-error bound from a foreign fingerprint.

Multiple output scales, effective input densities, shifted/cropped/full/empty requested regions, and supported no-preferred-GPU fallback use tolerances freshly recorded and justified with the target baseline. Existing feature-003 golden requirements remain authoritative.

### Non-vacuity

Every parity workload has a control rendering with its operation under test disabled or materially altered. The linear RGB or alpha delta between control and baseline must exceed the applicable parity tolerance plus a recorded margin. A workload whose control remains within parity tolerance cannot prove that operation and is rejected.

## Required workloads

### Primary cross-node proof

```text
deterministic materialized semitransparent RGBA16F source
  -> CurrentPixel Shader A
  -> invariant Opacity render node
  -> CurrentPixel Shader B
  -> root destination
```

The source is already coverage-resolved and enters the chain as a materialized value; this proof therefore exercises one fused shader run without claiming that a nonlinear public stage may cross analytic coverage. The stages remain distinct render nodes. A FilterEffectGroup-only chain does not qualify. The Opacity result must expose `CanBeUsedAsValueInput == true` so Shader B is accepted. After warm-up, the compiled plan must contain exactly one GPU-pass island and one fused program, contain no illegal boundary or opaque-external fragment, and use at most one intermediate target. Program-cache and target-pool statistics must show reuse with no new program or target creation after frame 1, the execution probe must observe no extra synchronization/materialization, and the rendered output must meet the parity gate.

### Boundary controls

For the same source/tail, insert each boundary independently and require the exact split plus parity:

- WholeSource/coordinate-dependent Shader;
- Geometry;
- opaque custom callback;
- explicit readback;
- destination-dependent `Blend` (which remains value-input-ineligible even for a pure child);
- analytic/AA vector, text, or geometry coverage followed by the valid non-coverage-homogeneous public CurrentPixel transform `return color * color.a;`: require coverage-resolving materialization before the shader run and forbid folding that public stage into the coverage-producing draw;
- dynamic expansion;
- external/materialized target;
- cache hit/capture boundary;
- backend/3D result;
- backend Shader budget overflow.

### Target-order and scope controls

- Root `[A, Clear, B]`: exact painter result and recorded fragment/token order; Clear remains in the root scope.
- Public `Layer([A, Clear, B], finiteDomain)`: the same child order on one local target, exactly one outer value, and content bounds clipped to the explicit domain when Clear writes despite empty Clear query bounds.
- Public `TargetLayerScope([A, Clear, B], Full)`: one transparent isolation target, one ordered replay, and one composite onto the current target; the handle is value-input-ineligible and the target is not elided without an equivalence proof. Existing `PushLayer(default)` records this primitive through ordinary bottom-up `Process`. Test `Transform(+10) -> PushLayer(default) -> Full Clear` and nested transform/clip/finite Layer combinations so Full resolves only after every enclosing scope map is known. Run the root form with a real destination and explicit target-less `TargetDomain`, and require a scope-token-lowering/planning failure for target-less Full with neither. Also cover `TargetLayerScope(..., Empty)`: it remains ordered and value-input-ineligible but allocates no target, runs no pixel work, and composites nothing.
- Root `Clear(Full)` with empty QueryBounds: `OutputBounds` equals the resolved root domain, `QueryBounds` and HitTest remain empty, Render commits the full write, and Rasterize returns that full logical domain. Repeat with a finite shifted writer and require the raster result to preserve its logical origin; an empty requested region returns a normal empty result with no bitmap.
- `SnapshotBackdrop -> optional Clear -> DrawBackdrop`: capture once, no implicit capture contribution, then exact later draw under each Blend/transform/filter scope combination. The Clear must lie between the capture and draw.
- Public `TargetCapture -> Shader -> ContributeValues`: one target read/materialization, optional pure fan-out without a second capture, and one explicit contributing draw. Repeat inside a finite Layer/TargetLayerScope whose concrete density exceeds `OutputScale`: require the declared downsampling result for `TargetCaptureScaleContract.MaterializeAtWorkingScale` and `Custom`, then require `PreserveTargetSupply` (including the built-in backdrop) to retain the resolved enclosing density through the Shader input.
- `TargetCommand` target readback versus input readback: pre-command target snapshot excludes callback writes; each declared input snapshot is scheduled only when selected; undeclared `UseSnapshot` throws without synchronization.
- RawTargetScope/RawTargetCommand: the compiled plan retains an opaque-external boundary, skipped work does not enter the callback, executed work enters it exactly once, and request-owned resources are discharged on both paths.

### Visual/scale/region scenes

- strong CurrentPixel color chain over materialized semitransparent content;
- antialiased thin line and thin stroke followed by `return color * color.a;`, with the exact coverage-materialization boundary plus edge-crop and maximum-error parity;
- mixed standard-filter/color/non-identity LUT barriers;
- scaled bitmap and vector/text input combinations from feature 003;
- shifted origin and offset requested region;
- Geometry/opaque/TargetCommand guarded callback canvases at shifted cropped origins: composition-global logical mapping, canonical device rounding, clipping, and zero close-induced synchronization;
- requested region outside source and empty after clipping;
- full-input fallback and sound transform/blur backward ROI;
- forward growth/shrink and runtime Geometry discard/shrink;
- 16,384-axis clamp with device-value late binding;
- supported no-preferred-GPU execution;
- 3D materialized boundary followed by eligible 2D work.

### Cache/animation scenes

- 100 frames of parameter-only Shader animation: one structural compilation total and no program creation after frame 1;
- bounds-changing runtime parameters with unchanged structure: re-resolve bounds/ROI without structural compilation;
- one declared structural toggle: exactly one affected replacement compilation;
- static prefix followed by animated eligible tail: one selected prefix-cache hit, zero executed prefix passes, zero prefix recompilations per warmed frame;
- child-cache hit with ineligible parent and parent-cache hit superseding descendants;
- command-bearing parent cache bypass with pure child value hit while clear/backdrop/readback command order and inputs remain intact;
- cache invalidation for parameter/resource version, bounds, region coverage, density, format, purpose policy, and device recreation;
- opaque/Geometry/target-command state changes between recordings invalidate pixels without recompiling; callers keep reusable state stable after recording, request-local callbacks never hit across requests, and unchanged reusable state may hit. Direct Shader uniform values are included automatically, custom uniform/resource binders are request-unique by default, and `ReuseFromSnapshot` accepts only non-capturing binders while deriving identity from copied values or versioned resource tokens;
- auxiliary/bounds/hit-test request isolation from frame cache and frame render counts, with bounds/hit-test requests stopping before execution.

### Pool/resource scenes

- stable exact-size frames after warm-up: zero new targets and zero pool misses;
- changing-size frames: permitted exact-size misses but no leak/stale lease;
- equivalent 3-stage and 10-stage linear schedules: equal upper bound for peak live intermediates;
- fan-out/merge lifetime where one producer remains live until its last consumer;
- context/device recreation evicts incompatible pooled/program resources;
- `RenderNodeRenderer.Dispose` releases all pooled targets/program/plan state, rejects later calls, and leaves its borrowed root/cache/factory untouched;
- byte-cap/LRU/idle eviction and generation-tag stale/double release detection.

## Failure matrix

Inject failure before/after each of:

- node and `ApplyTo` transaction resource transfer;
- duplicate `Own` of the same raw object, `Own`/`Borrow` conflicts, and repeated `Borrow` identity/version conflicts;
- direct and indirect `RecordNode`/`RecordSubtree` recursion, including a separate-target cycle;
- bounds/ROI mapping;
- cache lookup/substitution and capture staging/publication;
- input materialization and target acquisition;
- Shader source validation, merge, program creation, and runtime binding;
- resource-provider/native child creation;
- Geometry readback, canvas open, callback, shrink/copy, and callback close;
- callback-canvas author `Dispose`, `Snapshot`, nested draw, `SaveLayer`-backed opacity/blend/mask/paint, undeclared native/target use, hidden-allocation, and hidden-flush attempts;
- opaque source/map/combine/expansion execution and dynamic output validation;
- nested request recording/planning/execution;
- backend/3D transition;
- target command/capture/scope/input readback and RawTargetScope/RawTargetCommand callback entry;
- primary target/pool/program/session/resource disposal.
- cache-publication transfer, including acquire/discharge reconciliation while the cache retains its payload.

For every injection:

- zero request-owned target/program/resource/session/handle leaks after teardown;
- before the atomic cache-publication commit, no staged request result or cache publication; direct `Render(destination)` failure pins must instead prove canvas-state restoration, absence of later work, and no cache publication because pixels already committed to a caller-owned destination are not rolled back;
- after the atomic commit, a superseded-cache-storage cleanup failure surfaces as a cleanup exception while the complete replacement set remains published;
- every context/session/input/handle rejects retained use;
- cleanup continues after one cleanup fault;
- the first primary planning/render exception remains the surfaced exception;
- the owning request, cache, program cache, and target pool expose no leaked or still-active state after cleanup.

## Public API and migration gates

`tests/Beutl.PublicApiContractTests` is a non-friend assembly that must compile and execute examples for:

- unchanged plugin-style `FilterEffect.ApplyTo` using existing methods;
- Shader and Geometry using public `FilterEffectContext` only;
- no-output and pass-through nodes;
- source, opacity/semantic map, opaque map, many-to-one combine, runtime N-to-M expansion;
- TargetCommand with separate target/input readback declarations, TargetCapture/ContributeValues, TargetScope, symbolic `TargetLayerScope`, and finite-domain Layer;
- RawTargetScope and RawTargetCommand public authoring and render behavior;
- nested subtree and explicit-input node;
- transferred materialized input;
- cache disablement and custom scale declaration;
- independent `RenderScaleUtilities` plus the absence of forwarding scale helpers on `RenderNodeContext`;
- every `CanBeUsedAsValueInput` propagation row, including eligible `Shader -> Opacity -> Shader` and ineligible pure-child Blend;
- `RenderNodeMeasurement.OutputBounds` versus `QueryBounds`, plus non-empty, shifted-origin, and normal empty `RenderNodeRasterization` ownership/disposal;
- engine-internal recording-bounds progression after Shader/Geometry, absence of the removed public `FilterEffectContext.Bounds` accessor, and append/resource rollback on invalid or throwing forward mappings;
- disposable `Own`, non-disposable `Borrow`/`UseResource`, null-key request-local Borrow identity, and explicit-key/version coalescing;
- transaction rollback and retained-handle/resource rejection;
- guarded opaque fallback and shared `RenderExecutionInput` capability behavior.

A migration census covers compiled `src/**/*.cs` and `tests/**/*.cs`: all 29 production and 7 test `Process` overrides from the baseline, every executable operation subclass/factory, every raw `ImmediateCanvas` author hook, every `RenderNodeProcessor` pull/rasterize consumer, and every legacy static scale-helper caller. Historical symbol text in `docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch` is deliberately outside this compiled-source census. The gate fails if a returning override, executable `RenderNodeOperation`, `Pull`/`PullToRoot`, list rasterizer, `OperationWrapperRenderNode.SetOperations`, `EffectTarget.NodeOperation`, `EffectTarget(RenderNodeOperation)`, isolated nested processor, independent cache-generation pull, unclassified `CreateLambda`/raw callback, or reference to `RenderNodeContext.MaxBufferDimension`, `RenderNodeContext.SanitizeMaxWorkingScale`, `RenderNodeContext.ResolveWorkingScale`, or `RenderNodeContext.ClampWorkingScaleToBufferBudget` remains in the compiled scope.

Friend Engine tests—not the non-friend public-contract project—assert the raw forms' opaque-external plan boundary, callback-entry probe, cache bypass, and resource cleanup directly.

## Benchmark contract

Use BenchmarkDotNet with persistent production-equivalent renderer/node/cache/pool lifetime. Setup constructs deterministic source data with a fixed seed and warms the renderer; an iteration renders a complete target-surface request, not an isolated descriptor executor.

The starting SHA does not contain the final 11-case harness. Benchmark provenance therefore uses the separately committed `docs/specs/004-gpu-pass-fusion/evidence/run-paired-benchmarks.sh` runner and these four external harness files:

- `docs/specs/004-gpu-pass-fusion/evidence/target-benchmark-harness/Beutl.GpuPassTargetBenchmarkHarness.csproj`;
- `docs/specs/004-gpu-pass-fusion/evidence/target-benchmark-harness/Program.cs`;
- `docs/specs/004-gpu-pass-fusion/evidence/target-benchmark-harness/TargetEvidenceFingerprint.cs`;
- `docs/specs/004-gpu-pass-fusion/evidence/target-benchmark-harness/TargetRenderPipelineBenchmarks.cs`.

The external harness takes a read-only project reference to `src/Beutl.Engine/Beutl.Engine.csproj` in the clean starting-SHA worktree. It must not copy, patch, or generate source or build files inside that worktree or the feature worktree. Restore, build, and BenchmarkDotNet-generated executable outputs live only under a runner-owned temporary directory and are removed after the paired run. The paired manifest records the SHA-256 hash of the runner and of every external harness file, in addition to both code SHAs, Engine assembly provenance, commands, raw results, workload-shape observations, applicable component statistics, and the exact environment fingerprint. These benchmark-tool hashes are independent of the visual generator patch/script/runner hashes and must not be substituted for them.

Required cases:

- no-effect control;
- single eligible Shader;
- primary `Shader A -> Opacity -> Shader B` cross-node chain;
- same chain with a hard barrier;
- long eligible invariant chain;
- parameter-only animation;
- structural toggle;
- static-prefix/animated-tail cache scene;
- mixed spatial/color chain;
- small-object/fixed-overhead scene;
- multiple top-level drawables with target dependencies.

Compare pinned baseline and feature worktrees in the same machine/session with identical runtime, backend/device, dimensions, warm-up, renderer lifetime, scene, and output verification. Every setup and final measured output contract for every feature workload must match the independently executed starting-SHA baseline; a second feature replay supplies repeatability evidence but is not the semantic oracle. Preserve raw BenchmarkDotNet results and every observation each unmodified engine can expose. The external starting-SHA harness records legacy workload shape derived from pulled operations and immutable scene declarations, and explicitly records final structural-plan/program-cache/target-pool statistics as unavailable on that SHA. The feature harness records compiled-plan shape plus component-local structural-plan, program-cache, and target-pool statistics. It applies the same scene-specific workload-shape invariants to both setup and the replay of the final measured request. A feature-only statistic is not fabricated for the baseline or treated as a cross-version numeric pair; the paired manifest preserves the two native evidence shapes independently and validates each against its own engine.

Acceptance for the primary warmed cross-node workload is a post/pre median frame-time ratio whose 95% confidence interval lies entirely below 1.0. Controls and barrier cases must remain within the measurement tolerance established by repeated baseline runs. No absolute milliseconds or historical donor percentage is normative.

## Verification commands

At completion, the evidence run includes:

```bash
dotnet format Beutl.slnx --verify-no-changes
dotnet build Beutl.slnx
dotnet test Beutl.slnx -f net10.0 --settings coverlet.runsettings
BEUTL_REQUIRE_GPU=1 dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj \
  -f net10.0 \
  --filter "(TestCategory=GpuPassFusionGpu|FullyQualifiedName~GpuGoldenSuiteCanaryTests)"
dotnet test tests/Beutl.UnitTests/Beutl.UnitTests.csproj \
  -f net10.0 --filter "FullyQualifiedName~ShaderFallbackTests"
BEUTL_REQUIRE_GPU=1 dotnet test tests/Beutl.Graphics3DTests/Beutl.Graphics3DTests.csproj \
  -f net10.0 --filter "TestCategory=GpuPassFusionGpu"
dotnet run -c Release --project tests/Beutl.Benchmarks -- \
  --filter '*RenderPipelineBenchmarks*'

# Mandatory paired visual gate. The output path must not exist yet; the feature
# exporter command must produce manifest.json plus row-packed *.rgba16f files.
docs/specs/004-gpu-pass-fusion/evidence/run-paired-visual-evidence.sh \
  --feature-worktree <clean-feature-worktree> \
  --output-dir <nonexistent-create-only-visual-output-dir> \
  --feature-command '<feature-export-command>'

# Mandatory external paired benchmark gate. The baseline worktree must be clean
# and pinned to the actual pre-feature parent; the output directory must already
# exist and be empty.
test "$(git -C <clean-baseline-worktree> rev-parse HEAD)" = \
  83e63689d8c72bd0b7fbd4cb01d9e468d7a78c53
docs/specs/004-gpu-pass-fusion/evidence/run-paired-benchmarks.sh \
  <clean-baseline-worktree> \
  <clean-feature-worktree> \
  <existing-empty-benchmark-output-directory>
```

Tests selected by `TestCategory=GpuPassFusionGpu` carry the NUnit `GpuPassFusionGpu` category. The two hardware-required commands run on a capable GPU/Vulkan or configured software device; the UnitTests canary prevents a vacuous category run, and the Graphics3D project is invoked separately rather than assumed to be selected through the UnitTests assembly. `ShaderFallbackTests` and ordinary fallback/public-contract tests must pass independently of either GPU gate and must not skip for lack of a preferred GPU.

The paired visual runner regenerates the target from the pinned starting SHA, passes `BEUTL_GPU_PASS_EVIDENCE_OUTPUT_DIR`, `BEUTL_GPU_PASS_TARGET_OUTPUT_DIR`, `BEUTL_GPU_PASS_BASELINE_MANIFEST`, `BEUTL_GPU_PASS_EVIDENCE_MODE=feature`, and `BEUTL_REQUIRE_GPU=1` to the feature command, and hard-fails source/environment/output parity. The paired benchmark runner owns the 100,000-resample paired bootstrap and 95% confidence interval. Completion fails unless the primary warmed `ShaderOpacityShader` feature/pooled-stable-baseline A+B median-ratio interval has upper bound below `1.0`, the baseline-repeat interval contains `1.0` with symmetric factor at most `1.20`, every control/barrier interval stays below its case-specific repeat factor, and every schema, provenance, and output-parity gate passes. The in-tree BenchmarkDotNet command alone satisfies none of these paired gates.
