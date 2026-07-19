# Diagnostics and Evidence Contract

Renderer-wide fusion is accepted only with complete-request accounting, provenance-locked visual evidence, failure/lifetime coverage, and paired production-representative benchmarks. Effect-local counters and donor timing percentages cannot establish success.

## Request-wide diagnostics

Diagnostics are an internal renderer/evidence seam in this feature. No public provider, sink, writer, snapshot factory, or telemetry schema is added to `IRenderer` or `RenderNodeRenderer`. Instrumentation must not change planning decisions, retain request resources, or require a GPU. Friend tests and in-tree benchmarks observe immutable completed snapshots through internal test hooks.

The normative internal shape is:

```csharp
namespace Beutl.Graphics.Rendering;

internal interface IRenderPipelineDiagnosticsState
{
    RenderPipelineDiagnosticSnapshot Latest { get; }
    RenderPipelineDiagnosticSnapshot LatestFrame { get; }
    event Action<RenderPipelineDiagnosticSnapshot>? RequestCompleted;

    void Reset();
    void Complete(RenderPipelineDiagnosticSnapshot snapshot);
}

internal sealed class RenderPipelineDiagnosticSnapshot
{
    internal static RenderPipelineDiagnosticSnapshot Empty { get; }

    internal static RenderPipelineDiagnosticSnapshot Create(
        long requestId,
        long? parentRequestId,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        bool succeeded,
        bool hasOpaqueExternalWork,
        string rootTargetClass,
        RenderPipelineFailurePhase? failurePhase,
        IReadOnlyDictionary<RenderPipelineCounter, long> counters,
        IEnumerable<RenderPipelineDiagnosticEvent> events);

    internal long RequestId { get; }
    internal long? ParentRequestId { get; }
    internal RenderIntent Intent { get; }
    internal RenderRequestPurpose Purpose { get; }
    internal bool Succeeded { get; }
    internal bool HasOpaqueExternalWork { get; }
    internal string RootTargetClass { get; }
    internal RenderPipelineFailurePhase? FailurePhase { get; }
    internal IReadOnlyDictionary<RenderPipelineCounter, long> Counters { get; }
    internal IReadOnlyList<RenderPipelineDiagnosticEvent> Events { get; }

    internal long this[RenderPipelineCounter counter] { get; }
}

internal readonly record struct RenderPipelineDiagnosticEvent(
    long Sequence,
    RenderPipelineDiagnosticEventKind Kind,
    long SubjectId,
    long? RelatedRequestId,
    RenderPipelineBoundaryReason? BoundaryReason,
    RenderPipelineOutcome? Outcome,
    RenderPipelineFailurePhase? FailurePhase);

internal enum RenderPipelineDiagnosticEventKind
{
    RequestStarted,
    FragmentRecorded,
    BoundaryPlanned,
    PassPlanned,
    SynchronizationPlanned,
    BackendTransitionPlanned,
    CacheDecision,
    PassExecuted,
    SynchronizationExecuted,
    BackendTransitionExecuted,
    CacheCapturePublished,
    OutcomeAssigned,
    NestedRequest,
    Failure,
    CleanupFailure,
    RequestCompleted,
}

internal enum RenderPipelineBoundaryReason
{
    Opaque,
    Geometry,
    CacheInput,
    CacheCapture,
    TargetCommand,
    TargetCapture,
    TargetScope,
    Layer,
    Readback,
    UnsafeComposite,
    LegacyRawCanvas,
    BackendTransition,
    DynamicTopology,
    BackendLimit,
    ThreeD,
    LegacyCustomEffect,
}

internal enum RenderPipelineOutcome
{
    Executed,
    Cached,
    Metadata,
    Skipped,
    Failed,
}

internal enum RenderPipelineFailurePhase
{
    Recording,
    Metadata,
    RegionAnalysis,
    CacheResolution,
    Planning,
    ProgramCompilation,
    Binding,
    Allocation,
    Execution,
    CachePublication,
    Cleanup,
}

internal enum RenderPipelineCounter
{
    RecordedFragments,
    RecordedMaterializableValues,
    RecordedTargetCommands,
    RecordedTargetCaptures,
    RecordedTargetScopes,
    RecordedLayers,
    PlannedGpuPasses,
    ExecutedGpuPasses,
    FusedStages,
    ExecutionIslands,
    IntermediateAcquires,
    IntermediateCreates,
    IntermediateDischarges,
    PoolHits,
    PoolMisses,
    PeakLiveIntermediates,
    FullFrameMaterializations,
    RoiMaterializations,
    Synchronizations,
    PlannedBackendTransitions,
    ExecutedBackendTransitions,
    StructuralPlanCompilations,
    StructuralPlanHits,
    StructuralPlanMisses,
    ProgramCreations,
    ProgramHits,
    ProgramMisses,
    RenderCacheHits,
    RenderCacheMisses,
    RenderCacheCaptures,
    RejectedRenderCacheCaptures,
    OpaqueBoundaries,
    OpaqueExternalExecutions,
    Opaque3DBoundaries,
    ExecutedOutcomes,
    CachedOutcomes,
    MetadataOutcomes,
    SkippedOutcomes,
    FailedOutcomes,
    Failures,
    CleanupFailures,
    ExternalRootResources,
}
```

The internal state is thread-safe. `Latest` is the most recently completed request of any purpose; `LatestFrame` is the most recently completed request whose purpose is `Frame`. A new instance exposes `RenderPipelineDiagnosticSnapshot.Empty` from both properties until the corresponding completion. The validating factory copies the counter dictionary and event sequence; it rejects non-positive request IDs, invalid parent IDs, empty target classes, negative counters, non-gap-free event sequences, invalid optional-field/event-kind combinations, outcome or acquire/discharge reconciliation failure, and inconsistent success/failure phase/count/external-work state. Missing counter keys read as zero through the indexer.

`Reset` atomically returns both `Latest` and `LatestFrame` to `Empty` without cancelling or mutating an in-flight request and does not remove internal test subscribers; later completions repopulate them according to purpose. Observer failures are isolated and cannot change or replace the render outcome.

Snapshots, dictionaries, and event lists are immutable and contain numeric/string/enum identity data only. Events use a zero-based, gap-free sequence within one request; `SubjectId == 0` denotes a request-level event. Only `BoundaryPlanned` carries `BoundaryReason`, only `OutcomeAssigned` carries `Outcome`, only failure/cleanup-failure events carry `FailurePhase`, and only `NestedRequest` carries `RelatedRequestId`; other optional fields must be null. Planned events precede corresponding executed events. Each child request also receives its own snapshot and parent ID. No snapshot retains native targets, programs, contexts, resources, callbacks, exceptions, or author objects.

### Required counters

| Counter | Definition |
|---|---|
| `RecordedFragments` | Every committed execution-relevant fragment. This is the sole denominator for terminal outcome reconciliation. |
| `RecordedMaterializableValues` | Materializable value records exposed by committed fragments, including ordinary, effect, materialized, opaque, nested, capture, and backend values. This is a non-exclusive classification counter. |
| `RecordedTargetCommands` | Committed guarded/raw clear/draw/readback/presentation command fragments. Non-exclusive with containing sequence/Layer classifications. |
| `RecordedTargetCaptures` | Committed target-token-to-value capture fragments. |
| `RecordedTargetScopes` | Committed guarded typed or raw same-target scope fragments. |
| `RecordedLayers` | Committed local-target Layer fragments. |
| `PlannedGpuPasses` | Planner-controlled GPU draw/dispatch passes in the compiled request, including new opaque/Geometry compatibility islands but excluding internals of legacy external callbacks. |
| `ExecutedGpuPasses` | Planner-controlled planned GPU passes that actually execute. Cache/skipped work and legacy callback internals are excluded. |
| `FusedStages` | Semantic stages executed inside multi-stage compiled runs. |
| `ExecutionIslands` | Planned cache/backend/lifetime islands. |
| `IntermediateAcquires` | Planner requests for an intermediate lease. |
| `IntermediateCreates` | New target allocations, excluding external root/presentation targets. |
| `IntermediateDischarges` | Planner ownership discharges: return to a pool, direct disposal/eviction, or successful transfer of a staged capture into `RenderNodeCache`. After teardown/publication this equals `IntermediateAcquires` for every request, including failures. |
| `PoolHits` / `PoolMisses` | Exact-size compatible acquire outcomes. |
| `PeakLiveIntermediates` | Maximum simultaneous planner-owned intermediate leases. |
| `FullFrameMaterializations` | Complete-input materializations forced by a barrier/full-input contract. |
| `RoiMaterializations` | Materializations restricted by resolved requested region. |
| `Synchronizations` | Actual planner-controlled explicit flush/readback/backend synchronization points; opaque external internals are excluded. |
| `PlannedBackendTransitions` | Backend ownership/execution transitions present in the compiled schedule. |
| `ExecutedBackendTransitions` | Planned backend transitions actually performed before completion/failure. |
| `StructuralPlanCompilations` | New compiled structural request plans. |
| `StructuralPlanHits` / `Misses` | Structural-plan cache outcomes. |
| `ProgramCreations` | Native Shader program compilations/creations. |
| `ProgramHits` / `Misses` | Program-cache outcomes after full-key equality. |
| `RenderCacheHits` / `Misses` | Selected output-cache island outcomes, not merely lookup attempts. |
| `RenderCacheCaptures` | Successfully published captures. Failed/staged-only captures are separate. |
| `RejectedRenderCacheCaptures` | Staged captures rejected because the complete request, validation, or cleanup did not succeed. |
| `OpaqueBoundaries` | Aggregate opaque-island count; `Events.BoundaryReason` supplies per-reason classification. |
| `OpaqueExternalExecutions` | Invoked `LegacyCustomEffect` or `LegacyRawCanvas` callbacks whose internal raw target/canvas work is not planner-attributable. This increments on callback entry only. |
| `Opaque3DBoundaries` | 3D-to-2D materialized backend boundaries. |
| `ExecutedOutcomes` | Committed fragments whose planned work executed successfully. |
| `CachedOutcomes` | Committed fragments satisfied by a selected render-cache entry. |
| `MetadataOutcomes` | Committed fragments resolved without pixel execution by a `Bounds` or `HitTest` request. |
| `SkippedOutcomes` | Committed fragments omitted because no required consumer/region remained, including failure-dependent skips. |
| `FailedOutcomes` | Committed fragments assigned the primary or dependent failure outcome. |
| `Failures` | Primary failure count (zero or one); `FailurePhase` and failure events supply phase classification. A cleanup-only first fault becomes the primary `Cleanup` failure. |
| `CleanupFailures` | Every cleanup fault. With an earlier primary they remain secondary; without one, the first is also represented by `Failures = 1` / `FailurePhase = Cleanup`. |
| `ExternalRootResources` | Externally owned root/presentation targets, classified separately from intermediates. |

When `HasOpaqueExternalWork` is false, planned/executed pass, synchronization, transition, acquire, and discharge counters are exact for the complete request. Existing `FilterEffectContext.CustomEffect` lowers to `LegacyCustomEffect`; every retained raw `IBackdrop`/audio/custom canvas hook plus `RawTargetScope`/`RawTargetCommand` lowers to `LegacyRawCanvas`. Recording any such fragment sets `HasOpaqueExternalWork` even when ROI/failure later skips callback invocation. The planner counts the boundary and every resource/synchronization it controls around it, but cannot claim the callback's internal physical GPU pass or flush count. `OpaqueExternalExecutions` increments only on callback entry. A snapshot with opaque external work must not support an exact whole-request physical-pass claim, while fragment outcome reconciliation remains complete.

### Outcome reconciliation

Every committed execution-relevant fragment receives exactly one terminal outcome:

- executed;
- satisfied by selected cache;
- resolved as metadata without pixel execution for a bounds/hit-test request;
- skipped because no required consumer/region remains;
- failed or skipped due to an upstream/primary failure.

For every completed or failed request:

```text
RecordedFragments
  == ExecutedOutcomes + CachedOutcomes + MetadataOutcomes
     + SkippedOutcomes + FailedOutcomes
```

`RecordedMaterializableValues`, `RecordedTargetCommands`, `RecordedTargetCaptures`, `RecordedTargetScopes`, and `RecordedLayers` are overlapping classifications and never appear on the left side of this equation. A mixed or nested fragment may increment several of them without receiving more than one outcome.

Nested requests are visible both as parent nested-request events and as their own internally reconciled scopes; their fragments are not double-counted in one request total. Externally owned resources never increment intermediate acquire/discharge counters.

After cleanup and any successful cache-publication transfer, `IntermediateAcquires == IntermediateDischarges` for the request scope. A cache transfer discharges request/planner ownership even though `RenderNodeCache` retains the payload afterward. A cleanup fault always prevents cache publication. If no earlier primary failure exists, the first cleanup fault makes `Succeeded == false`, `Failures == 1`, and `FailurePhase == Cleanup`; every cleanup fault increments `CleanupFailures`. With an earlier primary, that phase remains primary and cleanup faults are secondary events/counters.

### Event ordering

Diagnostics record request ID, parent request ID, purpose, intent, root target identity class, and monotonic event sequence. Planned events precede their execution outcomes. Cache publication follows complete request success. Cleanup/failure events remain available after request disposal without retaining native objects. `Bounds` and `HitTest` requests still publish their own immutable snapshots and completion events, with every committed record assigned `Metadata`; they may replace `Latest` but never `LatestFrame`, persistent frame caches, frame render counts, or the counters stored in a completed frame snapshot.

## Baseline provenance

The behavioral baseline is target code SHA:

```text
43a38e665d9bf52548161a3917e748bd1457ff55
```

Before scheduling behavior changes, create a new target-specific golden/provenance category with an out-of-tree generator and a paired visual-evidence driver. The committed evidence tooling consists only of:

- `docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch`;
- `docs/specs/004-gpu-pass-fusion/evidence/generate-target-baseline.sh`;
- `docs/specs/004-gpu-pass-fusion/evidence/run-paired-visual-evidence.sh`.

The script creates a temporary clean worktree pinned to the exact baseline SHA, applies the generator patch there, runs it, and copies only immutable raw linear premultiplied RGBA16F blobs plus their manifest into the evidence set. The generator source is never added to or compiled from `tests/` on the feature branch. The manifest contains:

- baseline code SHA and clean repository state;
- SHA-256 hashes of the generator patch, generator script, paired visual-evidence driver, and every blob;
- scene name, dimensions, scale, requested region, seed, and parameter values;
- an exact evidence fingerprint containing OS and version, architecture, graphics backend/API, device vendor/model/identifier, driver, graphics-stack versions, and .NET runtime version;
- request-wide counter snapshot;
- allocation-failure behavior for preview and delivery paths;
- benchmark command/environment/raw result reference.

A paired target-baseline comparison is valid only when the baseline and feature runs have byte-identical evidence fingerprints. `run-paired-visual-evidence.sh` runs the pinned baseline and feature worktrees, compares every required fingerprint field before invoking the parity oracle, and records both result sets. A missing/unknown field or mismatch is a hard evidence-run error, never a skip or a reason to select another device's blob; rerun both worktrees under one matching environment. The evidence runner uses immutable `AssertExisting` behavior. Missing files or hash mismatches fail and are never generated by the implementation under test.

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
  -> CurrentPixel Shader A (Gamma)
  -> invariant Opacity render node
  -> CurrentPixel Shader B (Invert)
  -> root destination
```

The source is already coverage-resolved and enters the chain as a materialized value; this proof therefore exercises one fused shader run without claiming that a nonlinear public stage may cross analytic coverage. The stages remain distinct render nodes. A FilterEffectGroup-only chain does not qualify. The Opacity result must expose `CanBeUsedAsValueInput == true` so Shader B is accepted. After warm-up, the eligible run must report `HasOpaqueExternalWork == false`, `OpaqueExternalExecutions == 0`, exactly one planned/executed GPU pass, one compiled fused program selected through a cache hit, no new `ProgramCreations` after frame 1, no illegal boundary, at most one intermediate target, and visual parity.

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

- Root `[A, Clear, B]`: exact painter result and fragment/token event order; Clear remains in the root scope.
- Public `Layer([A, Clear, B], finiteDomain)`: the same child order on one local target, exactly one outer value, and content bounds clipped to the explicit domain when Clear writes despite empty Clear query bounds.
- Public `TargetLayerScope([A, Clear, B], Full)`: one transparent isolation target, one ordered replay, and one composite onto the current target; the handle is value-input-ineligible and the target is not elided without an equivalence proof. Existing `PushLayer(default)` records this primitive through ordinary bottom-up `Process`. Test `Transform(+10) -> PushLayer(default) -> Full Clear` and nested transform/clip/finite Layer combinations so Full resolves only after every enclosing scope map is known. Run the root form with a real destination and explicit target-less `TargetDomain`, and require a scope-token-lowering/planning failure for target-less Full with neither. Also cover `TargetLayerScope(..., Empty)`: it remains ordered and value-input-ineligible but allocates no target, runs no pixel work, and composites nothing.
- Root `Clear(Full)` with empty QueryBounds: `OutputBounds` equals the resolved root domain, `QueryBounds` and HitTest remain empty, Render commits the full write, and Rasterize returns that full logical domain. Repeat with a finite shifted writer and require the raster result to preserve its logical origin; an empty requested region returns a normal empty result with no bitmap.
- `SnapshotBackdrop -> optional Clear -> DrawBackdrop`: capture once, no implicit capture contribution, then exact later draw under each Blend/transform/filter scope combination. The Clear must lie between the capture and draw.
- Public `TargetCapture -> Shader -> ContributeValues`: one target read/materialization, optional pure fan-out without a second capture, and one explicit contributing draw. Repeat inside a finite Layer/TargetLayerScope whose concrete density exceeds the capture's output-derived density and require the declared downsampling result; neither `MaterializeAtWorkingScale` nor `Custom` may inherit that enclosing density. The engine-internal backdrop control may late-bind it.
- `TargetCommand` target readback versus input readback: pre-command target snapshot excludes callback writes; each declared input snapshot is separately scheduled/counted; undeclared `UseSnapshot` throws without synchronization.
- RawTargetScope/RawTargetCommand: fragment/outcome/resource counters reconcile, `HasOpaqueExternalWork` is true even when execution is skipped, and `OpaqueExternalExecutions` changes only on callback entry.

### Visual/scale/region scenes

- strong CurrentPixel color chain over materialized semitransparent content;
- antialiased thin line and thin stroke followed by `return color * color.a;`, with the exact coverage-materialization boundary plus edge-crop and maximum-error parity;
- mixed Blur/color/DropShadow/non-identity LUT barriers;
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
- opaque/Geometry/target-command captured scalar changes with explicit runtime identity invalidate pixels without recompiling; null runtime identity never hits across requests; direct Shader uniform values are included automatically and custom uniform/resource binders are request-unique unless given a complete runtime key;
- auxiliary/bounds/hit-test request isolation from frame cache, frame render counts, and `LatestFrame`, while bounds/hit-test requests emit independently reconciled metadata snapshots.

### Pool/resource scenes

- stable exact-size frames after warm-up: zero new targets and zero pool misses;
- changing-size frames: permitted exact-size misses but no leak/stale lease;
- equivalent 3-stage and 10-stage linear schedules: equal upper bound for peak live intermediates;
- fan-out/merge lifetime where one producer remains live until its last consumer;
- context/device recreation evicts incompatible pooled/program resources;
- `RenderNodeRenderer.Dispose` releases all pooled targets/program/plan/internal-diagnostic state, rejects later calls, and leaves its borrowed root/cache/factory untouched;
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
- no partial output or cache publication;
- every context/session/input/handle rejects retained use;
- cleanup continues after one cleanup fault;
- the first primary planning/render exception remains the surfaced exception;
- cleanup failures and terminal outcomes reconcile in diagnostics.

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
- synchronous `FilterEffectContext.Bounds` updates after Shader/Geometry and append/resource rollback on invalid or throwing forward mappings;
- disposable `Own`, non-disposable `Borrow`/`UseResource`, null-key request-local Borrow identity, and explicit-key/version coalescing;
- transaction rollback and retained-handle/resource rejection;
- guarded opaque fallback and shared `RenderExecutionInput` capability behavior.

A migration census covers compiled `src/**/*.cs` and `tests/**/*.cs`: all 29 production and 7 test `Process` overrides from the baseline, every executable operation subclass/factory, every raw `ImmediateCanvas` author hook, every `RenderNodeProcessor` pull/rasterize consumer, and every legacy static scale-helper caller. Historical symbol text in `docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch` is deliberately outside this compiled-source census. The gate fails if a returning override, executable `RenderNodeOperation`, `Pull`/`PullToRoot`, list rasterizer, `OperationWrapperRenderNode.SetOperations`, `EffectTarget.NodeOperation`, `EffectTarget(RenderNodeOperation)`, isolated nested processor, independent cache-generation pull, unclassified `CreateLambda`/raw callback, or reference to `RenderNodeContext.MaxBufferDimension`, `RenderNodeContext.SanitizeMaxWorkingScale`, `RenderNodeContext.ResolveWorkingScale`, or `RenderNodeContext.ClampWorkingScaleToBufferBudget` remains in the compiled scope.

Friend Engine tests—not the non-friend public-contract project—assert `HasOpaqueExternalWork`, `OpaqueExternalExecutions`, terminal reconciliation, and every other internal diagnostic property for the raw forms.

## Benchmark contract

Use BenchmarkDotNet with persistent production-equivalent renderer/node/cache/pool lifetime. Setup constructs deterministic source data with a fixed seed and warms the renderer; an iteration renders a complete target-surface request, not an isolated descriptor executor.

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

Compare pinned baseline and feature worktrees in the same machine/session with identical runtime, backend/device, dimensions, warm-up, renderer lifetime, scene, and output verification. Preserve raw BenchmarkDotNet results and request-wide counters.

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
```

Tests selected by `TestCategory=GpuPassFusionGpu` carry the NUnit `GpuPassFusionGpu` category. The two hardware-required commands run on a capable GPU/Vulkan or configured software device; the UnitTests canary prevents a vacuous category run, and the Graphics3D project is invoked separately rather than assumed to be selected through the UnitTests assembly. `ShaderFallbackTests` and ordinary fallback/public-contract tests must pass independently of either GPU gate and must not skip for lack of a preferred GPU.
