# Implementation Plan: Renderer-Wide GPU Pass Fusion

**Branch**: `speckit/004-gpu-pass-fusion` | **Date**: 2026-07-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `docs/specs/004-gpu-pass-fusion/spec.md`

## Summary

Replace the executable `RenderNodeOperation[]` pull pipeline with a renderer-wide, recording-only request pipeline. Every `RenderNode` implements `void Process(RenderNodeContext)` and publishes ordered `RenderFragmentHandle` instances through the context. One fragment DAG preserves value contributions, target commands/captures, finite value Layers, symbolic current-target `TargetLayerScope` effects, other target-scope nesting, and painter order; its embedded value DAG exposes only the semantic values that may be analyzed or fused. Scope-local target-token topology is derived after recording, never stored in an early root-global side list. The recorder discovers the complete 2D request without consulting render caches or touching GPU/media resources, then lowers scope-local token dependencies, resolves forward query/output metadata, propagates requested regions backward, substitutes safe cache entries, partitions execution islands, compiles compatible Shader/opacity runs, schedules pooled resources, and executes the request once.

The public filter-effect lifecycle remains `FilterEffect.ApplyTo(FilterEffectContext, Resource)`. `FilterEffectContext` and `RenderNodeContext` will accept the same renderer-neutral `ShaderDescription` and `GeometryDescription` types. Only a mechanically validated current-pixel Shader form is fusible, and that validation proves coordinate restrictions rather than commutation with antialiased coverage. Arbitrary CurrentPixel work therefore remains after upstream coverage is resolved; only engine-known operations with mechanically proven premultiplied-coverage homogeneity may cross a coverage-producing rasterization boundary. Whole-source Shader, Geometry, legacy custom effects, 3D work, destination readback, and unknown callbacks remain explicit barriers. Legacy `CustomEffect` preserves its raw execution callback as marked opaque-external work, so exact physical-pass claims require zero such boundaries. The abandoned implementation branch is an extraction source for leaf algorithms, tests, and independently reproducible visual evidence, not a branch or subsystem to merge.

## Technical Context

**Language/Version**: C# with `LangVersion=preview`; .NET 10 (`net10.0` and `net10.0-windows`)

**Primary Dependencies**: Beutl.Engine rendering abstractions, SkiaSharp/SkSL, Avalonia geometry primitives, the existing Vulkan/Skia backends, Beutl.Engine.SourceGenerators as already referenced by consuming projects

**Storage**: In-memory recorded graphs, structural/program caches, render-output caches, pooled RGBA16F render targets, and immutable raw linear-RGBA16F starting-SHA references plus fingerprinted manifests under this feature's evidence directory; no database or persisted project-format change

**Testing**: NUnit + Moq for unit/integration/public-contract coverage; Vulkan-gated NUnit execution-shape tests; BenchmarkDotNet for paired renderer benchmarks

**Target Platform**: Cross-platform Beutl desktop engine on macOS, Windows, and Linux; preferred GPU execution where available and the existing supported ordinary-2D fallback otherwise

**Project Type**: Desktop compositing/rendering engine with plugin-facing public APIs

**Performance Goals**: Exactly one GPU pass for the distinct-node, coverage-resolved-source `Shader A -> Opacity -> Shader B` proof; one structural compilation across 100 parameter-only frames; zero warmed intermediate creations for stable bounds; no growth in peak live intermediates between equivalent 3-stage and 10-stage linear schedules; paired warmed median frame-time ratio whose 95% confidence interval is below 1.0 for the cross-boundary workload

**Constraints**: Recording performs no GPU, target allocation, media-read, snapshot, flush, synchronization, or nested execution; preserve painter order, scoped target dependencies, antialiased coverage application order, premultiplied linear RGBA16F semantics, feature-003 density rules and 16,384-pixel buffer clamp; keep output-cache identity separate from structural/program identity; preserve current-main allocation-failure behavior; no GPU fusion across unresolved coverage production, opaque, legacy raw-canvas, 3D, readback, destination-dependent/unproven composite, external-target, or backend boundaries

**Scale/Scope**: One complete target-surface request, including all top-level drawables and nested/auxiliary 2D requests; migrate 29 production and 7 test `Process` overrides plus every direct processor/operation and scale-helper consumer across `Beutl.Engine`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.Editor`, `Beutl.AgentToolkit`, and application call sites; add a non-friend public API contract test project

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

| Principle / gate | Result | Design evidence |
|---|---|---|
| I. License Firewall | PASS | All production work remains in MIT projects. No reference to `Beutl.FFmpegWorker` is added and no IPC boundary changes are planned. |
| II. Dual-Target Framework | PASS | Existing `net10.0` and `net10.0-windows` targets remain unchanged. New Engine code is backend-neutral unless already guarded by the existing backend projects. |
| III. Test-First with NUnit | PASS | Baseline and contract tests precede behavior changes; each planner, cache, fusion, lifetime, and fallback unit has matching NUnit coverage. BenchmarkDotNet is used only for performance evidence. |
| IV. Avalonia + Compiled Bindings | PASS / N/A | No XAML or UI control is introduced. |
| V. Style Belongs to the Linter | PASS | Implementation ends with repository format verification; the plan does not prescribe manual style-only edits. |
| VI. Source Generators Are Load-Bearing | PASS | No generator change is required. The new non-friend project references the existing generator only as an analyzer when its public authoring fixtures require generated members. |
| Quality gates | PASS BY PLAN | The implementation must pass format verification, dual-target solution build, `net10.0` tests with coverage settings, GPU-required tests on capable hardware, and review before merge. |

Post-design re-check: the selected request recorder, planner, public descriptors, test project, and donor extraction policy introduce no constitutional exception. There is therefore no complexity violation to justify.

## Project Structure

### Documentation (this feature)

```text
docs/specs/004-gpu-pass-fusion/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── public-api.md
│   ├── render-request.md
│   ├── breaking-changes.md
│   └── diagnostics-and-evidence.md
├── evidence/
│   ├── target-baseline-generator.patch # applied only to a pinned baseline worktree
│   ├── generate-target-baseline.sh     # reproducible out-of-tree generator driver
│   ├── run-paired-visual-evidence.sh   # exact-fingerprint baseline/feature comparison
│   └── target-baseline/                # immutable RGBA16F files + fingerprinted manifest
└── tasks.md                         # generated by /speckit-tasks, not this phase
```

### Source Code (repository root)

```text
src/Beutl.Engine/Graphics/
├── ImmediateCanvas.cs
├── FilterEffects/
│   ├── FilterEffect.cs
│   ├── FilterEffectContext.cs
│   ├── ShaderDescription.cs
│   ├── SkslSource.cs
│   ├── ShaderBindings.cs
│   ├── GeometryDescription.cs
│   └── GeometrySession.cs
├── Rendering/
│   ├── RenderNode.cs
│   ├── RenderNodeContext.cs
│   ├── RenderFragmentHandle.cs        # replaces executable RenderNodeOperation
│   ├── RenderNodeRasterization.cs     # one owned bitmap/logical-domain result
│   ├── RenderExecutionInput.cs         # shared by Geometry/opaque/target callbacks
│   ├── RenderBoundsContract.cs        # renderer-wide forward/backward bounds primitive
│   ├── RenderScaleUtilities.cs        # feature-003 pure scale helpers
│   ├── RenderNodeRenderer.cs         # high-level replacement for Pull APIs
│   ├── Renderer.cs
│   ├── GraphicsContext2D.cs
│   ├── Operations/
│   │   ├── RenderOperationDescriptor.cs
│   │   ├── OpaqueRenderDescription.cs
│   │   ├── MaterializedInputDescription.cs
│   │   ├── TargetCaptureDescription.cs
│   │   ├── TargetScopeDescription.cs
│   │   ├── TargetLayerScopeDescription.cs
│   │   ├── TargetCommandDescription.cs
│   │   └── RawTargetDescriptions.cs
│   ├── Planning/
│   │   ├── RecordedRenderGraph.cs
│   │   ├── RenderRequest.cs
│   │   ├── RenderRequestOptions.cs
│   │   ├── RenderRequestRecorder.cs
│   │   ├── RegionAnalyzer.cs
│   │   ├── RenderCacheResolver.cs
│   │   ├── ExecutionIslandPlanner.cs
│   │   ├── RenderRequestCompiler.cs
│   │   ├── CompiledRenderRequest.cs
│   │   ├── RenderRequestExecutor.cs
│   │   ├── StructuralPlanCache.cs
│   │   ├── ProgramCache.cs
│   │   ├── RenderTargetPool.cs
│   │   └── RenderPipelineDiagnostics.cs
│   └── Cache/
│       ├── RenderNodeCache.cs
│       └── RenderNodeCacheHelper.cs  # policy/invalidation only; no independent pull

src/Beutl.Engine/Graphics3D/
└── Scene3DRenderNode.cs               # records an opaque backend source

src/Beutl.NodeGraph/                  # migrate wrapper/output nodes and query consumers
src/Beutl.ProjectSystem/              # migrate SceneDrawable and nested scene consumers
src/Beutl.Editor/                     # migrate save-frame scale and renderer consumers
src/Beutl.AgentToolkit/               # migrate metadata-only query consumers
src/Beutl/                            # migrate player/type-converter processor consumers

tests/
├── Beutl.UnitTests/Engine/Graphics/Rendering/
│   ├── Baseline/
│   ├── Recording/
│   ├── Planning/
│   ├── Fusion/
│   ├── Cache/
│   ├── Failure/
│   └── Golden/
├── Beutl.Graphics3DTests/             # 3D opaque/backend boundary coverage
├── Beutl.PublicApiContractTests/      # non-friend authoring/compile contract
└── Beutl.Benchmarks/Rendering/
    └── RenderPipelineBenchmarks.cs
```

**Structure Decision**: Keep plugin-facing Shader and Geometry descriptions in the existing `Beutl.Graphics.Effects` namespace so `FilterEffectContext` authors need no new subsystem. Put the renderer-wide `RenderBoundsContract`, `RenderScaleUtilities`, render-node, and request types in `Beutl.Graphics.Rendering`, so custom-node authors do not depend on Effects merely to describe bounds or density. Place the implementation under renderer-wide `Rendering/Planning` and `Rendering/Operations` folders, not the donor's effect-private graph namespace. Add one lean non-friend NUnit project to compile public authoring examples without `InternalsVisibleTo`; add it to `Beutl.slnx` and do not copy donor tests tied to `Describe(EffectGraphBuilder, ...)`.

## Design Overview

### Complete-request pipeline

```text
update all drawable trees
        |
record all roots without cache substitution
        |
ordered effectful fragment DAG + embedded semantic value DAG
        |
lower scope-local target-token topology and dependencies
        |
finalize and validate forward bounds / density / hit-test metadata
        |
backward requested-region propagation
        |
cache-hit substitution + cache-capture insertion
        |
execution-island partition + Shader/opacity fusion
        |
resource/synchronization schedule
        |
single request execution and atomic cache publication
```

Each published fragment represents an ordered sequence containing zero or more materializable values plus target commands, non-contributing captures, finite value Layers, symbolic `TargetLayerScope`, and other target-state scopes. Commands flow through parent `Inputs`, so `[A, Clear, B]` remains distinct from `Layer { A, Clear, B }` and a child command cannot escape its Layer/opacity/transform scope. The embedded value DAG represents source, map, combine, expansion, Shader, Geometry, materialized, nested, and guarded opaque results. During lowering, every root, finite Layer, and non-empty `TargetLayerScope` receives its own initial target token. `TargetLayerScope` resolves its `TargetRegion` only after every enclosing transform/clip/scope map is known. A non-empty scope replays its mixed stream once on a transparent local target and composites that isolated result once to the current target; `Empty` preserves authored ordering without allocating or executing pixel work. It remains an effect fragment rather than an immediately consumable value. Commands/captures consume and produce the appropriate local token, while pure values retain reusable data dependencies. Per-root provenance links fragments and values to renderer entries for output extent, query bounds, and hit testing.

### Transactional node recording

`RenderRequestRecorder` opens a checkpoint before invoking each `RenderNode.Process`. The supplied context owns borrowed input fragments, newly recorded handles, ordered publications, target effects/scopes, cache-disable state, transferred disposable resources, built-in brush-mask snapshots, and non-resource runtime cache identities. Each fragment immediately memoizes conservative query/value bounds, effective-scale declaration, value-only cardinality, `ContributesValuesToTarget`, `CanBeUsedAsValueInput`, and CPU hit-test metadata from already-recorded inputs, so downstream nodes can inspect handles without execution. Scope-relative target access remains separate internal metadata: a `TargetLayerScope(Full)` handle exposes only finite query metadata and is value-input-ineligible until an author deliberately wraps it in finite `Layer(inputs, Rect)`. Concrete scale resolution uses complete output bounds and applies the feature-003 ceiling/dimension clamp before reverse ROI; later cropping never changes that density. Success validates context ownership and atomically commits the checkpoint. Failure rolls it back, discharges transferred resources best-effort, preserves the primary exception, and invalidates the context and every public handle. Nested node recording uses fresh child-owned facade handles over parent fragment IDs and maps committed child publications back to fresh parent handles. The later graph-wide forward phase finalizes/validates metadata; it does not make the first fragment available.

The executable `RenderNodeOperation` type is removed rather than reused for a different lifecycle. Its replacement, `RenderFragmentHandle`, is a sealed context-owned fragment handle with aggregate metadata and value cardinality; the name remains accurate when one handle represents an ordered runtime fragment stream, allowing dynamic expansion to remain one graph edge until execution. Shader/opacity preserve value cardinality; Geometry and opaque map are ordered zero-or-one maps per value input; combine produces at most one value; expansion owns arbitrary N-to-M topology; target commands may exist with cardinality `None`. Authors inspect `CanBeUsedAsValueInput` before feeding an arbitrary child fragment to a pixel-materializing method and use explicit finite-domain `Layer(inputs, Rect)` when replaying a mixed stream as one value is intended.

### Region and cache ordering

Per-node requested regions are not exposed during `Process`, because they are not sound until the complete graph exists. The planner first derives scope-local target-token dependencies, then resolves forward metadata and stores required regions for values, fragments, and target accesses with explicit `Full`, `Empty`, and finite `Region(Rect)` states. `RootOutputExtent` unions contributing value bounds with every potentially pixel-writing root target effect after scope mapping and clipping; separate `QueryBounds` unions contributing-value and target-command/scope query provenance for Measure/HitTest. A null `RequestedRegion` selects `RootOutputExtent`, while a non-null value is the explicit root output requirement/commit crop. Neither form shrinks the available root, finite Layer, or resolved TargetLayerScope target domain, and reverse ROI may expand a target read up to that domain. Cache lookup happens only after this analysis and target-token dependency discovery. A hit substitutes a selected pure producer with a materialized input while retaining original metadata/provenance for queries. A target-dependent subtree bypasses whole-subtree reuse unless the cache identity proves the complete preceding token's pixel identity and coverage; opaque raw target work always bypasses. A miss inserts a capture point into the same schedule and never starts a second pull. Cache disablement is monotonic through the current result and its ancestors.

### Shader and Geometry seam

Both authoring contexts accept the same `ShaderDescription` and `GeometryDescription` objects. `ShaderDescription.CurrentPixel` accepts only the restricted `half4 apply(half4 color)` form after lexer-based validation; there is no author-asserted invariance or coverage-homogeneity flag. CurrentPixel consumes pixels after upstream analytic/antialiased coverage has been resolved. Coordinate validation is the eligibility source for joining a Shader run, but does not prove `f(kx) = kf(x)` for partial coverage. The planner materializes vector, text, path, or antialiased-clip coverage before arbitrary public CurrentPixel work; a future engine-known participant may cross only when the engine mechanically proves that premultiplied-coverage property. `ShaderDescription.WholeSource` is valid but always starts a non-fused pass. Structural source/binding names are separated from execution-time uniform/resource values, and full source equality protects program-cache hash collisions.

`GeometryDescription` is a deferred one-input/zero-or-one-output barrier with mandatory forward/backward bounds, CPU hit-test contract, separate structural/runtime cache identities, declared resources, and an explicit readback declaration. Its callback receives complete output bounds, resolved required/device region, and a one-shot callback-scoped canvas facade over executor-owned input/output resources. The canvas maps composition-global logical coordinates through canonical rounded device bounds and closes without an implicit flush. Retained sessions, inputs, canvases, facades, and resource handles reject use after the callback. Runtime output discard or shrink is permitted only within the allocated forward bounds.

## Implementation Strategy

### Phase A - Freeze the current-main behavior

1. Pin starting code SHA `43a38e665d9bf52548161a3917e748bd1457ff55` in provenance.
2. Add raw linear-RGBA16F immutable golden support, alpha MAE, edge-band local MAE, and maximum-channel error without changing rendering behavior.
3. Store the target-baseline generator as `evidence/target-baseline-generator.patch` plus `evidence/generate-target-baseline.sh`; the script creates a temporary worktree pinned to the starting SHA, applies the patch there, and copies only immutable RGBA16F files and a manifest back. No historical generator source is compiled by the feature branch. Add `evidence/run-paired-visual-evidence.sh` to run both worktrees and reject missing or mismatched fingerprint fields before comparison. The manifest records artifact/generator/paired-runner hashes and exact OS, architecture, backend, device, driver, graphics-library, and runtime fingerprints.
4. Capture new-branch visual, allocation-failure, scale, cache, nested/query, AA-coverage, and no-preferred-GPU behavior. Paired baseline/feature evidence is valid only under an exact matching fingerprint and fails explicitly on mismatch. Normal CI instead compares fusion-disabled and fusion-enabled output in the same process/device through an internal request `FusionMode`; production and public renderer options expose only the enabled behavior, while friend evidence tests may select disabled compatibility partitioning. The mode is part of structural-plan identity so the two schedules cannot reuse one another accidentally. Normal-CI AA edge checks use a fixed device-independent maximum channel error of `0.02`; fingerprint-specific paired bounds come only from the exact matching manifest. CI always verifies evidence integrity, never silently selects a foreign-device blob, and does not treat this same-process check as a replacement for the paired starting-SHA proof. Import the eight independently reproducible `004-parity-strong` donor references only as supplemental effect regressions.
5. Add request-wide observational counters to the existing renderer and capture baseline schedules and allocations without changing decisions. Prove instrumentation-on/off neutrality for output, allocation/failure behavior, and cache decisions before relying on those counters.
6. Add a persistent-production-lifetime BenchmarkDotNet harness and record paired baseline data; do not adopt donor timing percentages.

### Phase B - Introduce the recording contract with compatibility execution

1. Add request options (including target-less `TargetDomain` distinct from `RequestedRegion`), render purpose/intent, renderer-wide bounds contract including custom-forward/full-input fallback, ordered fragment/value IR, scope-local target-token lowering, provenance, owned/borrowed resource handles, scalar runtime identities, and node transaction support. Characterize option sanitization, lifecycle transitions, graph IDs/order/provenance/cache candidates, LIFO cleanup and cleanup-fault aggregation, exact ownership discharge/cache transfer, and diagnostics neutrality before production implementation. Move feature-003 pure density helpers from the recorder to `RenderScaleUtilities` and migrate every production and test caller without a forwarding shim; update the old `EffectiveScale` operation-oriented documentation at the same time.
2. Change `RenderNode.Process` to `void`, remove executable `RenderNodeOperation`, add the sealed `RenderFragmentHandle`, and implement the concrete `RenderNodeContext` API in [contracts/public-api.md](contracts/public-api.md). Fix `CanBeUsedAsValueInput` propagation per recorder: eligible Shader/Geometry/opaque values stay true, pure-child Opacity stays true, destination-dependent Blend plus `TargetScope`, `TargetLayerScope`, raw target forms, and commands stay false, and finite Layer is the explicit mixed-stream-to-value boundary.
3. Implement typed `TargetCommand`, non-contributing `TargetCapture`/`ContributeValues`, public symbolic `TargetLayerScope(inputs, TargetRegion)`, finite public `Layer(inputs, Rect domain)`, guarded `TargetScope`, and the explicitly opaque-external `RawTargetScope`/`RawTargetCommand`. `LayerRenderNode.Process` records a default legacy `PushLayer()` through `TargetLayerScope(..., Full)` in the normal bottom-up transaction; no recorder traversal special-case bypasses a public override. The scope retains symbolic Full through later parent transform/clip wrappers and resolves it only during target-token lowering. It remains value-input-ineligible; a non-empty scope preserves the isolation target unless equivalence proves elision, while `Empty` preserves ordering without pixel work. It becomes an ordinary value only through an explicit finite Layer. Add ordering characterizations for root `[A, Clear, B]`, finite public `Layer { A, Clear, B }`, `Transform(+10) -> PushLayer(default) -> Full Clear`, nested target-Layer scopes, empty target-Layer scopes, and `Snapshot -> optional Clear -> blend/transform/filter DrawBackdrop`; require each capture to materialize once and contribute only at its explicit later draw.
4. Migrate all 29 production and 7 test overrides in one breaking change. Classify every old callback through the migration census: typed value/effect, guarded `Opaque*`, typed target command/capture/scope, raw scope/command, or 3D/backend boundary. Separately migrate every existing render-node authoring, scale, hit-test, rasterization, cross-project, and golden-harness test that directly names the removed operation/pull surface; the 18 golden consumers remain unchanged behind the migrated harness. Initially keep unsupported callbacks opaque so output remains baseline-equivalent before fusion, but leave no unclassified `CreateLambda` or raw-canvas escape.
5. Replace `RenderNodeProcessor.Pull`/`PullToRoot` and both old rasterize shapes with the disposable high-level `RenderNodeRenderer.Render`, single-result `Rasterize`, `Measure`, and `HitTest` operations. `Rasterize` returns one caller-owned result that carries its logical bounds/origin, output scale, normal empty state, and optional bitmap rather than a list or a bare shifted bitmap. Migrate all Engine, NodeGraph, ProjectSystem, Editor, AgentToolkit, and application consumers. Raster/save callers use `Measure().OutputBounds` or the rasterization result bounds, while layout/query/hit-test callers use `QueryBounds`; the old operation-bounds union never represented every target write soundly, so new output and query bounds intentionally differ where required. Remove operation-backed `EffectTarget` and `OperationWrapperRenderNode.SetOperations`; the renderer owns persistent plan/program/pool state and factory-created pooled targets, and no executable/list-rasterization compatibility operation remains.
6. Make Particle, Scene3D, media sources, custom filter effects, nested drawables, brushes, and NodeGraph record deferred work instead of executing during `Process`. Lower DrawableBrush masks through inherited nested recording. Add the `ImmediateCanvas` deferred-callback capability guard so author disposal, snapshot, nested execution, undeclared resources, synchronization, `SaveLayer`-backed state APIs, and hidden target allocation are rejected.

### Phase C - Make the renderer request-wide

1. Update all drawable trees before recording any root.
2. Record root clear and every top-level contribution as ordered fragments; derive target-token dependencies only inside the root, finite Layer, or symbolic TargetLayerScope that owns them.
3. Resolve per-root metadata and convert bounds/hit-test queries to metadata-only requests that never invoke the executor.
4. Record same-target child nodes into the current graph; represent separate-target child rendering as nested requests inheriting allocator, diagnostics, purpose, intent, scale, region, and cache policy.
5. Convert 3D to a deferred opaque backend source whose execution produces one materialized 2D input and explicit transition/synchronization events.

### Phase D - Analyze regions and caches after discovery

1. Acquire and validate each finite owning target domain from the real destination, a finite Layer, or explicit target-less `TargetDomain`, then lower scope-local target-token topology and discover complete preceding-token dependencies. Resolve symbolic TargetLayerScope regions only after every enclosing scope map is known; fail during lowering when a reachable Full access has no finite owner domain.
2. Resolve forward bounds, `RootOutputExtent`, `QueryBounds`, aggregate stream cardinality, effective supply density, and hit-test metadata. Self-bounded graphs without Full need no separate root domain. Query bounds and `RequestedRegion` never substitute for a target domain.
3. Seed the final requirement from non-null `RequestedRegion` or otherwise `RootOutputExtent`, then propagate it backward through sound bounds contracts, use explicit `Full` for opaque/unknown mappings, preserve `Empty`, and reject invalid rectangles as planning failures.
4. Discover render-cache candidates without short-circuiting traversal. Reject raw-target candidates and conservatively bypass target-dependent whole subtrees unless complete preceding-token identity/coverage is proven; select valid pure-value hits after dependencies are known, preserve fragment/token order, and insert miss capture points into the same schedule.
5. Preserve child cache granularity, static-prefix reuse, feature-003 density eligibility, query isolation, and atomic publication after complete request success.

### Phase E - Add canonical Shader and Geometry authoring

1. Extract and harden the donor lexer, source identity, snippet merger, uniform binding, bounds contract, and Geometry session algorithms.
2. Add descriptor-only `Shader` and `Geometry` methods to both contexts while preserving every existing `FilterEffectContext` operation and `ApplyTo` lifecycle. In `FilterEffectContext`, apply each description's pure forward bounds contract synchronously in item order so `Bounds` is updated before the call returns; invalid/thrown mapping rolls back that append.
3. Lower existing Skia filters, color filters, transforms/scopes, built-in brush masks, and custom effects to known semantics only where equivalence is proven; otherwise preserve authored order through guarded opaque or marked raw compatibility fragments.
4. Add non-friend public authoring tests for existing ApplyTo source compatibility and every new render-node shape, the full value-input-eligibility table, non-disposable borrowed resources, disposable ownership transfer, null-key request-local Borrow identity, and the independent scale utilities.
5. When the API lands, update both mirrored `beutl-filter-effect` skills and `docs/ai-workflow/resolution-independent-rendering.md` so author guidance teaches Shader/Geometry recording and deferred custom-scale declarations instead of eager `Process` allocation.

### Phase F - Plan, fuse, and execute

1. Lower a target-token chain independently for every root, finite Layer, and non-empty TargetLayerScope; preserve an Empty TargetLayerScope as order-only metadata with no local chain or pixel work. Then partition the complete graph at cache, unresolved analytic/antialiased coverage production, opaque, target-read/write, raw-canvas, readback, destination-dependent/unproven composite, external-target, backend, dynamic-topology, and 3D boundaries.
2. Compose maximal validated current-pixel Shader runs and invariant opacity across distinct render nodes after upstream coverage is resolved. Split deterministically at coverage and backend stage/uniform/sampler/child/program limits.
3. Compile a structural plan independent of parameter values, bind execution-time bounds/regions/resources, include the internal fusion mode in plan identity, include built-in parameters, Shader uniforms, declared resources, and callback runtime identities only in output-cache keys, and cache programs by backend capability plus full-source equality.
4. Schedule pooled RGBA16F intermediates by lifetime, execute one request owner, release all resources on every path, and publish caches only after complete success.
5. Preserve current-main fallback and allocation-failure outcomes; invalid Shader source/bindings or program creation fail explicitly and never become identity.

### Phase G - Prove the redesign

1. Run public contract, raw-callback migration census, fragment/scope order, target capture/backdrop, transaction, ROI, scale, cache, animation, fallback, nested, 3D-boundary, failure, program-cache, pool, and diagnostics reconciliation suites.
2. Require exactly one pass for a coverage-resolved source followed by `Shader A -> Opacity -> Shader B`; require an exact materialization/barrier before a non-coverage-homogeneous Shader applied to an antialiased thin line/path and enforce edge-local parity; also require exact remaining barrier splits, one compile over 100 parameter frames, zero warmed allocation, bounded peak ownership, and non-vacuous visual references.
3. Run all applicable feature-003 goldens, the full `net10.0` test suite, both target builds, format verification, and dedicated `GpuPassFusionGpu` NUnit-category suites in both `Beutl.UnitTests` and `Beutl.Graphics3DTests` with GPU absence promoted to failure. Run the non-GPU Shader fallback suite separately so a hardware filter cannot hide it.
4. Run paired persistent-lifetime benchmarks against the pinned baseline and record confidence intervals, controls, environment, code SHAs, counters, and raw results.

## Dependency and Review Boundaries

- Phases A and the opaque-only portion of B establish characterization evidence before behavior changes.
- The complete `Process`/operation migration is one public breaking change; no returning overload, `[Obsolete]` member, or parallel builder is permitted between phases.
- Request-wide recording (C) precedes region/cache decisions (D); cache short-circuiting or top-down ROI during recursive traversal is prohibited.
- Canonical descriptions (E) precede fusion (F); an author declaration alone never makes work fusible.
- Structural plan caching is introduced only after request identity and cache-island behavior are correct without it.
- Public API changes require `beutl-design-reviewer`; the complete diff requires `beutl-reviewer`; source-generator review is required only if implementation discovers an actual generator change.

## Risk Controls

| Risk | Control |
|---|---|
| Painter-order or target-read corruption | Ordered effectful fragments; scope-local target tokens; explicit read/write dependencies; root/finite-Layer/TargetLayerScope clear-order and multi-root backdrop/snapshot tests. |
| ROI under-render | Graph-complete backward analysis; mandatory bounds contracts; full-input fallback; shifted/full/empty ROI goldens. |
| False Shader fusion | Restricted current-pixel grammar validated by lexer; no invariance assertion; exact barrier and collision suites. |
| Shader moves across antialiased coverage | CurrentPixel is post-coverage; arbitrary public stages stop at geometry/text/path/AA-clip rasterization; only engine-mechanically-proven coverage-homogeneous operations may cross; thin-AA edge-local golden and exact-boundary tests. |
| Cache hides dependencies or loses density | Record before lookup; substitute only after metadata/ROI; retain provenance; include coverage/density/device in output-cache identity. |
| Animated values trigger recompilation | Separate structural source/names/topology from runtime binding values and resource contents; 100-frame counter gate. |
| Recording leaks GPU or media side effects | Transaction probes around every public node shape and known eager source; execution sessions are callback-scoped. |
| Guarded callback hides `SaveLayer`/nested work | Capability canvas rejects layer/opacity/blend/mask/paint APIs, target allocation, nested renderers, and flush; retained raw hooks are classified `LegacyRawCanvas` and excluded from exact claims. |
| Dynamic N-to-M output loses ordering | Stream-valued handles with explicit cardinality/topology and aggregate metadata; never infer identity from empty bounds. |
| Resource leaks or masked failures | One request owner, generation-checked leases, rollback checkpoints, best-effort cleanup sweep, primary-exception preservation, injection at every acquisition/compile/publish phase. |
| Donor architecture contaminates the redesign | Leaf-file allowlist and explicit denylist in research; no cherry-pick, no `PlanExecutor` copy, no effect-local caches or replacement lifecycle. |
| Device-specific evidence becomes a false CI oracle | Out-of-tree pinned-SHA generator; hashed RGBA16F manifest with exact environment fingerprint; hard-error paired mismatches; same-process fusion-off/on CI comparison; no foreign-blob selection. |

## Complexity Tracking

No constitution violations or intentionally retained parallel architectures exist. The temporary opaque interpreter is an implementation stage of the single new request pipeline, not a compatibility API, and is removed or retained only as the explicit long-term opaque execution boundary required by the specification.
