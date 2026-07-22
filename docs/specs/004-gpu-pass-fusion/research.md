# Research: Renderer-Wide GPU Pass Fusion

## Research scope

This document resolves the implementation decisions needed to replace the current recursive executable-operation pull with one complete-request planner while preserving the existing filter-effect authoring lifecycle. It is based on:

- target baseline code at `43a38e665d9bf52548161a3917e748bd1457ff55`;
- the delivered feature-003 scale contracts;
- current production/test render-node and processor consumers;
- donor branch `yuto-trd/integrate-gpu-pass` at `7290836f43e4cdf1512b50dcc790a3f0a291cd0a` as extraction-only evidence;
- independently reproducible legacy golden provenance in the donor branch.

There are no unresolved design questions in this planning phase.

## R1. Plan one complete target-surface request

**Decision**: `Renderer` updates every participating drawable tree first, then records all top-level roots and target contributions into one `RenderRequest` before any planner-controlled 2D GPU work executes. The pipeline order is:

1. record the complete ordered effectful fragment DAG and embedded value DAG without cache lookup;
2. lower/discover scope-local target-token dependencies and resolve symbolic target regions against their actual external-root or offscreen scope;
3. resolve forward metadata, including separate root output and query extents;
4. propagate requested output regions backward;
5. substitute valid render-cache hits and insert miss capture points;
6. partition cache/backend/opaque execution islands;
7. compile fusion and resource schedules;
8. execute once and atomically publish successful caches.

**Rationale**: Current `Renderer.RenderObjects` pulls and executes each top-level drawable independently. `RenderNodeProcessor.Pull` can return a cache hit before traversing children, and `RenderNodeCacheHelper` can independently pull a subtree again to create a cache. These boundaries hide later drawables and upstream dependencies from one optimizer and make request-wide counters incomplete.

**Alternatives rejected**:

- **Effect-local planner**: cannot fuse across opacity or other ordinary render nodes and repeats the donor architecture's central limitation.
- **One planner per top-level drawable**: still cannot model backdrop, snapshot, painter order, or cross-root opportunities over the target surface.
- **Cache lookup during recursive traversal**: hides dependency metadata and makes later ROI/cache-island decisions unsound.

## R2. Make `RenderNodeContext` the only public recorder

**Decision**: The public node method is exactly `public abstract void Process(RenderNodeContext context)`. `RenderNodeContext` owns borrowed fragment inputs, semantic/opaque recording, and one explicit publication order containing both value contributions and target-command/capture/scope fragments. A command is returned as a handle and travels through parent inputs; there is no root-global void side list, public plan builder, or returning `Process` overload. The context also owns nested recording, cache disablement, and transferred resources.

The executable `RenderNodeOperation` type is removed and replaced by `RenderFragmentHandle`, a sealed, non-executable, non-disposable context handle. The new name reflects that one handle may denote an ordered fragment stream rather than one executable operation. Value contribution/cardinality and `CanBeUsedAsValueInput` are always readable while the handle is active. Concrete recording-time bounds/scale are available only through `TryGetMetadata(out RenderFragmentMetadata)`, and CPU hit testing only through `TryHitTest(Point, out bool)`; either query returns false with a default out value while an owning-target dependency is symbolic. It has no public constructor, factories, `Render`, `Dispose`, or ownership transfer. The new count type is named `RenderValueCardinality`, not output cardinality, because fragment existence and value count are independent.

Nested node recording maps parent inputs to fresh child-owned facade handles over the same internal fragments, invalidates those facades with the child transaction, and maps committed child outputs back to fresh parent-owned handles. Parent handle objects never cross the child lifetime. The high-level replacement for direct pull consumers is a disposable `RenderNodeRenderer`; it owns persistent structural/program caches, the target pool, accepted factory-created targets, and internal diagnostic state while borrowing its root, targets, and factory. Its single-result rasterizer returns a disposable `RenderNodeRasterization` carrying logical `Bounds`, `OutputScale`, nullable `Bitmap`, and `IsEmpty`; the result owns `Bitmap` when non-null and represents an empty output without inventing or allocating a zero-area bitmap. `RenderNodeMeasurement` separately reports execution-facing `OutputBounds` and query-facing `QueryBounds` before its existing scale/cardinality/fragment flags.

**Rationale**: Returning executable operations creates a second lifecycle and lets rendering escape the request owner. A context transaction can atomically commit all node effects or roll them back, and it gives public custom nodes one orthogonal vocabulary instead of a context plus builder plus callback-operation hierarchy.

**Alternatives rejected**:

- **Retain or internally derive `RenderNodeOperation` as the public authoring model**: its executable singular name preserves the removed lifecycle and misdescribes a handle that may carry a command, capture, scope, or ordered stream.
- **Separate `RenderPlanBuilder` argument**: duplicates context responsibilities and lets the two surfaces diverge.
- **Compatibility overload or `[Obsolete]` bridge**: leaves both ownership models live and conflicts with the repository's public-design policy.

## R3. Represent effectful fragments and value cardinality separately

**Decision**: One `RenderFragmentHandle` handle can represent an ordered runtime fragment stream. A fragment may carry a semantic value, an effect-only target command, a target-token-to-value capture, or a local/current-target scope. `RenderValueCardinality` counts materializable values only, so an effectful command is a real published fragment with `None`; internal fragment existence/order is separate. `CanBeUsedAsValueInput` tells an author whether every possible runtime value is exposed to value-consuming APIs after explicit dependencies are scheduled; it does not promise purity or target-independent execution. Shader preserves value order/cardinality, opacity/typed target scopes preserve the complete fragment, Geometry and opaque map produce one or zero-or-one value per value input, combine produces at most one value, and arbitrary runtime N-to-M belongs to expansion. Pure fan-out is explicit; effectful duplication is rejected except for a single-execution capture value shared by pure consumers.

**Rationale**: Some existing nodes can publish zero, one, many, duplicated, or runtime-discovered outputs. Forcing recording to know every runtime output would require eager media/GPU execution. A stream-valued edge keeps topology inspectable without inventing individual handles prematurely.

Each fragment/value record computes pure conservative aggregate metadata immediately when all required input metadata is concrete. When a fragment depends on `OwningTargetDomain`, the recorder may retain finite internal bounds/scale/hit-test hints for constructing the graph, but the fragment and every ordinary descendant remain publicly symbolic: `TryGetMetadata`, `TryHitTest`, and `RenderNodeContext.TryCalculateInputBounds` return false rather than exposing those hints. Standard and custom scale contracts consume complete resolved output bounds before their result becomes authoritative; they cannot observe the later ROI, and the 16,384-axis clamp is applied against those complete bounds. Target commands expose no value supply/cardinality, while public target capture resolves an explicit working density and remains non-contributing until `ContributeValues` is recorded. Runtime shrink/discard and later ROI cropping remain within the final declaration. Graph-wide analysis resolves/finalizes symbolic metadata and performs reverse ROI without executing deferred work.

**Alternatives rejected**:

- **One handle per eventual output**: impossible for runtime-discovered expansion without violating recording-only behavior.
- **Single opaque array callback**: preserves ordering but hides map/combine/expansion topology from lifetime, cache, and ROI analysis.
- **Implicit pass-through for zero published outputs**: makes intentional drop/no-output indistinguishable from author error and current `MemoryNode` behavior.

## R4. Use an ordered fragment DAG with scoped target tokens

**Decision**: The primary IR is an ordered `RenderFragment` DAG. Fragment kinds embed a `RenderValue` DAG for reusable pixels, effect-only `TargetCommand` nodes, `TargetCapture` token-to-value edges, ordered sequence publications, current-target scopes, scope-relative `TargetLayerScope` effects, and finite value-producing Layer scopes. Child publications—including commands—flow through parent `Inputs` in painter order. Only after the hierarchy is complete does lowering thread a `TargetToken` separately through each external root and nested offscreen scope. A command consumes/emits its current scope's token; a capture consumes/emits that token plus a request-owned value. `TargetRegion.Full` refers to the finite current scope, not automatically to the external root.

`TargetCommand` is a returned handle published in the same stream as value fragments. `TargetScope` replays one fragment exactly once with mechanically allocation-free transform/clip state; Opacity/Blend/OpacityMask are planner-visible typed scopes. Public `TargetLayerScope(inputs, TargetRegion)` is the typed offscreen-isolation effect: it records through the normal bottom-up `Process` path, keeps a Full region symbolic, and remains `CanBeUsedAsValueInput == false`. Its handle and ordinary downstream handles report metadata/hit-test unavailability while that dependency remains symbolic. A non-empty resolved region replays its ordered inputs into a transient local target during lowering/execution and composites that target back into the current painter target; the transient materialization is required unless the engine proves its removal equivalent. `Empty` is an order-only scope with no target allocation or pixel work. Existing `PushLayer(default)` records this public typed scope with `TargetRegion.Full`; no concrete-node pre-order traversal or early root-sized guess is used.

Public `Layer(inputs, Rect domain)` is deliberately different: it requires a finite non-empty domain, replays a mixed ordered stream into one local target, exposes exactly one reusable outer value, and publishes `EffectiveScale.Unbounded`. Concrete inputs retain tight child-derived bounds and hit testing. If any input is symbolic, the Layer is the explicit public metadata barrier: it immediately reports its full domain as conservative bounds and domain containment for hit testing, while preserving the internal symbolic dependency for final resolution and fan-out analysis. It cannot accept scope-relative Full because it needs that finite conservative boundary during recording. During scope-token lowering, the ordered outer transform/clip state is known before a nested TargetLayerScope Full is resolved. For root X-domain `[0, 100)`, `Transform(+10) -> PushLayer(default) -> Full clear` therefore resolves the child's local Full domain to `[-10, 90)`; freezing `[0, 100)` during child recording would incorrectly miss root `[0, 10)`. `RawTargetScope` and zero-input `RawTargetCommand` preserve unguarded legacy target behavior while conservatively consuming/producing the whole current target token and marking exact physical counters unavailable. This preserves `A -> Clear -> B`, finite `Layer { A -> Clear -> B }`, symbolic `TargetLayerScope { A -> Clear -> B }`, and `Snapshot -> Clear -> filtered draw(snapshot)` without moving the Clear or Snapshot across scopes. The public typed capture is non-contributing until wrapped by `ContributeValues`; the built-in backdrop uses a request-local identity binding across sibling transactions.

`OpacityMask` accepts the existing `Brush.Resource` semantics rather than pretending the brush is a finite alpha bitmap. The recorder snapshots/lower known brush kinds during `Process`, records DrawableBrush content as nested fragments, and uses the mask bounds as brush mapping geometry over the late-bound scope domain. It never starts `BrushConstructor`/a renderer during execution. Guarded callback canvases reject every `SaveLayer`-backed opacity/blend/mask/paint API and hidden target allocation; retained raw hooks are the marked raw forms above.

**Rationale**: A pure value DAG does not encode painter order/current-target reads, while an early root-global command list loses child interleaving and Layer/decorator scope. Embedding both in composable fragments retains reusable value dependencies and lowers target tokens only when the correct scope is known.

**Alternatives rejected**:

- **Value DAG plus an independently appended global command list**: loses `[A, Clear, B]` ordering through parents and makes a child Clear affect the root instead of its Layer.
- **Convert the target into an ordinary graph value after every draw**: creates artificial full-frame values/materializations and complicates external root ownership.
- **Resolve `PushLayer(default)` before recording children**: an enclosing parent transform/clip has not yet been lowered in the bottom-up tree, so a root-sized guess can under-render and bypasses the ordinary public `Process` contract.
- **Let public value-producing Layer accept symbolic Full**: it would have no finite conservative domain with which to reestablish concrete `TryGetMetadata`/`TryHitTest` results during recording.

## R5. Resolve bounds and requested regions after recording

**Decision**: Every typed non-invariant value has a `RenderBoundsContract` with a forward output map and either a backward required-input map or a conservative full-input declaration. The contract lives in `Beutl.Graphics.Rendering` beside hit-test/scale and target primitives because Shader, Geometry, render-node scopes, and opaque descriptions all consume it; Effects descriptions reference that renderer-wide primitive rather than making custom render nodes import an effect-only namespace. `CreateFullInput(transformBounds, structuralKey)` covers non-identity forward maps whose inverse ROI cannot be proven. After complete recording, scope-token dependency lowering resolves Full access against the actual finite external root, `TargetLayerScope`, or Layer scope; only then is pure conservative forward metadata finalized/validated, followed by reverse requested-region propagation.

Forward metadata retains two root aggregates. `RootOutputExtent` is the conservative union of contributing value bounds and every potentially pixel-writing root target-effect region after scope transforms/clips. Read-only captures and order-only accesses do not enlarge it; potentially writing work remains included even when its `QueryBounds` is empty. `QueryBounds` separately unions contributing-value and target-command/scope query provenance for measurement/layout and hit testing; non-contributing capture anchors and order-only effects without query metadata do not enlarge it. A null `RequestedRegion` selects `RootOutputExtent` for the root requirement, final commit, and rasterization domain; a non-degenerate value is clipped to that extent, while an explicitly degenerate value preserves its authored empty bounds and origin. `RenderNodeMeasurement` exposes both aggregates, and `RenderNodeRasterization.Bounds` records the resolved logical raster domain.

`RequestedRegion` never supplies the available target domain. A real destination supplies the root domain; a target-less caller must set a finite non-empty `TargetDomain` whenever a resolved root `TargetRegion.Full` access requires one. QueryBounds, RootOutputExtent, RequestedRegion, and finite value anchors never substitute. A Full TargetLayerScope nested inside a finite Layer resolves from that Layer and does not require an unrelated root guess; public value-producing Layer already has its explicit finite domain. Target reads may expand reverse ROI up to the finite current target domain. Fan-out requirements union upstream; unknown opaque work requests its full declared input. Required-region state is an explicit `Full`, `Empty`, or finite `Region(Rect)` value; an invalid rectangle from an author mapping is an error, not a synonym for full.

Per-node resolved ROI is not exposed through `RenderNodeContext.Process`, because it does not exist soundly until downstream dependencies are recorded. Execution-time Shader and Geometry contexts receive final bounds, density, and required regions.

**Rationale**: The donor's top-down requested-bounds propagation occurs while children are pulled, before later effect bounds are known. That can under-request coordinate-changing operations. A separate region map also keeps provisional internal hints distinct from author-readable metadata and avoids making cache substitution change recording-time query results.

**Alternatives rejected**:

- **Expose `RequestedBounds` during `Process`**: invites node structure or allocation decisions based on incomplete information.
- **Always render full input**: correct but defeats ROI goals and hides missing bounds contracts.
- **Infer reverse bounds from forward bounds**: not sound for blur, transform, convolution, clip, and many custom operations.
- **Use QueryBounds as the default output or target domain**: drops pixel-writing commands such as Full Clear when their measurement/hit-test metadata is intentionally empty and conflates layout metadata with target availability.

## R6. Resolve render caches after graph discovery

**Decision**: Recording wraps eligible pure-value fragment results in cache candidates but never short-circuits traversal. After bounds/ROI resolution, `RenderCacheResolver` selects valid materialized hits and inserts capture points for selected misses in the current schedule. A candidate transitively containing a target command, current-target scope, target capture/read, or other target-token dependency is ineligible as a whole-subtree boundary unless the planner has a complete immutable prior-token pixel identity and coverage. Borrowed external-root/prior pixels are request-unique, so captures from them never hit across requests. Pure child value candidates remain independently selectable, and substitution preserves every fragment/token edge and publication position. Query metadata/provenance stays attached to the original producer. Cache publication occurs only after complete-request success.

Render-output cache identity includes subtree revision, built-in scalar parameters, canonical Shader uniform values, explicit deferred-callback `RenderRuntimeIdentity` keys, declared resource key/versions, logical bounds, covered region, effective density, format, render intent/purpose where relevant, and device/context identity. A null callback runtime identity is synthesized uniquely for each recording and therefore disables cross-request pixel-cache reuse without risking stale pixels. Structural plan and program identities deliberately exclude runtime-only values and resource contents.

**Rationale**: This preserves existing per-child cache granularity without hiding dependencies. It also allows a static prefix to be cached while an animated tail remains in the same globally visible request.

**Alternatives rejected**:

- **Donor `PrefixOutputCache` or nested effect caches**: solves the symptom inside one filter graph and creates competing cache owners.
- **Independent cache-generation pull**: duplicates execution and separates allocation/failure accounting from the request.
- **Use one identity for output and structure**: either recompiles on every parameter frame or reuses stale pixels.

## R7. Separate request purpose from delivery intent

**Decision**: Introduce orthogonal request options:

- `RenderIntent`: `Preview` or `Delivery`, preserving current allocation/failure policy;
- `RenderRequestPurpose`: `Frame`, `HitTest`, `Bounds`, `CacheWarmup`, or `Auxiliary`.

Purpose is inherited by same-request nested nodes. Separate-target nested requests inherit both values unless they explicitly declare a boundary. `HitTest` and `Bounds` record metadata but never call the GPU executor or mutate persistent frame caches, the latest completed Frame snapshot, or render counts. They still emit their own immutable request-wide diagnostic snapshots whose records reconcile to the metadata outcome.

**Rationale**: Current independent pulls for rendering, hit testing, bounds, cache warm-up, and nested work can share mutable state accidentally. Preview/export allocation behavior is a different concern from why the request exists.

**Alternatives rejected**:

- **One combined enum**: creates a Cartesian product and encourages missing propagation cases.
- **Treat all auxiliary work as frame rendering**: pollutes cache and diagnostic state and may perform unnecessary GPU work.

## R8. Share one hardened Shader description

**Decision**: Add renderer-neutral `ShaderDescription` in `Beutl.Graphics.Effects`, accepted by both `FilterEffectContext.Shader` and `RenderNodeContext.Shader`.

It has exactly two forms:

- `CurrentPixel`: a mechanically validated `half4 apply(half4 color)` snippet with identity bounds and fusible post-upstream-coverage semantics;
- `WholeSource`: a complete shader with mandatory bounds contract that always forms its own pass in this feature.

There is no `IsCoordinateInvariant` setter and no `WholeSourceInvariant` factory. Current-pixel validation uses a lexer/token model, rejects entry-point/coordinate built-ins and source sampling outside the restricted binding grammar, verifies declarations and binding names/types, and rejects unsupported constructs rather than trusting author assertions. Source text and binding names are structural; uniform values, bounds, density, target/device size, and resource contents are execution parameters. A direct unmanaged uniform overload has canonical binding and cache identity. Custom uniform/resource binders have separate structural and runtime keys; their null runtime key is request-unique so captured/global state cannot produce an unsafe cache hit by default. Program-cache lookup uses a stable hash for bucketing and full normalized source/signature equality for correctness.

Child samplers are represented by deferred/provider or owned-resource descriptions resolved by the executor. The canonical API does not accept an eager caller-created native `SKShader` as a fusible child.

CurrentPixel has no output-position coordinate and permits only value-coordinate resources proven independent of destination position. That validator proves coordinate independence, not the premultiplied-coverage property `f(kx) = kf(x)` for every analytic/antialiased coverage value `k`. CurrentPixel therefore consumes pixels after upstream geometry, text, path, or antialiased-clip coverage has been resolved. Arbitrary public stages may fuse with each other and with invariant opacity after that point, but may not fold into the coverage-producing draw. Only an engine-known operation whose coverage homogeneity is mechanically proven may cross that boundary; there is no public author assertion for it. WholeSource receives local output device pixels (`0.5,0.5` at the first pixel center); the execution context exposes the logical origin, complete output bounds, required logical region, device bounds, working density, and input supply density. The implicit `src` child maps that local coordinate back through input logical origin/density. Extra resource bindings explicitly declare value, output-logical, or output-device coordinates and create any local matrix during scoped execution binding.

CurrentPixel preserves the supply of an already coverage-resolved input until the whole eligible run reaches one materialization; the standard supply-driven density is then resolved once for the run. An unbounded vector fragment first materializes its analytic/antialiased coverage before arbitrary CurrentPixel execution, so coordinate validation cannot move a nonlinear public stage into the source draw. WholeSource is a materialization boundary and publishes the standard concrete density from mapped complete bounds. Direct/custom unmanaged uniform values use a validated canonical scalar/vector/matrix allowlist and reject pointers/native handles/padding-dependent blobs.

**Rationale**: This preserves the useful donor snippet-merging model while fixing its trust and recording-time native-allocation gaps. It gives existing effect authors a small opt-in without replacing `ApplyTo`.

**Alternatives rejected**:

- **Author-declared invariance**: can silently produce incorrect fusion when a shader reads coordinates or neighboring pixels.
- **Author-declared coverage homogeneity**: a false `f(kx) = kf(x)` claim changes antialiased edge pixels; the engine must prove any participant allowed to cross coverage production.
- **Whole-source shaders in fused runs**: their sampling behavior is too broad to prove equivalent in this feature.
- **Bake animated values into source**: defeats structural/program reuse and makes cache identity unstable.

## R9. Share one deferred Geometry description

**Decision**: Add `GeometryDescription` in `Beutl.Graphics.Effects`, accepted by both authoring contexts. Geometry is a one-input/zero-or-one-output ordered map, has mandatory bounds and CPU hit-test contracts plus a stable structural key, and explicitly declares whether CPU readback is required. A null explicit structural key deterministically defaults to the callback method identity plus operation kind; shape-changing captured choices require an explicit equality-stable key. Pixel-affecting captured scalar state uses a distinct runtime identity, with null conservatively request-unique. It is a non-fused execution island in this feature.

The executor invokes its callback with an active-token-guarded `GeometrySession`. Each element uses the standard supply-driven materialization density and the complete mapped bounds clamp. Before callback entry the executor transparently clears the planner-owned output inside the scheduled island. Opaque, Geometry, and TargetCommand sessions share one `RenderExecutionInput` facade rather than duplicating identical input types; owning descriptions control whether its one-shot `UseSnapshot` is enabled. The Geometry session exposes complete output bounds, resolved required region/device bounds, output/working/maximum scales, one borrowed input, and a non-disposable scoped canvas facade. The facade maps canonical rounded device bounds to composition-global logical coordinates, preclips the resolved allocation, and permits one executor-managed `ImmediateCanvas` action whose close does not introduce an implicit flush. Author disposal, snapshot, `SaveLayer`-backed state, nested draw/renderer entry, undeclared native/target use, hidden allocation, and hidden flush/synchronization are rejected; declared resources and the request-owned bitmap are authorized only in their nested same-session scopes. Geometry permits output discard or shrink within allocated forward bounds. Input readback is one-shot when declared; the request disposes the bitmap before return. All facades reject retained use.

**Rationale**: Geometry is the honest deferred escape hatch for work that is not a current-pixel Shader. Mandatory bounds and readback metadata let the global planner schedule it without executing it during recording.

**Alternatives rejected**:

- **Reuse `CustomFilterEffectContext` as the new primitive**: it exposes target creation/opening and therefore owns planning decisions imperatively.
- **Let Geometry allocate arbitrary outputs**: hides fan-out/resource lifetime; dynamic topology belongs to the explicit opaque expansion path.
- **Implicit readback**: introduces uncounted synchronization and backend stalls.

## R10. Preserve `ApplyTo` and lower legacy items conservatively

**Decision**: `FilterEffect.ApplyTo(FilterEffectContext, Resource)` remains the only abstract effect entry point. Existing `FilterEffectContext` methods and ordering remain available. `Shader(ShaderDescription)` and `Geometry(GeometryDescription)` append to the same transactional ordered item list. During render-node recording, the effect resource invokes `ApplyTo` to produce descriptions only; activation, target allocation, GPU access, and custom callback execution are deferred.

Existing color filters, Skia filters, and transforms lower to typed operations only when their equivalence and bounds are proven. Unsupported engine-controlled items lower to the appropriate capability-guarded opaque value or target-scope topology. Existing `CustomEffect` keeps its raw `CustomFilterEffectContext`/materialized `EffectTarget` execution behavior and therefore lowers to a distinct `LegacyCustomEffect` opaque-external island. When its render-node input is symbolic, the public legacy `FilterEffectContext` begins with `Bounds == Rect.Invalid`; the recorder uses an internal finite hint only to retain the provisional opaque segment. Missing bounds stay symbolic until scope lowering can resolve the local owning target after enclosing transforms, clips, and finite scopes, at which point the retained bounds-transforming items are evaluated again from the resolved input bounds. Once an unknown item starts the runtime sequence, later Skia/custom/Shader/Geometry items remain in that same island and use actual target bounds. The executor crops only its final semantic outputs to the resolved domain; the callback's internal allocations remain unmeasured and unconstrained. Every other retained public/protected raw-`ImmediateCanvas` hook—including custom `IBackdrop.Draw`, audio-visualizer foreground/shape callbacks, `RawTargetScope`, and `RawTargetCommand`—lowers to `LegacyRawCanvas` opaque-external work. Their outer fragment/order/resource ownership is planned, but internal passes/flushes are unmeasured, nothing fuses through them, and diagnostics mark the limitation. The built-in SnapshotBackdrop/DrawBackdrop pair instead uses typed capture/binding and never calls the raw interface in the same request. A repository-wide migration census must classify every old `CreateLambda`, decorator, target/surface wrapper, and raw-canvas hook; none remains as an executable-operation escape.

`FilterEffect.Resource.CreateRenderNode()` remains the customization point, but returned nodes use the new void recording contract.

**Rationale**: Ordinary effect authors that stay within `FilterEffectContext` operations remain source-compatible while the renderer gains semantic visibility incrementally. The old operation-backed `EffectTarget` escape necessarily migrates with executable `RenderNodeOperation`; unsupported pixel work itself stays correct through an execution-time opaque boundary instead of becoming a false optimization.

**Alternatives rejected**:

- **`Describe(EffectGraphBuilder, ...)` replacement lifecycle**: caused the abandoned branch's migration expansion and is explicitly outside the restart.
- **Migrate every built-in effect before proving the seam**: increases risk and diff size without proving renderer-wide fusion.
- **Coalesce each `FilterEffectGroup` into one node**: changes cache granularity and makes group layout, rather than global semantics, the optimization mechanism.

## R11. Use one request owner for resources and synchronization

**Decision**: `RenderRequestExecutor` owns all request-scoped planner-acquired intermediates, program leases, owned materialized inputs, deferred sessions, owned declared resources, cache-capture outputs, and synchronization transitions. It owns only the token/lease for an explicitly borrowed external resource and never disposes or permits pixel-affecting mutation of that raw value. Every deferred author resource uses `RenderResource<T>`: `Own<T>` requires a disposable reference type, while `Borrow<T>` accepts any reference type because no disposal transfers. A request-family reference table rejects duplicate `Own` and Own/Borrow conflict. Repeated Borrow with the same explicit key/version coalesces, an explicit mismatch is rejected, and a null key creates a distinct request-local identity that disables cross-request output-cache reuse. Keys are lightweight immutable CPU identities. The token carries runtime cache identity, while opaque, Geometry, target-scope, and target-command descriptions carry analogous scalar runtime identity. The persistent `RenderNodeRenderer` owner retains structural/program caches and pooled targets across requests. The executor schedules exact-size RGBA16F leases by liveness, transparently initializes callback outputs, tracks lease generation, and discharges every acquire by pool return/disposal or successful cache-ownership transfer while preserving cleanup failures and the first primary failure.

Working scale for each ordinary materialization remains:

`min(max(OutputScale, densest concrete input supply), MaxWorkingScale)`

followed by the existing per-buffer 16,384-pixel dimension clamp against the complete conservative operation output bounds. The pure helpers move from `RenderNodeContext` to the independent public `RenderScaleUtilities` type because 3D, brushes, export policy, and planning use them outside a recording transaction; all callers migrate together with no forwarding shim. Concrete inputs can resolve supply during recording. Symbolic dependencies keep only an internal hint and make the public handle's metadata query fail for the remainder of that recording transaction; after those handles invalidate, graph-wide analysis establishes internal concrete metadata. `EffectiveScale.Unbounded` continues to mean vector/lossless supply, never an unresolved raster target. `RenderScaleContract.MapInputSupply(Func<EffectiveScale, EffectiveScale>, structuralKey)` is the declarative one-input resolved-supply transform used by Transform and DrawableGroup. It is restricted to element-wise one-input maps, may return `Unbounded`, and is reevaluated with the resolved input supply after symbolic dependency resolution. A coverage-resolved unbounded input may remain unbounded through a CurrentPixel run until its eventual materialization. Public target capture has no value supply and is deliberately a declared-density materialization/resampling boundary: the standard policy starts from `OutputScale`, while a custom resolver always receives empty `InputSupplies` and may use only `OutputBounds`, `OutputScale`, and `MaxWorkingScale`. Neither form can observe a finite Layer or TargetLayerScope density resolved later, so it may intentionally downsample a denser owning target. The engine-internal backdrop capture may late-bind the resolved density of its owning root, finite Layer, or TargetLayerScope target; a nested public handle for that symbolic capture reports metadata and hit testing as unavailable until a finite Layer establishes a concrete boundary. Later requested-region analysis crops the materialized logical region but does not increase or recompute density. Root target density remains the active destination density. Execution binds final cropped device bounds and other runtime values; recording does not bake them into structural Shader or Geometry descriptions.

**Rationale**: A single owner can reconcile every acquisition, release, synchronization, and cache publication. Donor pool/program-cache algorithms are useful, but their effect-prefix and Vulkan-lifecycle coupling is not.

**Alternatives rejected**:

- **Let each pass allocate/dispose its own target**: prevents liveness reuse and makes cleanup/counter reconciliation incomplete.
- **Cap intermediates at output scale**: violates feature 003 and loses concrete source density.
- **Make public TargetCapture inherit its enclosing target density**: makes a reusable public operation's sampling semantics and cache identity depend on a later owning scope. Public capture therefore keeps declared-density resampling; the internal backdrop path uses symbolic metadata availability, and `Unbounded` remains reserved for vector/lossless supply rather than an unresolved raster target.
- **Require zero misses for changing target sizes**: exact-size pooling legitimately misses when dimensions change; the zero-allocation gate applies only to stable warmed bounds.

## R12. Treat 3D and unsupported backends as explicit boundaries

**Decision**: `Scene3DRenderNode.Process` records an opaque backend source containing scene/version/bounds metadata. Execution later renders the declared full 3D bounds at resolved/clamped density, records the backend transition and synchronization, and publishes one materialized 2D value. Backward 2D ROI does not enter the 3D renderer.

Every public Shader form has a supported unfused ordinary-2D path. GPU-specific pass-count tests may self-skip without a suitable device, but ordinary rendering must not fail merely because fusion is unavailable. Invalid source/bindings or program creation are explicit render failures, not identity fallback.

**Rationale**: The feature is a 2D request redesign, not a 3D renderer rewrite. Correct fallback is a product requirement distinct from hardware-gated performance evidence.

**Alternatives rejected**:

- **Inspect the 3D graph**: expands scope into a different backend and synchronization model.
- **Silently disable a Shader on unsupported fusion**: corrupts output.
- **Require GPU execution-shape assertions on all CI hosts**: conflicts with the repository's hardware-gated graphics tests.

## R13. Extract donor algorithms, never donor architecture

**Decision**: Port leaf implementations and matching invariant tests from donor final HEAD only after adapting them to the renderer-wide ownership model.

### Extraction candidates

- `SkslSource`, `SkslLexer`, uniform/child binding logic, and `SkslSnippetMerger`;
- `RenderBoundsContract` concepts and Geometry session/input math;
- program-cache collision, reset, LRU, and re-entrant lease behavior;
- render-target pool exact-size buckets, LRU/byte cap, generation checks, context eviction, and cleanup patterns;
- raw linear-RGBA16F golden storage, Alpha MAE, immutable/provenance tooling;
- ROI, binding, merger, failure-injection, cache, pool, and persistent-lifetime benchmark tests.

### Explicit denylist

- `EffectGraphBuilder`, `FilterEffect.Describe`, or removal of current filter contexts;
- `PlanFilterEffectRenderNode`, effect-private graph/compiler ownership, `PlanCache`, `NestedGraphPlanCache`, or `PrefixOutputCache`;
- wholesale `PlanExecutor` or `EffectGraphCompiler` copy;
- `WholeSourceInvariant` and its author-asserted fusion contract;
- eager native `SKShader` child bindings as the canonical recording API;
- FilterEffectGroup coalescing;
- public Compute/Split/Composite/NestedGraph vocabularies as prerequisites;
- donor-wide RenderIntent/lifecycle/resource-tail changes unrelated to this request pipeline;
- donor effect-local diagnostic definitions or timing targets.

**Rationale**: Donor and target differ by hundreds of files and later donor fixes materially changed early commits. Cherry-picking would import the architecture being abandoned. Final leaf code plus focused tests captures the useful work without inheriting its ownership boundary.

## R14. Establish fresh evidence and a non-friend contract gate

**Decision**: Add `tests/Beutl.PublicApiContractTests` as a lean NUnit project without `InternalsVisibleTo`. It compiles plugin-style `ApplyTo`, Shader/Geometry authoring, fragment publication/value-input inspection, TargetCapture/ContributeValues, Layer/TargetScope/TargetCommand, RawTargetScope/RawTargetCommand, shared execution-input callbacks, owned/borrowed resources, and high-level renderer use using public API only. Its project shape may be adapted from donor, but no `Describe`-lifecycle tests are copied.

Before behavior changes, capture a new target-baseline category with starting SHA, generator script/patch, environment, file hashes, immutable `AssertExisting` behavior, and non-vacuity comparisons. The generator is not compiled into feature tests: `docs/specs/004-gpu-pass-fusion/evidence/target-baseline-generator.patch` is applied by `generate-target-baseline.sh` only inside a temporary worktree pinned to the starting SHA, and only immutable RGBA16F artifacts plus their manifest are copied back. `run-paired-visual-evidence.sh` owns the starting-SHA/feature invocation, requires an exact matching fingerprint before comparison, and fails explicitly rather than skipping or selecting a foreign-device reference. The manifest hashes the artifacts, patch, generator script, and paired runner and records exact OS, architecture, backend, device, driver, graphics-library, and runtime fingerprints. Normal CI uses fusion-disabled versus fusion-enabled rendering in the same process/device and always verifies manifest/hash integrity. An internal request `FusionMode` supplies that evidence seam, is included in structural-plan identity, and is available only to friend tests/internal renderer entry points; the public renderer does not expose an optimization toggle. That check complements rather than replaces the paired provenance proof.

Use linear-light SSIM >= 0.99, linear RGB MAE <= 0.02, and alpha MAE <= 0.02 for scale-1 parity. Antialiased thin-line/path workloads additionally use an edge-band local-MAE and maximum-channel-error oracle so a small number of corrupted edge pixels cannot disappear in a whole-frame average. Normal CI uses a fixed device-independent per-channel maximum error of 0.02 for its same-process pair; the paired runner may enforce a tighter fingerprint-specific bound only from the exact matching manifest. Multiple scales/regions and fallback use freshly recorded baseline tolerances. Every workload must change by more than its parity threshold plus a recorded margin when the operation under test is disabled.

The donor's `004-parity-strong` eight references may be imported as supplemental regressions because they are independently reproducible from a historical legacy activator. Donor absolute timing and effect-local counter values are historical only. Request-wide counters and all timings are remeasured on the target baseline with persistent production-equivalent renderer state.

**Rationale**: Friend tests can accidentally depend on internals and donor post-redesign images cannot prove current-main parity. Provenance, non-vacuity, and request-wide accounting make performance/correctness claims auditable.

**Alternatives rejected**:

- **Regenerate a missing golden from the implementation under test**: allows a regression to approve itself.
- **Use RGB-only metrics**: misses alpha-only corruption.
- **Use a committed device-specific blob as an unconditional CI oracle**: backend/device differences can masquerade as regressions or approvals; normal CI must compare both modes on one device, while historical paired evidence is accepted only under an exact fingerprint.
- **Adopt a fixed historical percentage speedup**: donor timings were machine-specific and included a corrected benchmark-lifetime error.

## R15. Keep request-wide diagnostics internal

**Decision**: Request-wide diagnostics are an internal renderer/evidence seam in this feature. Neither `IRenderer` nor `RenderNodeRenderer` gains a public provider, sink, writer, snapshot factory, or telemetry schema. The production renderer, friend tests, and in-tree benchmarks use immutable completed snapshots through internal hooks; mutable counters, native identities, and planner state never escape.

The request outcome denominator is committed `RenderFragment` records. Value, target-command, capture, scope, Layer, opaque, and materialization counts are non-exclusive classification counters and never sum to the outcome total. `Latest` means the most recently completed request of any purpose; `LatestFrame` means the most recent `Frame`. Metadata-only Bounds/HitTest requests publish their own `Metadata` snapshots without altering frame caches/counts or `LatestFrame`. `HasOpaqueExternalWork` becomes true when any `LegacyCustomEffect`/`LegacyRawCanvas` fragment is recorded, even if execution is skipped; `OpaqueExternalExecutions` increments only when such a callback is entered.

**Rationale**: Acceptance needs exact counters/events, but plugin-facing telemetry is not a product requirement of renderer-wide fusion. Keeping evidence instrumentation internal limits this breaking change to authoring/execution ownership and allows the planner schema to evolve without public API churn.

**Alternatives rejected**:

- **Default nullable `IRenderer.Diagnostics` or a new public provider**: conflates rendering with instrumentation and freezes a large telemetry surface without a user requirement.
- **Public mutable request writer/event recorder or completion sink**: lets observers affect execution and freezes internal scheduling representation.
- **Value-plus-command outcome equation**: double-counts mixed fragments and cannot reconcile scoped sequences.

## Current migration census

The starting renderer contains 29 production `Process` overrides covering:

- vector/media sources: geometry, rectangle, ellipse, text, image, video, audio visualization;
- unary/scope work: opacity, transform, rectangle/geometry clip, blend, mask, push;
- pass-through/combine: container, layer, drawable-group nodes, memory/drop;
- destination work: clear, snapshot backdrop, draw backdrop;
- nested/bridges: referenced child, filter effect, operation wrapper, NodeGraph output, scene bitmap;
- opaque/backend work: particle and 3D.

Seven test overrides and all direct `RenderNodeProcessor`, executable-operation factory/subclass, operation-backed `EffectTarget`, `OperationWrapperRenderNode.SetOperations`, cache replay, hit-test/bounds, NodeGraph measure/preview, ProjectSystem `SceneDrawable`, Editor save-frame, AgentToolkit query, thumbnail, brush, texture-source, and player consumers migrate in the same breaking change. The operation API appears in 24 starting-SHA test files; the golden harness is one of them and hides the migration from its 18 consumers. The feature-003 scale helpers have 24 direct caller/reference files (15 production and 9 tests), all of which migrate to `RenderScaleUtilities` without a forwarding shim or assertion changes beyond the renamed owner. A repository-wide census test or source scan must keep these lists from silently shrinking around an unmigrated executable path.
