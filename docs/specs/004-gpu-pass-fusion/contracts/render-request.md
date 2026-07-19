# Internal Render Request Contract

This contract defines the single renderer-wide implementation seam behind the public context API. Internal names may be adjusted for local conventions, but stage ordering and ownership boundaries are normative.

## Request entry points

`RenderNodeProcessor.Pull` and `PullToRoot` are removed. Their executable-array lifecycle is replaced by a high-level `RenderNodeRenderer` facade backed by `RenderRequestRecorder`, compiler, and executor.

Required high-level operations are:

```csharp
public sealed class RenderNodeRenderer : IDisposable
{
    public RenderNodeRenderer(
        RenderNode root,
        RenderNodeRendererOptions? options = null);

    public RenderNode Root { get; }
    public RenderNodeRendererOptions Options { get; }
    public bool IsDisposed { get; }

    public void Render(ImmediateCanvas destination);
    public RenderNodeRasterization Rasterize();
    public RenderNodeMeasurement Measure();
    public bool HitTest(Point point);
    public void Dispose();
}
```

`RenderNodeRendererOptions` fixes intent, optional target-less `TargetDomain`, optional requested region (`null` = complete `RootOutputExtent`), output/maximum working scales, cache policy, and an `IRenderTargetFactory`. A null factory selects the engine's standard current-backend linear-premultiplied RGBA16F allocator. The factory replaces the old protected `CreateRenderTarget` seam and is called only on a pool miss; each non-null return transfers one fresh exclusive compatible target, and invalid returns are disposed before allocation-failure policy is applied. `RenderNodeRenderer` owns persistent structural/program caches, the pool, and every factory-created target while pooled or request-leased; disposal evicts/releases them and does not dispose the borrowed root/cache, factory, destinations, or a returned caller-owned `RenderNodeRasterization`. Successful output-cache publication transfers the payload to the existing `RenderNodeCache` lifecycle. Public Render/rasterize create complete painter-ordered `Auxiliary` requests. Measure/hit-test stop after metadata/provenance analysis as `Bounds`/`HitTest` and never execute GPU/media callbacks. Only production `Renderer` creates `Frame`, and CacheWarmup is internal. Request diagnostics are an internal implementation/evidence seam, not a public option. There is no list-returning rasterizer because an effectful fragment stream has one target-ordered result; that result carries its logical bounds/origin and represents empty output without fabricating a zero-area bitmap. Internal overloads may accept an existing request owner for nested work; no overload returns public fragment handles outside a recording transaction. Full signatures are normative in [public-api.md](public-api.md#high-level-node-renderer).

## Renderer frame sequencing

For one target surface, `Renderer.Render` performs these steps in order:

1. Acquire/update every participating renderer `Entry` for the frame.
2. Run all `GraphicsContext2D` construction so every render-node tree is complete.
3. Finalize invalidation and cache-candidate state without reading cached pixels.
4. Create one `RenderRequest` and record root clear plus every entry contribution in painter order.
5. Lower scope-local target-token topology and discover complete target dependencies.
6. Resolve output/query metadata and required regions for the complete request.
7. Resolve render-cache hits/misses and capture points.
8. Plan islands, fusion, resources, and synchronization.
9. Execute once against the externally owned root target.
10. On complete success, commit entry bounds/render counts and publish selected cache captures.

No top-level entry may execute planner-controlled 2D work before step 4 has recorded the final entry.

## Recording

### Traversal

`RenderRequestRecorder.RecordSubtree` recursively records container children in their established order, flattens their explicitly published fragment streams into the parent `Inputs`, and invokes the parent transaction. Target commands/captures remain inside those streams and are not hoisted to a request-global side list. Recording never consults `RenderNodeCache.IsCached`.

There is no concrete-node traversal exception. `LayerRenderNode.Process` runs in the same normal bottom-up transaction as an out-of-tree override. A finite non-default limit records public `Layer(inputs, Rect domain)` and emits one eligible value. A default legacy `PushLayer()` records public `TargetLayerScope(inputs, TargetRegion.Full)`: its transparent isolation target and scope-relative Full access remain symbolic inside an effect fragment, later parent transform/clip/scope nodes wrap it normally, and lowering resolves the actual mapped domain only after the complete hierarchy exists. The handle exposes finite query metadata but is not a value input; a public author that needs one value uses an explicit finite Layer.

The request family owns a reference-identity active-node stack spanning same-target traversal and separate-target nested recording. Before opening a nested invocation it rejects any node already on that stack with a deterministic recording-cycle failure and path; removal occurs in `finally`. The guard prevents self/ancestor recursion without forbidding repeated completed occurrences.

`RecordNode` accepts explicit parent-owned handles for wrapper/NodeGraph cases. It validates that every handle belongs to the same request and active parent transaction, then creates fresh child-owned input facades mapped to the same internal fragment IDs. The child never observes the parent handle objects. On child commit it invalidates every child facade/handle and returns fresh parent-owned handles mapped to the child's published internal fragment IDs; the original parent handles remain active. Normal `RecordSubtree` traversal uses the same child-facade/remap protocol between every invocation.

### Checkpoint protocol

Before invoking a node:

1. allocate a request-local checkpoint;
2. map borrowed internal input IDs to fresh child-owned facade handles and inherit options/cache state;
3. create one public context bound to the checkpoint;
4. invoke `Process`;
5. validate all published handles, value cardinalities, fragment/value-input eligibility, descriptor structures, nested transactions, and ownership transfers;
6. commit atomically or roll back completely.

Rollback disposes transferred resources best-effort in reverse acquisition order, records cleanup failures separately, preserves the primary exception, invalidates every public facade, and leaves the parent graph unchanged.

The same checkpoint model wraps one `FilterEffect.ApplyTo` call. Its item list is transferred only after successful return and validation.

### Recording side-effect guard

Tests install counters/fakes at the engine's GPU context, render-target factory, surface snapshot, media frame read/decode, nested renderer, flush/synchronization, and readback seams. Every public node shape and every migrated eager path must leave all counters at zero during recording. Debug builds may maintain a request-local recording guard checked by engine allocation/execution entry points.

## Recorded IR

### Ordered fragment graph and embedded value graph

Every `RenderFragment` declares its ordered child fragments, embedded value producers, target effects, scope kind, publication/provenance, value cardinality, contribution flag, and whether it can be consumed as a materialized value input. Sequence and Layer fragments retain exact child order. `TargetCommand` and `RawTargetCommand` are real fragments even with value cardinality `None`; `TargetCapture` is a non-contributing value plus a target-token read edge. Opacity, Blend, OpacityMask, guarded TargetScope, and raw scope wrap their child fragment instead of moving its target effects.

Each `RenderValue` declares:

- ordered input IDs and source/map/combine/expand topology;
- fixed/ranged/dynamic output cardinality;
- forward/backward bounds behavior;
- effective-supply behavior;
- CPU-only hit-test behavior;
- pixel semantic kind, if known;
- readback/backend/target/external ownership flags;
- structural identity and runtime binding payload;
- separate output-cache runtime identity for built-in values, Shader uniforms, callback scalar state, and resources;
- node/root/cache provenance;
- request-owned resources.

The recorder immediately evaluates each descriptor's pure forward bounds/cardinality/scale/hit-test declaration against already-recorded input metadata and memoizes a conservative fragment aggregate for downstream public handles. `RenderValueCardinality` counts values only; fragment existence is separate. The initial compatibility migration classifies existing callbacks as guarded opaque values, typed target effects, or `LegacyRawCanvas` scope/command fragments. Every opaque form describes graph shape and metadata but forms an execution boundary.

### Scope-local target-token lowering

After the complete fragment graph is committed, lowering creates one initial target token for each externally owned root, each finite `Layer`, and each non-empty `TargetLayerScope`. Replaying a sequence consumes children in order. Clear, target command, capture, backdrop/snapshot, target-dependent operation, readback, and presentation consume the preceding token and produce the next inside that same scope. A capture also produces a reusable value edge but remains non-contributing until `ContributeValues`. A `TargetScope` decorates the child's replay without changing ownership of its token. A finite Layer consumes its own local child-token chain and yields one outer value. `TargetLayerScope` resolves its symbolic `TargetRegion` through every enclosing scope map. A non-empty result allocates a transparent offscreen isolation target, replays the mixed child sequence once, then composites the isolated result once onto the current target; the planner may elide that target only after proving exact equivalence. `Empty` remains an ordered effect fragment but creates no local target or pixel work. The scope has no public value input. Value inputs are explicit edges. These scoped chains are the canonical painter-order dependency and are discovered before cache selection; query bounds are metadata only and never authorize reordering or deletion.

Lowering preserves these invariants:

- Root sequence `[A, Clear, B]` draws `A`, clears that root, then draws `B`; `Layer([A, Clear, B], region)` performs the same sequence on a fresh local target and exposes only the resulting layer value outside. Layer content bounds union contributing child values with potentially pixel-writing target-effect regions after scope transforms and clip to the Layer domain, so a full clear yields a full-domain value even though its command query bounds are empty. Read/order-only accesses do not add content; hit testing remains query-metadata-driven.
- `Transform(+10) -> TargetLayerScope([Full Clear], Full)` maps Full through the completed inverse/forward scope chain before choosing the isolation domain; it never freezes the root rectangle while the child is recorded. Existing `PushLayer(default)` records this shape through ordinary `Process`.
- `TargetCapture` snapshots the immutable pre-capture token once, emits a reusable value edge, and never redraws it merely because its anchor is published. `ContributeValues` controls the later automatic composite without changing that token dependency.
- `TargetScope` may only surround one replay with mechanically allocation-free transform/clip state. Opacity, Blend, and brush-backed OpacityMask use engine-typed scope kinds. Raw decorators use `RawTargetScope` and invalidate exact whole-request pass claims.
- `TargetCommand` is guarded and region/access declared. `Readback` snapshots the immutable pre-command target before invoking the callback; writes during that callback are absent from `UseSnapshot`. `RawTargetCommand` is a zero-input conservative Full/ReadWrite opaque-external effect.
- Built-in SnapshotBackdrop records the capture above plus a request-local identity binding. A later DrawBackdrop consumes that value after any intervening Clear and inside its authored blend/transform/filter scopes; no separate untracked backdrop snapshot exists.

### Provenance

Each renderer entry has a provenance group with its published fragment IDs, embedded value IDs, node/cache candidate tree, and top-level painter index. Cache substitution changes only eligible execution producers. Bounds and hit testing continue to use original metadata and provenance.

## Metadata analysis

### Forward resolution

Process fragments and embedded values topologically, validate/finalize their memoized declarations, and compute:

- logical output bounds, preserving empty/non-empty distinctions and rejecting invalid rectangles;
- aggregate and per-runtime-output effective supply rules;
- declared cardinality and possible empty output;
- value contribution, value-input eligibility, target-effect classification, and scope-local query bounds;
- hit-test mapping;
- materialization requirements and backend format.

A source description provides its output metadata directly. Map-each applies its forward contract to every runtime element and exposes the conservative aggregate. Combine uses its declared multi-input contract. Expansion retains conservative aggregate metadata until runtime outputs are known. A thrown/invalid mapping is a planning failure, never identity.

### Backward region analysis

Scope-token lowering has already resolved every reachable `TargetRegion.Full` against a finite owning target domain before forward or reverse analysis begins. A production Frame or `Render(destination)` supplied the root viewport; a target-less request with root Full access—including a root-owned `TargetLayerScope(Full)`—supplied explicit non-empty `TargetDomain` or failed during lowering. Query bounds and `RequestedRegion` never substitute. Self-bounded requests with no Full access need no separate root domain, and public `Layer(inputs, domain)` supplied its own finite non-empty Rect. A default `LayerRenderNode` remained symbolic only through recording and was resolved in the preceding lowering phase, never here and never as a recording-time value.

Compute `RootOutputExtent` as the union of contributing root value bounds and every potentially pixel-writing root target-effect affected region after scope mapping and clipping. Full writes contribute the complete root domain; finite writes contribute their mapped region. Engine-proven read-only captures and order-only effects do not enlarge it. Compute separate `QueryBounds` from contributing value query metadata and target-command/scope query provenance for Measure/HitTest. Treat the optional root `RequestedRegion` as the final output requirement/commit crop (`null` uses complete `RootOutputExtent`), not as the finite target domain. Intersect it with the active destination clip for final writes, add target reads, and walk fragments/values in reverse topological order:

- identity/current-pixel/opacity propagates the intersected requirement unchanged;
- a custom contract invokes its backward map;
- `RequiresFullInput` requests the complete resolved input bounds;
- multiple consumers and fan-out union requirements;
- combine propagates through its per-input contract;
- opaque work without a proven map requests complete inputs;
- dynamic expansion uses its declared conservative contract;
- 3D/backend source receives no internal ROI and produces complete declared bounds;
- target read/write commands preserve their exact scoped-token dependencies even with empty regions;
- a lowered Full access carries its complete finite root, finite Layer, or TargetLayerScope domain, so blur/filter aprons for a target capture may expand beyond `RequestedRegion` up to that domain;
- a non-contributing capture receives dependency ROI but is omitted from root query bounds until a contributing consumer requires it.

The result stores separate requirements for value IDs, fragment IDs, target-token accesses, and the final commit crop, each with explicit `Full`, `Empty`, and finite `Region` states. An invalid rectangle returned by a mapper is a planning failure. These maps do not mutate author handles or structural identity.

## Render-cache resolution

### Discovery and selection

Every eligible node occurrence records a `CacheCandidate` around its result. After metadata/region analysis, resolver selection observes the existing hierarchy and per-child granularity:

- disabled candidates and affected ancestors bypass;
- candidates with raw target work bypass as whole-subtree boundaries;
- candidates with target-token dependencies bypass unless the cache key proves complete preceding-token pixel identity, coverage, target domain, density, format, and device/context;
- a valid parent hit may supersede descendant execution;
- when a parent is invalid/ineligible, valid child hits remain selectable;
- a hit must cover the required region and supply sufficient density without violating feature 003;
- persistent frame caches are not mutated by `HitTest`, `Bounds`, or unrelated auxiliary requests.

The parent-supersedes rule applies only to pure value subtrees. Clear, capture, backdrop, readback, target-dependent blend, raw target work, and every other target-token effect remain in their fragment scope. Eligible child value hits may replace command inputs, but substitution preserves the command's token edge, input edge, required region, and authored position. The conservative first implementation does not persistently reuse a target capture or subtree that depends on an externally owned root token.

### Hit substitution

A hit creates an internal materialized value with cached logical bounds, coverage, density, format, and device identity. Downstream execution consumes it, while provenance retains the original metadata graph. The hit creates an execution-island boundary and produces no executed pass inside the substituted subtree.

### Miss capture

A selected miss inserts a capture point after the value's scheduled producer. It does not start another recorder/renderer. Captured resources remain request-owned and unpublished until the entire request succeeds. Failure, cancellation, invalid runtime output, or cleanup fault prevents publication. Publication records the exact output identity, coverage, density, format, and device/context.

### Cache identities

Structural plan identity includes:

- operation kinds/order/topology;
- structural Shader/Geometry/opaque keys and binding signatures;
- bounds behavior identity;
- fusion/barrier-relevant state;
- backend capability and target format class;
- cache-island shape and capture locations.

It excludes runtime parameter values, resource contents/versions, resolved regions, and resolved sizes/densities that do not change schedule shape.

Render-output identity additionally includes every pixel-affecting built-in parameter, every canonical Shader uniform value, every explicit description/binding `RenderRuntimeIdentity`, every description-declared `RenderResourceIdentity` key/version, source/subtree revision, output bounds, coverage, effective density, format, request policy where behavior differs, and device/context identity. Null runtime identities and resources owned without stable cache keys get fresh request-local identities and therefore cannot cause unsafe cross-request pixel reuse. Undeclared token use is rejected. Structural keys never substitute for runtime identities. Hashes select buckets; complete key equality decides reuse.

## Island planning

The planner partitions the post-cache graph at:

- opaque and Geometry values;
- every `LegacyCustomEffect` or `LegacyRawCanvas` opaque-external callback whose internal passes/synchronization are unmeasured;
- materialized cache inputs and captures;
- target commands/captures/readback and Layer/target-scope boundaries where token equivalence is not proven;
- destination-dependent `Blend` or another target-dependent composite;
- external targets;
- backend transitions and 3D;
- runtime-dynamic topology that cannot be scheduled statically;
- explicit CPU readback;
- an analytic/AA coverage-producing source before an arbitrary public CurrentPixel stage, unless every crossed engine-owned stage has a mechanical premultiplied-coverage-homogeneity proof;
- backend Shader capability/resource limits.

An island is maximal only when combining adjacent work preserves authored fragment/value order, scope-local target-token order, contribution semantics, bounds/ROI, scale, color/alpha semantics, hit-test metadata, output cardinality, cache identity, and synchronization behavior.

Production requests use internal `FusionMode.Enabled`. Friend evidence tests may issue otherwise identical requests with `FusionMode.Disabled`, which retains the same semantic lowering and compatibility execution but prevents eligible stage composition. The mode is inherited by nested requests and included in structural-plan identity; it is not a public `RenderNodeRendererOptions` switch and therefore is not a plugin-visible behavior knob.

## Shader fusion

### Eligibility

Eligible stages are:

- validated `ShaderDescription.CurrentPixel` stages;
- the built-in invariant opacity descriptor;
- later built-in operations only after a dedicated equivalence rule and tests are added within this feature.

WholeSource Shader, Geometry, opaque work, coordinate changes, unknown sampling, destination-dependent `Blend`/composite, dynamic topology, readback, cache capture/input, external target, and backend transitions are barriers. Blend remains destination-dependent and value-input-ineligible even when its child is pure; no Shader eligibility rule weakens that existing barrier.

Coordinate independence proves only that adjacent CurrentPixel stages can share pixel coordinates. It does not prove equivalence between applying a nonlinear transform before analytic/AA coverage and applying it to the coverage-resolved premultiplied pixel. An arbitrary public CurrentPixel stage therefore starts after a materialization boundary when its producer emits analytic coverage. Vector, text, geometry, and equivalent coverage-producing sources must first resolve coverage into the intermediate value. Only an engine-owned stage with a mechanical premultiplied-coverage-homogeneity proof may be lowered across that source boundary, and the public API exposes no assertion flag. Once coverage is resolved, adjacent eligible CurrentPixel/opacity stages may form the normal maximal run.

### Composition

The compiler uses lexer/token-aware renaming and emits one merged program that applies stages in authored order. A compiled run records whether its input coverage was already resolved or which engine-owned homogeneity proof authorized crossing a coverage-producing source; a source boundary without either fact splits the run before compilation. The compiler must isolate:

- uniform and resource names;
- functions, top-level constants, arrays, and declarations;
- identifiers versus member/swizzle tokens;
- reserved implicit source and entry-point names;
- child/sampler binding layout.

The compiler validates stage count, uniform-vector, sampler, child, source-size, and backend-specific program limits. It splits before the first overflowing stage, then continues deterministically. A one-stage run remains a valid unfused Shader pass.

### Runtime binding

Final logical bounds, required region, effective supply, working density, clamped device size, frame parameter values, and resource providers bind after plan selection. Parameter-only changes cannot alter merged source or binding layout. A resource/provider failure is a render failure and publishes no partial output/cache.

### Program cache

Cache key includes merged full source/signature, backend/device capability class, color/alpha/format contract, and relevant compile options. Stable hashes are bucket indexes only. Full equality prevents collision aliasing. Builders/program leases reset all runtime values before reuse and support re-entrant use of the same structural program without corrupting outer bindings.

## Resource and scale plan

### Working density

For every materializing value during forward recording:

1. compute its complete conservative logical output bounds;
2. start with `OutputScale`;
3. take the maximum concrete input supply, excluding `EffectiveScale.Unbounded` vector inputs;
4. apply a custom resolver instead when declared, using only input supplies, complete output bounds, output scale, and maximum scale;
5. require the custom result to be finite and strictly positive, failing the current recording transaction otherwise, then cap with `MaxWorkingScale`;
6. apply `ClampWorkingScaleToBufferBudget` against the complete output bounds so each device axis is at most 16,384 pixels;
7. publish the actual clamped density immediately as downstream `EffectiveScale`.

Public `TargetCapture` follows the same rule from `OutputScale` and its declared bounds because no value input supplies density. It is a deliberate sampling/materialization boundary, not a lossless or scope-density-preserving snapshot: a capture inside a denser finite Layer or TargetLayerScope may downsample into its published concrete density. Its `Custom` resolver sees an empty `InputSupplies` list and may use only `OutputBounds`, `OutputScale`, and `MaxWorkingScale`; it never receives the enclosing target's resolved density. The engine-internal backdrop capture may late-bind the already resolved density of its owning root, finite Layer, or TargetLayerScope target, but never exposes an unresolved public handle. CurrentPixel Shader and other vector-preserving stages remain `Unbounded` until the run's eventual materialization, subject to the analytic/AA coverage boundary above. Backward ROI later chooses `Full`, `Empty`, or a finite cropped logical allocation. It never recomputes or raises a published density; the crop uses the already-published scale. The root target remains at the destination density. `PixelRect.FromRect(croppedBounds, density)` remains the canonical logical-to-device rounding. Device/density-dependent Shader uniforms bind after ROI using that stable density and final device bounds.

### Pool and liveness

`ResourcePlan` computes first/last use for every materialized value and reuses an exact-size/format target after its last consumer. The pool is renderer/request-owner scoped, tracks device/context identity and lease generation, treats acquired contents as undefined, and supports byte-cap/LRU/idle eviction. Before any guarded opaque/Geometry output canvas becomes author-visible, the executor transparently clears that planner-owned allocation inside its scheduled island; the clear is not a second pass or synchronization. Planner-owned finite Layer and non-empty TargetLayerScope isolation targets are likewise transparently initialized as part of their scheduled work. The externally borrowed root target is never auto-cleared.

Stable warmed bounds require zero fresh creation and zero pool misses. Changing sizes may miss legitimately. Peak-live count follows dependency liveness, not serial stage count. Externally owned root/presentation resources are reported separately and never returned to the pool. The pool survives individual requests and is disposed with its owning `RenderNodeRenderer` or production renderer lifetime.

### Synchronization

Synchronization occurs only for:

- declared CPU readback;
- backend transition;
- target-token dependency that requires it;
- platform-required ownership transition.

No same-backend compatible Shader stage introduces an implicit per-stage flush. Guarded Opaque/Geometry/TargetCommand callback canvases are executor-managed one-shot leases: their close restores canvas state without flushing, and only the synchronization schedule may submit or flush. Guarded canvases reject `SaveLayer`-backed opacity/blend/mask/paint APIs, hidden allocations, and nested rendering. Raw target callbacks are marked opaque-external because their internal flush/pass behavior cannot be counted. Diagnostics count scheduled transitions in `PlannedBackendTransitions`, performed transitions in `ExecutedBackendTransitions`, and retain their ordered plan/execute events separately.

## Execution and failure

`RenderRequestExecutor` is the sole owner of planner resources. It:

1. acquires program and target leases according to the plan;
2. executes islands and lowered scope-local target tokens in dependency/painter order;
3. validates dynamic cardinality/bounds against declarations;
4. records each committed fragment outcome exactly once and classification counters separately;
5. stages cache captures;
6. publishes output/cache state only after complete success;
7. releases every lease/resource/session in all paths.

On failure:

- preserve the first planning/render exception;
- mark the faulting and unexecuted dependents failed/skipped for reconciliation;
- discard partial island outputs and staged cache captures;
- invalidate callback sessions and handles;
- continue best-effort cleanup after disposal faults;
- report cleanup failures separately;
- perform GPU/native release on the valid rendering lifetime/thread.

Existing preview/delivery allocation-failure behavior is characterized before migration and reproduced at the appropriate execution seam. This feature does not normalize those outcomes.

## Nested requests

Two cases are distinct:

1. **Same target / same request**: referenced child and NodeGraph output use `RecordNode`/`RecordSubtree` and remain in the same graph, transaction, cache policy, diagnostics, ROI, and scale analysis.
2. **Separate target**: drawable brush/texture, scene drawable, thumbnail, particle child, and similar work records a `NestedRenderRequest`. It has its own complete fragment/value graph, scoped target tokens, and island plan but inherits allocator owner, diagnostics, intent, purpose, requested-region policy, scale limits, cache policy, and primary-failure owner.

An opaque callback that needs nested drawing records the nested request before parent GPU execution. It does not start an unplanned renderer from inside a running pass.

NodeGraph wrappers cannot retain public handles between requests. They use request-local input binding: parent inputs seed the nested output-node transaction, and wrapper/input nodes publish only while that binding is active.

## 3D boundary

`Scene3DRenderNode.Process` records scene resource/version, full declared bounds, scale behavior, and current-main failure policy as an `OpaqueBackendSource`. Execution later:

- resolves/clamps density;
- renders the complete declared 3D result;
- materializes one 2D RGBA16F input;
- records backend transition/synchronization;
- releases 3D resources through the request owner.

The 2D planner neither inspects nor propagates ROI inside 3D. Fusion may begin after the materialized 2D boundary when downstream work is otherwise eligible.

## Metadata-only queries

Bounds and hit-test requests use the same recorder and metadata analysis. They do not resolve pixel caches as execution substitutes, allocate targets, read media frames, execute deferred callbacks, increment frame render counts, mutate persistent frame caches, or replace `LatestFrame`. Each still emits its own immutable completed diagnostic snapshot in `Metadata` outcome and may become `Latest`.

`RenderNodeMeasurement.OutputBounds` reports `RootOutputExtent`; `QueryBounds` reports the contributing value query union plus target-command/scope query provenance. A Full Clear with empty query metadata therefore has full output bounds and empty query bounds, while a non-contributing capture anchor enlarges neither. Hit testing evaluates top-level query provenance in reverse painter order and uses declared CPU-only hit-test transforms, but returns false outside a non-null `RequestedRegion`. An operation unable to provide a sound CPU hit test declares conservative false/region behavior without executing pixels.
