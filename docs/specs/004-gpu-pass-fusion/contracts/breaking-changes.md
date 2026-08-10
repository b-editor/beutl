# Breaking Changes and Migration Contract

Feature 004 intentionally replaces the executable render-node pull API. It is breaking for custom `RenderNode`/`RenderNodeOperation` authors and direct `RenderNodeProcessor` consumers in `Beutl.Engine`, `Beutl.Editor`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and downstream plugins. Existing `FilterEffect.ApplyTo` operation calls remain source-compatible unless they directly use the removed operation-backed `EffectTarget` members or subclass/consume the changed render-node API, but synchronous author-time metadata access is intentionally stricter: symbolic `Bounds` is unavailable and symbolic or branch-dependent `WorkingScale` must be probed with `TryGetWorkingScale` and bound later in an execution callback. Generated `EngineObject.Resource` authoring, `GetOriginal()` nullability, object-resource replacement ownership, and direct `FilterEffectActivator` construction also change as described below.

The slice-2 auditor's stored integration message carrying this public change must use a breaking Conventional Commit. It must contain a literal `BREAKING CHANGE:` footer; a Markdown heading is not a substitute. Use this template:

```text
refactor(engine)!: record complete render requests before execution

BREAKING CHANGE: Beutl.Engine, Beutl.Editor, Beutl.NodeGraph, Beutl.ProjectSystem, Beutl.AgentToolkit, application render-node consumers, and out-of-tree Engine/plugin RenderNode implementations now use void Process(RenderNodeContext), context-owned RenderFragmentHandle values, and high-level request entry points. Executable/disposable RenderNodeOperation, RenderNode.PrepareForProcess(ImmediateCanvas), RenderNodeProcessor Pull APIs, OperationWrapperRenderNode.SetOperations, and operation-backed EffectTarget members were removed. PrepareForProcess work migrates to ordered typed/opaque fragments recorded from Process without a live canvas. RenderNodeContext is now an engine-created sealed recorder: Input/CalculateBounds/the cache setter migrate to Inputs/TryCalculateInputBounds/DisableRenderCache, and its static scale helpers move to RenderScaleUtilities. RenderFragmentHandle no longer exposes direct Bounds, EffectiveScale, or HitTest members; authors use TryGetMetadata and TryHitTest and must handle symbolic owning-target dependencies. Rasterize now returns one owned RenderNodeRasterization carrying its logical Bounds, OutputScale, and nullable Bitmap; Measure reports separate OutputBounds and QueryBounds. Existing FilterEffect.ApplyTo operation calls remain available, but the FilterEffectContext.Bounds property is removed (engine-internal only) and symbolic inputs may make WorkingScale unavailable: effect authors must use TryGetWorkingScale and defer bounds/scale-dependent parameters to Shader, Geometry, or CustomEffect execution callbacks. Custom nodes returned by FilterEffect.Resource.CreateRenderNode must migrate.

BREAKING CHANGE: `ImmediateCanvas` no longer exposes public draw overloads that accept the lease-bound `LoweredBrush` or `LoweredPen` types. Painted callbacks draw lowered paint only through the active `PaintedRenderSession.Canvas` (`PaintedRenderCanvas`); directing those session capabilities to a captured author-owned canvas would escape request ordering, synchronization, diagnostics, and cache control. Ordinary public `ImmediateCanvas` brush/pen overloads are unchanged.

BREAKING CHANGE: `RenderNodeCacheHelper.MakeCache`, `CreateDefaultCache`, and `CanCacheRecursiveChildrenOnly`, together with `RenderNodeCache.RejectCache` and `IsCacheRejected`, are removed. Cache lookup, miss capture, and atomic publication now occur only inside the complete request after dependency and region analysis; callers render through `RenderNodeRenderer`/the production `Renderer` and use `Invalidate` or `RenderNodeCacheHelper.ClearCache` to discard retained entries.

BREAKING CHANGE: `RenderNodeCache.Density` is no longer public, `UseCache` remains an internal inspection-only accessor, and `StoreCache` is removed entirely. Cache payloads are renderer-owned and may contain multiple outputs at independent effective scales, so a scalar public density or target/bounds-only tuple is not a sound inspection contract. Plugin code controls retention through `ReportRenderCount`, `CanCache`, `Invalidate`, and `RenderNodeCacheHelper.ClearCache` rather than reading or seeding engine payloads; seeding a cache entry is not a plugin operation.

BREAKING CHANGE: `TargetInputReadback` is renamed to the operation-neutral `RenderInputReadback` and is shared by `TargetCommandDescription.InputReadbacks` and `OpaqueRenderDescription.InputReadbacks`. Opaque `requiresReadback: true` migrates to one selector per authored input, normally `inputReadbacks: [RenderInputReadback.All, ...]`; `None` and `Values` avoid synchronizing unrelated runtime values. `OpaqueRenderSession.CreateOutput(bounds, density)` may select an independent finite positive density per runtime output. Direct `RenderNodeRenderer.Render`/`Rasterize` frame hosts set `RenderNodeRenderRequest.Purpose = RenderRequestPurpose.Frame`.

BREAKING CHANGE: `IRenderer.GetBoundaries`, `IRenderer.GetBoundary`, and `Renderer.RecalculateBoundaries` are render-thread-affine queries. Bounds are resolved lazily from the recorded render graph after `Render` or `UpdateFrame`, so callers must dispatch these queries through `RenderThread.Dispatcher` instead of reading them from arbitrary threads.

BREAKING CHANGE: `RenderCacheOptions.Default` now denotes the same disabled policy as `RenderCacheOptions.Disabled`, so unchanged `Beutl.Engine` and plugin callers no longer opt into persistent render caching implicitly. `RenderNodeRenderRequest.UseRenderCache` is removed and replaced by `CacheOptions`: migrate `true`/`false` to `RenderCacheOptions.Enabled`/`RenderCacheOptions.Disabled`, and migrate request-specific admission rules to `new RenderCacheOptions(enabled, rules)`. Callers that require persistent caching must select `RenderCacheOptions.Enabled` explicitly or set `RenderNodeRenderRequest.CacheOptions = RenderCacheOptions.Enabled`.

BREAKING CHANGE: `RenderNodeRenderer` operations now accept an optional complete `RenderNodeRenderRequest`. `RenderNodeRendererOptions` composes a sanitized `DefaultRequest` with the renderer-lifetime `TargetFactory`; request intent, target domain, requested region, output/working scales, and cache policy move under that descriptor. A null operation argument selects the default snapshot, while a supplied descriptor completely replaces it, allowing one persistent renderer to serve changing regions and scales without discarding its structural/program caches or target pool.

BREAKING CHANGE: `EngineObject.Resource.GetOriginal()` and every generated typed form now return nullable values for detached resources. Callers that require an attached backing object migrate to `RequireOriginal()`; callers that support detached authoring handle null or use `EngineResourceIdentity.Of`. A bare `EngineObject.Resource()` now starts enabled, public generated `Resource()` constructors apply declared value/object defaults and inherit `IsEnabled` from their defaults owner, and `ToResource` uses a separate attached fast path.

BREAKING CHANGE: Resource-generating `EngineObject` subclasses without an explicit defaults constructor must expose every generated `IProperty` from stable declaration-time state: an auto-property has a declaration initializer, or a computed getter directly returns a declaration-initialized readonly instance field, and no ordinary constructor replaces that storage. `BESG003` reports unsupported or constructor-replaced storage. `[ResourceDefaultValuesConstructor]` now marks one defaults-only instance constructor; the old factory-oriented `ResourceDefaultValuesProviderAttribute` name is removed without an alias. The constructor takes exactly one `ResourceDefaultValuesConstruction`, passes that marker directly to base, and may only assign stable generated property storage from recognized non-disposable `Property` factories. `BESG005` rejects an invalid/ambiguous defaults constructor; `BESG006` requires every generated derived type in a defaults-constructor hierarchy to declare its own marker-forwarding constructor. A primary-constructor owner cannot use this path because secondary construction executes the primary path; `BESG004` requires migration to an ordinary constructor or a manual `Resource`/`ToResource` implementation. The automatic marker path and marked defaults constructor both create a short-lived defaults owner with no cleanup lifetime. `BESG008` rejects mechanically visible ownership in automatic-path instance initializers: disposable, Resource, subscription, custom property implementation, populated/custom collection, or other owned state moves into every ordinary constructor, while recognized engine property storage and provably empty exact BCL `List<T>`/array containers remain allowed. Opaque factory arguments in a marked constructor remain an author no-throw/no-owned-state responsibility. Suppressing generation and implementing the complete contract remains the manual alternative.

BREAKING CHANGE: Generated abstract `Resource` types no longer expose a protected parameterless constructor. A hand-written or generation-suppressed attached resource must explicitly chain to `base(skipDefaultInitialization: true)` before its first `Update`; a hand-written detached resource that promises declared-default parity must chain to `base(defaultValues)` with a correctly constructed owner. This replaces the old implicit `base()` path that silently left abstract-base properties at `default(T)`. The attached-only `ParticleEmitter.Resource`, `ShakeEffect.Resource`, `DelayAnimationEffect.Resource`, `NodeGraphDrawable.Resource`, `NodeGraphFilterEffect.Resource`, and `RenderNodeDrawable.Resource` parameterless constructors are now internal; callers construct the corresponding owner and call `ToResource(CompositionContext)` instead of exposing a pre-`Update` resource.

BREAKING CHANGE: A generated property backed by an `IProperty<T>` whose `T` is an `EngineObject` type, nullable or non-null, is an owning resource slot. Assigning a different nested resource now disposes the previous value before committing the replacement, assigning the same instance is a no-op, and disposing the containing resource disposes its current value. Every installed child receives one runtime lifetime claim. Installing an already-terminal, borrowed-only, cyclic, pending, or claimed resource—including a second slot on the same owner—is rejected before destructive mutation. Retained external references cannot call public Update/Dispose/generated value/list/ownership seams while claimed. Transfer only through generated `Detach<Property>()`/`Detach<Property>At`, which release the claim without disposal, or `Replace<Property>`/`Replace<Property>At`, which release and return the old value while claiming the replacement. Whole-list operations reserve every incoming claim, wait for preexisting child work outside all registry/resource locks, and atomically commit or roll back. If nested disposal fails, the old child remains claimed for generated cleanup retry and the replacement remains caller-owned. The first containing `Dispose()` attempt makes the owner terminal before callbacks, invokes the most-derived author `DisposeAuthorResources()` entry once, and separately attempts generated child cleanup. The old `protected Dispose(bool)` virtual is removed rather than retaining a dead `disposing == false` branch. Generated parameterless `PreDispose`/`PostDispose` phases capture failures and continue through their base-chain call. A hand-written override must do the same: catch its failure, call the reserved protected `CaptureResourceAuthorCleanupFailure(exception)`, and invoke `base.DisposeAuthorResources()` through finally-equivalent flow. The base orchestrator then preserves one failure or emits one flat aggregate in reached derived-to-base author order; throwing before the base call is nonconforming and cannot be repaired. A later `Dispose()` retries only failed generated child slots and never re-enters reached author cleanup. A generated partial resource migrates custom retryable child cleanup to its generated `partial void DisposeGeneratedResourcesCore()` implementation; it does not hand-write `DisposeGeneratedResources()`. The generated override captures a partial-method failure, then continues through its generated slots and base. A non-generated/manual subclass may override `DisposeGeneratedResources()`, but must capture each custom failure through `CaptureGeneratedResourceCleanupFailure(exception)` and still invoke the base hook. The slot helpers remain available to both forms, while failure aggregation stays base-owned and cannot be rewritten.

BREAKING CHANGE: Generated `Resource` initialization is now transactional. Every attached `ToResource`/manual attached factory must catch its first `Update` failure and route it through `AbortResourceInitialization`, which performs ordinary explicit cleanup and keeps the primary exception first. Detached generated constructors remain unpublished and unavailable to claim/update/dispose/authoring until the runtime-most-derived successful constructor calls generator-reserved `CompleteResourceConstruction(typeof(CurrentResource))`; the transition to Ready is one-way and there is no reopen seam. Each generated lexical level stages current-level state and defaults inside a non-virtual cleanup boundary. A partial `Resource` that acquires disposable or nested-resource state in an instance field/auto-property initializer must move that acquisition to `InitializeResourceConstructionStateCore`, provide partial-initialization-safe `DisposeResourceConstructionStateCore`, and still clean the successful lifetime through `PostDispose` for arbitrary `IDisposable` state or `DisposeGeneratedResourcesCore` for `EngineObject.Resource` state. `BESG007` enforces mechanically visible violations; only provably empty BCL `List<T>`/array initializers are exempt. Other initializers must be no-throw and acquire no owned state. One-shot author cleanup may use `DisposeAuthorOwnedDisposable`, which cycle-safely rejects direct or tuple/array/dictionary/generic/field-backed `EngineObject.Resource` ownership; retryable child ownership uses explicit generated Resource slots/helpers. Cleanup aggregates are recursively flattened in deterministic order, with a construction primary kept first.

BREAKING CHANGE: Generated and hand-written owning resource-list properties change from mutable `List<TResource>` to `IReadOnlyList<TResource>` with ownership-gated mutation methods. Replace `Items.Add(item)` with `AddToItems(item)`, `Items.Remove(item)` with disposing `RemoveFromItems(item)`, and `Items.Clear()` with disposing `ClearItems()`. Replace direct index removal/assignment used for ownership transfer with `DetachItemsAt(index)`/`ReplaceItemsAt(index, replacement)`, which return the old item to the caller without disposal; replace remove/insert reordering with `MoveItems(oldIndex, newIndex)`. Whole-list assignment remains available through the `IReadOnlyList<TResource>` setter, but it snapshots and validates the replacement, disposes the old list first, and commits only when all old cleanup succeeds. The same method-name migration applies to `DrawableGroup.Resource.Children`, `DrawableDecorator.Resource.Children`, `SoundGroup.Resource.Children`, and `Scene3D.Resource.Lights`/`Objects`. Their flow-reconciled prefix is borrowed and cannot be removed/replaced/transferred or moved across the lifetime boundary through the public methods; clear/replacement/terminal cleanup detach that prefix without disposal and clean only the owned tail. Retained list references are read-only and every mutator rejects after disposal starts. `DelayAnimationEffect.Resource.DelayedResources` was an internal cache exposed accidentally, not an authoring collection; its public member is removed. The built-in deferred callback now captures a private retryable cache aggregate whose own unclaimed Resource owner claims delayed children, rather than mutating the parent-claimed Delay Resource or exposing a live cache view.

BREAKING CHANGE: Direct generated `Resource` value/`IsEnabled` setters and every generated or hand-written ownership mutator now advance `Resource.Version` when invoked or when observable ownership changes, including partial successful cleanup before a later failure. This invalidates geometry/render caches without requiring callers to write `Version++` manually. Generated value setter calls are explicit invalidation signals and increment even for an equal value; ownership same-instance assignment, exact same-sequence list assignment, and same-index move remain no-ops. The `Version` setter becomes private; external and derived callers may read the revision but cannot reset/decrement it or bypass a lifetime claim. A derived/manual resource uses gated parameterless `MarkResourceChanged()` for one explicit detached-authoring invalidation. `Resource.Update(...)` becomes non-virtual so terminal/lifetime claims are always checked; migrate an override to protected `UpdateCore(...)` with the same arguments. Generated private `PreUpdate` and `PostUpdate` partial implementations take `ref bool updateOnly`; migrate their signatures and call `MarkResourceChanged(ref updateOnly)` instead of incrementing `Version` independently, so one attached Update coalesces all generated/manual/nested changes. `GraphNode.Resource.ItemValues` and `ItemIndexMap` cease to be public collection surfaces and become protected binding infrastructure with separate internal snapshot access. Unrelated callers can no longer replace/mutate/dispose graph binding entries behind generated fields. `GraphSnapshot` construction/binding/initialization is transactional: it exposes the committed next graph during `Initialize`, rolls back partial work with attempt-all cleanup, calls `Uninitialize` only after initialization began, and retains only failed `EngineObject.Resource` cleanup for retry.

BREAKING CHANGE: Public `ResourceReconciler.Reconcile*` calls now require an active owner scope and reject before mutation when invoked directly. An out-of-tree `Resource.UpdateCore` wraps singular or mixed-list reconciliation in protected `ExecuteOwnedResourceReconciliation(...)` or `ExecuteOwnedResourceListReconciliation(...)`; these scopes establish the exact lifetime-claim authorization, release the owner gate around child work, and coalesce the resulting parent `Version`. The previous null-owner fallback could install, update, or dispose an unclaimed child and is removed without a compatibility path.

BREAKING CHANGE: Code that constructs a replacement child on behalf of an owning Resource uses protected `SetCreatedOwnedResource(ref slot, replacement)` rather than the caller-owned `SetOwnedResource`. It claims and commits with the same rules, but if claim preparation or previous-child cleanup fails it deterministically disposes the still-uninstalled replacement and preserves the transition primary before recursively flattened cleanup failures. Image, Video, Geometry, Text, and Filter graph-node resources migrate their private child fields to this seam. A normal public/plugin assignment still uses `SetOwnedResource` and keeps the rejected incoming Resource caller-owned.

BREAKING CHANGE: A `Resource` subclass with a custom public lifecycle method must use a non-virtual public wrapper that calls protected `ExecuteResourceOperation(Action)` and move plugin customization to a protected virtual core. An owner calls a synchronous lifecycle method on one claimed child only through protected `ExecuteOwnedResourceCallback(Resource, Action)`, which establishes the exact owner-child authorization without holding registry/resource locks during the callback. Generated NodePort value setters use reserved `SetResourceOperationValue` so an assignment can reuse that exact same-thread lifecycle reservation and advance `Version`; ordinary generated values keep the stricter `SetResourceValue`. During the base-owned author cleanup phase only, the generator's private `Clear<Property>NodePortValueForResourceCleanup` wrapper uses reserved `ClearResourceOperationValueForCleanup` to clear a terminal backing field without invoking its public setter or changing `Version`. `GraphNode.Resource.Initialize`, `BindNodePortValues`, `Update(GraphCompositionContext)`, and `Uninitialize` adopt this template. `GraphSnapshot` claims every node resource through a private Resource owner and performs Bind/Initialize/Update/Uninitialize plus attempt-all teardown under exact authorization. `NodeGraphFilterEffect.Resource.Snapshot` is no longer a public live mutable alias, and `NodeGraphDrawable.Resource.OutputRenderNode` no longer exposes a mutable `List`; unrelated callers cannot mutate graph lifetime or render membership behind the outer Resource's claim and `Version`.

BREAKING CHANGE: A second thread that enters `Update`, `Dispose`, or an ownership/value/list mutation while the same Resource is already inside an ownership transition now fails immediately with `InvalidOperationException`; it is not queued behind the active transition. Waiting there can deadlock when an incoming child's already-started callback reaches the parent while the parent is waiting for that child to become quiescent. No mutation, claim, or `Version` change is committed by the rejected call, and the caller may retry after the active transition finishes.

BREAKING CHANGE: `EngineObject.Resource` finalization no longer invokes author or generated child cleanup. The author virtual is renamed from `Dispose(bool)` to explicit-only `DisposeAuthorResources()` and generated `PreDispose`/`PostDispose` partials lose their `bool disposing` parameter. Migrate overrides/partials to the parameterless forms. An out-of-tree resource that previously released unmanaged state from `Dispose(false)` must move that ownership to `SafeHandle`/another finalizable owner, or declare and implement its own finalizer; explicit `Dispose()` remains the only entry to the resource's one-shot author cleanup and retryable generated cleanup contract. Finalizer-thread exceptions never escape.

BREAKING CHANGE: Static built-in `Brushes.Resource` instances are borrowed-only lifetime nodes. They remain valid render-borrow identities but owning property/list installation, direct disposal, Update, and generated authoring mutation now throw. Create an independent brush resource when ownership or detached authoring is required; do not transfer or dispose the global singleton. A plugin derived Resource can mark its own inactive, unowned, childless static/shared instance through protected `MarkResourceBorrowedOnly()` in its construction path before publication; the transition is one-way and later/repeated calls reject.

BREAKING CHANGE: `FilterEffectActivator` has one public constructor that requires explicit `RenderIntent` and `RenderRequestPurpose` arguments before its optional scale arguments. Direct hosts must classify preview/delivery behavior and frame/cache-warmup/auxiliary purpose explicitly; the former scale-only constructor and implicit auxiliary purpose are removed.

BREAKING CHANGE: Opaque, Geometry, target-scope, target-command, and painted callbacks no longer address declared resources by integer position. Description `resources` arguments and `Resources` properties now use `RenderResourceBinding`; create one only with `resource.Bind("stable-name")` and replace `UseDeclaredResource<T>(index, use)` with `UseDeclaredResource<T>("stable-name", use)`. The binding constructor is internal. Binding names are unique ordinal strings and participate in structural/output-cache identity.

BREAKING CHANGE: State-passing callback factories reject mutable reference holders and other states that are not deeply immutable snapshots. Copy every fixed-shape pixel-affecting value and version into an immutable value tuple/record before `Create`/`PaintedSource`; copy variable-length state with `RenderStateSequence.CopyOf`, which validates every element and isolates later source mutation. Raw arrays, `ImmutableArray<T>` internals, and other collection implementations are not accepted as reusable state. Migrate to `CreateRequestLocal`/`PaintedSourceRequestLocal` when no complete reusable snapshot exists. Request-local callbacks may capture and intentionally disable cross-request output-cache reuse. The public `RenderRuntimeIdentity` type and description `RuntimeIdentity` properties are removed: reusable callback identity is derived only from the complete immutable state snapshot, while request-local forms are the explicit no-reuse mode.

BREAKING CHANGE: Custom metadata factories no longer accept capturing delegates. `RenderBoundsContract.Create`/`CreateFullInput`, `OpaqueRenderBoundsContract.Combine`/`FullInputs`, `RenderHitTestContract.Custom`, `RenderScaleContract.Custom`/`MapInputSupply`, and `TargetCaptureScaleContract.Custom` now use `Factory<TState>(TState state, Func<TState, ...> staticCallback, ...)`. Snapshot every callback-read value into one deeply immutable state, use `RenderStateSequence.CopyOf` for variable-length values, and make the callback non-capturing. Forward/backward pairs share that exact state, the engine derives runtime metadata/cache identity from its complete field graph, and callback method plus optional structural key remain structural identity. Explicit structural keys are now deeply validated and wrapped in engine-owned complete-field equality/hashing; replace a mutable/open key or one that depends on custom incomplete equality with an immutable record/tuple/sequence containing every structural field. Only runtime metadata-backed `MethodInfo` values are reusable: custom subclasses and `DynamicMethod` callbacks/keys are rejected, and closed runtime methods use engine-owned MVID/token/declaring-type/generic-argument identity. There is no mutable or request-local metadata-callback form.

BREAKING CHANGE: Explicit render-resource `cacheKey` values now follow the same deep immutable-shape validation and engine-owned complete-field equality as structural keys. Public `RenderResourceIdentity.Key` still returns the authored immutable key, but request-family duplicate/coalescing checks, Shader resource runtime identity, and output-cache keys no longer invoke author `Equals`/`GetHashCode`. Replace mutable/open keys with an immutable record/tuple/`RenderStateSequence` containing every content-identity field; two keys whose custom equality omits a field no longer alias one resource or cached output.

BREAKING CHANGE: `TargetScopeDescription` now declares `DeviceGridSensitivity`, and both `Create` factories insert `deviceGridSensitivity` before `deviceGridMapping`. Existing positional callers must pass an explicit sensitivity or name the `deviceGridMapping` argument. The default changes target-scope cache reuse from the previous effectively insensitive behavior to conservative `PhaseDependent`; only scopes whose pixels are proven independent of device-grid phase opt into `Insensitive`.

BREAKING CHANGE: `RenderNodeRenderRequest` now carries a non-null `AllocationBudget`, and `IRenderTargetFactory.GetMaximumDimension(RenderTargetAllocationDescriptor)` reports the active backend/device texture-axis ceiling for each exact allocation descriptor. Custom factories must answer that side-effect-free query consistently with `Create`; requests share their live byte/target ledger with nested requests and fail/degrade through `RenderIntent` when the budget is exceeded.
```

No `[Obsolete]` shim, returning overload, `V2` type, or executable compatibility wrapper remains after the same change.

## Removed executable surface

The following public model is removed:

- `RenderNodeOperation[] RenderNode.Process(RenderNodeContext)`;
- `RenderNode.PrepareForProcess(ImmediateCanvas)`; migrate its backdrop/current-target preparation to an ordered `TargetCapture`, `TargetCommand`, guarded opaque fragment, or backend boundary recorded from `Process` without touching a live canvas;
- public subclassing of `RenderNodeOperation`;
- `RenderNodeOperation : IDisposable`;
- `RenderNodeOperation.Render(ImmediateCanvas)`;
- public operation factories such as `CreateLambda`, `CreateDecorator`, `CreateFromRenderTarget`, and `CreateFromSurface`;
- public construction/subclassing of `RenderNodeContext`; contexts are sealed engine-created transactions;
- mutable `RenderNodeContext.Input` array, `CalculateBounds()` name, and `IsRenderCacheEnabled` setter; replacements are read-only `Inputs`, availability-checked `TryCalculateInputBounds(out Rect)`, and `DisableRenderCache()`;
- direct recording-time `RenderFragmentHandle.Bounds`, `EffectiveScale`, and `HitTest(Point)` access; replacements are `TryGetMetadata(out RenderFragmentMetadata)` and `TryHitTest(Point, out bool)` because owning-target-dependent metadata may remain symbolic while `Process` records;
- static scale helpers on `RenderNodeContext`; `MaxBufferDimension`, `SanitizeMaxWorkingScale`, `ResolveWorkingScale`, and `ClampWorkingScaleToBufferBudget` move to the independent `RenderScaleUtilities` type;
- public `RenderNodeProcessor`, including `Pull`/`PullToRoot` operation arrays and the protected `CreateRenderTarget` override seam; it is replaced by `RenderNodeRenderer` plus injected `IRenderTargetFactory`;
- public `OperationWrapperRenderNode`/`SetOperations(RenderNodeOperation[])` retention across recording/request boundaries;
- `EffectTarget(RenderNodeOperation)` and `EffectTarget.NodeOperation`; `EffectTarget()` and `EffectTarget(RenderTarget, Rect, EffectiveScale)` remain public for source-less and caller-materialized legacy effects. `EffectTarget` no longer renders or disposes an operation handle.

The replacement is `void Process`, `RenderNodeContext.Inputs`, availability-checked recording metadata, explicit fragment/value/target-scope recording, unified ordered publication, monotonic `DisableRenderCache`, nested recording, and high-level render/single-result-rasterize/measure/hit-test entry points. `RenderNodeRasterization` owns the one optional bitmap together with its logical bounds and output density, so shifted and empty output domains are not lost.

## Migration rules

### Pass-through node

Before:

```csharp
public override RenderNodeOperation[] Process(RenderNodeContext context)
{
    return context.Input;
}
```

After:

```csharp
public override void Process(RenderNodeContext context)
{
    context.PassThrough();
}
```

`PassThrough` publishes all borrowed input streams in order. It does not transfer disposal ownership.

### Recording-time metadata

Bounds, supply, and CPU hit testing are no longer unconditional handle properties:

```csharp
public override void Process(RenderNodeContext context)
{
    bool hasInputBounds = context.TryCalculateInputBounds(out Rect inputBounds);
    var outputs = new List<RenderFragmentHandle>(context.Inputs.Count);

    foreach (RenderFragmentHandle input in context.Inputs)
    {
        bool hasMetadata = input.TryGetMetadata(out RenderFragmentMetadata metadata);
        bool hasHitTest = input.TryHitTest(_point, out bool containsPoint);
        outputs.Add(RecordUsingDeclarativeContracts(
            input,
            hasInputBounds ? inputBounds : null,
            hasMetadata ? metadata : null,
            hasHitTest ? containsPoint : null));
    }

    context.PublishRange(outputs);
}
```

`RecordUsingDeclarativeContracts` in this migration sketch records the operation with bounds, scale, and hit-test contracts that can be reevaluated after owning-target resolution; nullable observations are optional author-time facts, not permission to discard an input. This is the same shape used by `FilterEffectRenderNode`: it records isolation and effect descriptions even when public input metadata is symbolic, and the forward analysis resolves them later. It passes through only when the effect is disabled, authors no operations, or an explicitly finite isolation domain is empty. Do not convert an unavailable aggregate into `PassThrough`, and do not `continue` past one input whose metadata is unavailable.

`TryGetMetadata` returns `Bounds` and `EffectiveScale` together only when both are concrete. `TryGetMetadata`, `TryHitTest`, and `TryCalculateInputBounds` return `false` with default out values for an `OwningTargetDomain` fragment and every ordinary descendant, including handles returned through nested recording; internal finite hints are not public metadata. `ValueCardinality`, `ContributesValuesToTarget`, and `CanBeUsedAsValueInput` remain directly readable. `TryCalculateInputBounds` succeeds for an empty input list with `default(Rect)`.

When a downstream author genuinely needs one reusable value with concrete conservative metadata, wrap the symbolic sequence in `Layer(inputs, finiteNonEmptyDomain)`. A finite Layer always publishes `EffectiveScale.Unbounded`, and lowering selects its materialization density from downstream demand, child supplies, `OutputScale`, and `MaxWorkingScale`. If any input is symbolic, it reports the complete domain as bounds and domain containment for hit testing; it still preserves its internal symbolic dependencies for final graph-wide resolution and fan-out analysis. With only concrete inputs it retains the normal tight child-derived bounds and hit test (`RenderNodeContext.Layer`, `RenderScaleUtilities.ResolveWorkingScale`).

### Intentional no-output node

Before:

```csharp
public override RenderNodeOperation[] Process(RenderNodeContext context)
{
    return [];
}
```

After:

```csharp
public override void Process(RenderNodeContext context)
{
    // Publishing nothing is the explicit zero-output result.
}
```

There is never implicit pass-through when no output is published.

### Semantic one-to-one map

Before:

```csharp
public override RenderNodeOperation[] Process(RenderNodeContext context)
{
    return context.Input
        .Select(input => RenderNodeOperation.CreateDecorator(
            input,
            canvas =>
            {
                using (canvas.PushOpacity(_opacity))
                    input.Render(canvas);
            }))
        .ToArray();
}
```

After:

```csharp
public override void Process(RenderNodeContext context)
{
    foreach (RenderFragmentHandle input in context.Inputs)
    {
        context.Publish(context.Opacity(input, _opacity));
    }
}
```

Use a named semantic method only when the engine owns and tests its equivalence rule. An arbitrary drawing callback uses `OpaqueMap` and remains a fusion boundary.

### Opaque map/decorator

Before, a node returned a lambda/decorator that owned and rendered its child. After, it records an execution-time callback and explicit topology/metadata:

```csharp
private OpaqueRenderDescription CreateDescription()
{
    return OpaqueRenderDescription.Create(
        state: typeof(MyDecoratorNode),
        execute: static (session, _) =>
        {
            using var output = session.CreateOutput(session.OutputBounds);
            output.Canvas.Use(canvas => session.Inputs[0].Draw(canvas));
            session.Publish(output);
        },
        bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
        hitTest: RenderHitTestContract.AnyInput,
        valueCardinality: RenderValueCardinality.Single,
        scale: RenderScaleContract.PreserveInputSupply,
        structuralKey: typeof(MyDecoratorNode));
}

public override void Process(RenderNodeContext context)
{
    foreach (RenderFragmentHandle input in context.Inputs)
    {
        if (!input.CanBeUsedAsValueInput)
            throw new InvalidOperationException("MyDecoratorNode requires value inputs.");

        context.Publish(context.OpaqueMap(input, CreateDescription()));
    }
}
```

The important migration points are deferred execution, declared topology/bounds/cardinality/scale, request-owned output acquisition, and explicit output publication.

### Many-to-one combine

```csharp
public override void Process(RenderNodeContext context)
{
    if (context.Inputs.Count == 0)
        return;
    if (context.Inputs.Any(input => !input.CanBeUsedAsValueInput))
        throw new InvalidOperationException("MyCombineNode requires value inputs.");

    RenderFragmentHandle combined = context.OpaqueCombine(
        context.Inputs,
        CreateLayerDescription());
    context.Publish(combined);
}
```

Each input must have `CanBeUsedAsValueInput == true`; a mixed painter stream must be intentionally wrapped in `Layer` instead of silently dropping its effects. Value streams are flattened in authored order by the combine topology. The description declares aggregate bounds, value cardinality, scale behavior, hit testing, and any target/readback dependency.

### Runtime N-to-M expansion

```csharp
private readonly OpaqueRenderBoundsContract _operationBoundsContract =
    OpaqueRenderBoundsContract.FullInputs(
        state: 0,
        transformBounds: static (_, inputBounds) => CalculateExpandedBounds(inputBounds));

public override void Process(RenderNodeContext context)
{
    if (context.Inputs.Any(input => !input.CanBeUsedAsValueInput))
        throw new InvalidOperationException("MyExpansionNode requires value inputs.");

    RenderFragmentHandle outputs = context.OpaqueExpand(
        context.Inputs,
        OpaqueRenderDescription.Create(
            state: (Count, Seed),
            execute: static (session, state) =>
                ExpandAtExecution(session, state.Count, state.Seed),
            bounds: _operationBoundsContract,
            hitTest: RenderHitTestContract.OutputBounds,
            valueCardinality: RenderValueCardinality.Dynamic,
            scale: RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: typeof(MyExpansionNode)));

    context.Publish(outputs);
}
```

One handle represents the ordered runtime stream. The execution callback's actual count and aggregate bounds must satisfy its declaration. Empty output is cardinality zero at runtime, not implicit identity.

### Source node

```csharp
public override void Process(RenderNodeContext context)
{
    context.Publish(context.OpaqueSource(CreateDeferredSourceDescription()));
}
```

Media reads, decoding, native resource creation, and drawing move into the deferred callback. `Process` may record immutable source/version metadata only.

### Materialized input

Before, callers commonly returned `CreateFromRenderTarget` and transferred disposal through a lambda. A repeatable node now records an explicit external borrow without touching the target during metadata-only requests:

```csharp
public override void Process(RenderNodeContext context)
{
    RenderResource<RenderTarget> borrowed = context.Borrow(
        _target,
        cacheKey: _targetIdentity,
        version: _contentVersion);
    var description = MaterializedInputDescription.FromRenderTarget(
        borrowed,
        _bounds,
        _effectiveScale,
        _deviceBounds,
        _deviceGridOffset,
        RenderHitTestContract.OutputBounds);
    context.Publish(context.MaterializedInput(description));
}
```

`_deviceBounds` and `_deviceGridOffset` are the borrowed target's exact physical footprint and composition-grid phase; they must not be re-derived from `_bounds`. `Borrow` leaves disposal with the node/producer, requires a stable identity/version, and requires the target to remain alive and unmodified through each executing request. A genuinely one-shot producer instead calls `context.Own(detachedTarget, cacheKey, version)`; that request disposes the raw value on rollback/teardown, so it must not be used for a repeatable node that will also service `Measure` or `HitTest`. In-tree cache/3D/decoder sources may use internal leases with the same explicit lifetime model. Raw targets are never wrapped with ambiguous ownership.

### Target command, capture, and scope

Clear, guarded target drawing, backdrop, and readback are returned fragments rather than a global side list:

```csharp
public override void Process(RenderNodeContext context)
{
    if (context.Inputs.Any(input => !input.CanBeUsedAsValueInput))
        throw new InvalidOperationException("BackdropCommandNode requires value inputs.");

    RenderFragmentHandle command = context.TargetCommand(
        context.Inputs,
        TargetCommandDescription.Create(
            state: _contentVersion,
            execute: static (session, _) => session.Canvas.Use(canvas =>
            {
                foreach (RenderExecutionInput input in session.Inputs)
                    input.Draw(canvas);
            }),
            affectedRegion: TargetRegion.Region(_bounds),
            queryBounds: _bounds,
            hitTest: RenderHitTestContract.OutputBounds,
            access: TargetAccess.ReadWrite,
            inputReadbacks: null,
            structuralKey: typeof(BackdropCommandNode)));

    context.Publish(command);
}
```

Public access is `ReadWrite` or `Readback`; only an engine-enforced clear/source-replace primitive may use an internal write-only classification. `TargetRegion.Full`, `Empty`, and finite `Region` describe access, while `QueryBounds`/hit testing independently describe visible query contribution. A command remains ordered even when both are empty. Target `Readback` snapshots the immutable pre-command target exactly once. The former all-or-none `requiresInputReadback` flag is replaced by `inputReadbacks`, whose per-authored-input `None`, `All`, or finite local-value selection enables `UseSnapshot` without depending on unstable flattened runtime positions.

A target-to-value read is explicit and non-contributing until its later draw:

```csharp
RenderFragmentHandle capture = context.TargetCapture(
    TargetCaptureDescription.Create(
        TargetRegion.Region(_bounds),
        _bounds,
        RenderHitTestContract.None,
        TargetCaptureScaleContract.PreserveTargetSupply));

context.Publish(capture); // Orders the read, but does not redraw it.
RenderFragmentHandle filtered = context.Shader(capture, _shader);
context.Publish(context.ContributeValues(filtered));
```

Choose the target-specific scale contract from the intended semantics. `TargetCaptureScaleContract.MaterializeAtWorkingScale` and `Custom` are explicit resampling boundaries: they derive a concrete density from request `OutputScale`, `MaxWorkingScale`, capture bounds, and the buffer clamp without receiving the enclosing target density. `PreserveTargetSupply` remains late-bound and materializes at the resolved density of the enclosing root, finite Layer, or `TargetLayerScope`, so backdrop-style plugin nodes do not downsample before a Shader or replay. The built-in backdrop uses this same public mode.

Use `TargetScope(input, description)` for exactly one same-target replay surrounded only by allocation-free transform/clip state. Opacity, Blend, and brush-backed OpacityMask are typed scope operations. Group isolation that remains an ordered current-target effect uses the normal bottom-up typed scope:

`TargetScopeDescription.Create` and `CreateRequestLocal` now take `deviceGridSensitivity` before `deviceGridMapping`. A positional caller that previously supplied only `deviceGridMapping` passes an explicit sensitivity first or names `deviceGridMapping`. The default is conservative `PhaseDependent`, replacing the old effectively insensitive cache behavior; select `Insensitive` only after proving that subpixel device-grid phase cannot change the scope's pixels.

```csharp
public override void Process(RenderNodeContext context)
{
    RenderFragmentHandle isolated = context.TargetLayerScope(
        context.Inputs,
        TargetRegion.Full);
    context.Publish(isolated);
}
```

`TargetLayerScope` preserves the supplied streams' aggregate value cardinality for dependency accounting but has `ContributesValuesToTarget == false` and `CanBeUsedAsValueInput == false`. Full stays symbolic while later Transform/Clip/Layer parents are recorded and resolves against the actual current target during final scope-token lowering. A non-empty resolved scope uses a transparent offscreen isolation target and one composite unless the planner proves direct replay equivalent; overlapping translucent children make unconditional elision incorrect. `Empty` preserves authored order without allocating a target or executing pixel work. Existing `GraphicsContext2D.PushLayer(default)`/`LayerRenderNode(default)` migrates directly to this method from its ordinary bottom-up `Process`; there is no pre-order traversal exception.

Use finite `Layer(inputs, finiteNonEmptyDomain)` to replay an arbitrary mixed sequence as exactly one materializable value. This is required before Shader, Geometry, or another public value consumer when `CanBeUsedAsValueInput` is false and that localization is the intended semantics. The value constructor deliberately does not accept Full because it needs a finite conservative recording-time metadata boundary. It always publishes `EffectiveScale.Unbounded`; lowering selects its materialization density from downstream demand, child supplies, `OutputScale`, and `MaxWorkingScale`. With symbolic inputs it reports the complete domain/domain hit test while retaining the symbolic internal edge; with concrete inputs it reports tight child-derived bounds/hit testing. A non-default finite `LayerRenderNode` limit records this value form (`RenderNodeContext.Layer`, `RenderScaleUtilities.ResolveWorkingScale`).

### Raw canvas migration

A decorator whose only behavior is `PushLayer` plus one replay now uses typed `TargetLayerScope` as shown above, not a raw callback. An old decorator with additional unguarded canvas behavior that cannot be expressed by typed scopes migrates to `RawTargetScope`, and a zero-input/current-target `CreateLambda` migrates to `RawTargetCommand`:

```csharp
public override void Process(RenderNodeContext context)
{
    foreach (RenderFragmentHandle input in context.Inputs)
    {
        context.Publish(context.RawTargetScope(
            input,
            RawTargetScopeDescription.CreateRequestLocal(
                execute: session =>
                {
                    DrawLegacyPrefix(session.Canvas);
                    session.ReplayInput();
                    DrawLegacySuffix(session.Canvas);
                },
                bounds: RenderBoundsContract.Identity,
                hitTest: RenderHitTestContract.AnyInput,
                scale: RenderScaleContract.PreserveInputSupply,
                structuralKey: typeof(LegacyDecoratorNode))));
    }
}
```

```csharp
context.Publish(context.RawTargetCommand(
    RawTargetCommandDescription.CreateRequestLocal(
        execute: session => DrawLegacy(session.Canvas),
        queryBounds: _bounds,
        hitTest: RenderHitTestContract.OutputBounds,
        structuralKey: typeof(LegacyPainterNode))));
```

Both raw forms conservatively read/write the full current target, are `LegacyRawCanvas` fusion/cache boundaries, and make exact whole-request physical-pass/synchronization claims unavailable. When the zero-input callback is actually an independent value source, migrate it to guarded `OpaqueSource`; when a raw painter result must become a reusable value, wrap its published command in an explicit finite Layer.

### Nested recording

Before:

```csharp
var processor = new RenderNodeProcessor(_child, useRenderCache: true);
return processor.PullToRoot();
```

After:

```csharp
public override void Process(RenderNodeContext context)
{
    context.PublishRange(context.RecordSubtree(_child));
}
```

For a wrapper that supplies explicit inputs:

```csharp
context.PublishRange(context.RecordNode(_child, context.Inputs));
```

Both forms remain in the current request. A returned handle preserves the child's metadata-availability state: an owning-target dependency and every ordinary descendant still return false from `TryGetMetadata`/`TryHitTest` after remapping to the parent. Separate-target nested rendering is recorded as an internal nested request before execution, not started from a render callback.

### EffectTarget and NodeGraph operation wrappers

`EffectTarget` itself remains because existing `FilterEffectContext.CustomEffect` callbacks use materialized targets. The engine now invokes those callbacks only at execution with render-target-backed `EffectTarget` instances. The public operation-backed constructor/property are removed; `Draw` and `Dispose` act only on the materialized target. Code that previously inspected `NodeOperation` migrates to Shader/Geometry/opaque recording before execution or consumes the execution-time `RenderTarget` supplied by the legacy custom-effect context.

Materialized targets now expose immutable `DeviceBounds` and derived `RasterBounds`, but the existing custom-effect allocation contract does not change. `CustomFilterEffectContext.DeviceBufferSize(bounds, w)` still sizes a local buffer from the logical dimensions alone (`(int)` at `w == 1`, otherwise `ceil(dimension * w)`), so a fractional logical origin does not add a pixel. `DeviceBufferBounds(bounds, w)` remains available as canonical composition-device metadata; it is not the source of truth for the legacy local-buffer size. Immediately before a legacy Custom callback, a forced compatibility Flush removes renderer-owned aprons. Targets created by the callback retain their local raster phase and are replayed directly at their authored logical position instead of being normalized through a canonical intermediate. New `Shader` and `Geometry` descriptions use the separate canonical typed path.

`OperationWrapperRenderNode.SetOperations` cannot retain transaction handles and is removed with the wrapper's public executable role. NodeGraph input nodes receive fresh request-local facade handles through `RecordNode` binding and publish only while that nested transaction is active. A downstream custom wrapper follows the same pattern instead of storing handles in fields.

### Cache disablement

Before:

```csharp
context.IsRenderCacheEnabled = false;
```

After:

```csharp
context.DisableRenderCache();
```

Disablement is monotonic and participates in the node transaction. An exception rolls it back with the rest of that node's partial recording.

### Scale utilities

Pure feature-003 density calculations no longer hang off the transaction-scoped recorder:

```csharp
float workingScale = RenderScaleUtilities.ResolveWorkingScale(
    inputScales,
    outputScale,
    maxWorkingScale);

workingScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
    completeOutputBounds,
    workingScale);
```

All callers—including 3D, brushes, export policy, custom nodes, and planner code—migrate in the same change. `RenderBoundsContract` likewise lives in `Beutl.Graphics.Rendering` because it is shared by Shader, Geometry, target scopes, and render-node descriptions. There are no forwarding members on `RenderNodeContext` and no duplicate Effects-only bounds type.

`RenderScaleContract.PreserveInputSupply` is valid only where topology supplies one unambiguous source density per surviving output: an element-wise `OpaqueMap` (including zero-or-one discard) or per-fragment `TargetScope`/`RawTargetScope`. Source, capture, combine, and expansion descriptions must choose another valid scale contract; combine/expansion cannot silently preserve a conveniently selected input. `TargetLayerScope` has no author scale argument and uses `EffectiveScale.Unbounded` only as an internal vector-supply hint while symbolic metadata remains unavailable. Validation occurs when the description is attached to its context method.

Use `RenderScaleContract.MapInputSupply<TState>(TState state, Func<TState, EffectiveScale, EffectiveScale> map, object? structuralKey)` for a pure density transform over an element-wise one-input map. Unlike `Custom`, it receives exactly the corresponding input's resolved supply and may return `EffectiveScale.Unbounded`. The non-capturing callback and optional immutable key identify the mapping shape; the complete deeply immutable state is runtime metadata/cache identity. Transform and DrawableGroup use this contract, so a symbolic upstream supply is mapped again with the same stored snapshot after graph-wide resolution rather than freezing a provisional recording value. It is rejected for source, capture, combine, and expansion topologies.

### Custom working-scale render node

`FilterEffect.Resource.CreateRenderNode()` remains. A custom `FilterEffectRenderNode` that only changes working-scale semantics overrides the protected `GetWorkingScaleContract()` hook and retains the base `Process` lowering. Returning `null` selects the standard supply-driven `MaterializeAtWorkingScale` contract: each branch resolves `w = min(max(s_out, densest concrete supply), MaxWorkingScale)` before the 16384-axis buffer clamp. `s_out` is the pre-ceiling floor that concrete supply can raise, while a lower positive `MaxWorkingScale` is authoritative and may reduce the final density below `s_out`. Only an explicit non-standard contract (`PreserveInputSupply`, `MapInputSupply`, `Custom`) escapes that standard calculation, and the multi-branch fold adds no second floor on top of the mapped branch results. An explicit `Custom` result may intentionally choose another positive finite density. After the base identifies finite or owner-relative isolation for mixed/value-ineligible inputs, it folds that standard or custom policy into the first surviving Shader, Geometry, or legacy operation. The callback is evaluated for each surviving branch with exactly one input supply and that branch's isolated effect-input bounds. Legacy multi-input work takes the densest concrete mapped result and falls back to `OutputScale` only when every branch is `Unbounded`. Allocation footprints are independent of callback count: before an opaque Custom callback they retain each branch's local-origin transforms and intermediate Flushes, while the forced compatibility materialization immediately before callback entry removes renderer-owned aprons. The callback then creates dimension-sized local buffers and keeps their raster placement through direct replay. The first Custom callback unions its transformed branch results and collapses later analysis to that aggregate domain because its implementation may combine or split targets. No identity fragment or extra opaque/pass boundary is recorded. A no-item effect publishes the original inputs, commits no provisional isolation, and rolls back untransferred owned resources; its hook/resolver remains lazy unless `ApplyTo` probes the author-time scale. With a concrete single input, `FilterEffectContext.TryGetWorkingScale` returns the nominal effect-input density and `WorkingScale` remains readable; a later expanding operation can still clamp its own buffer. With symbolic or branch-dependent inputs, `TryGetWorkingScale` returns `false` and the getter throws rather than exposing a provisional/aggregate value. Forward analysis reevaluates the pure contract only after the owning scope is resolved. The contract cannot depend on the later ROI.

Custom nodes must not use `OutputScale` as an implicit intermediate ceiling or floor. A non-supply custom scale choice must be declared in its operation's scale contract and bounded by `MaxWorkingScale` plus the per-buffer dimension clamp against complete concrete allocation footprints. A `Custom` resolver must return a finite value greater than zero; a throw, NaN, infinity, zero, or negative value fails rather than falling back to `OutputScale`. With a symbolic dependency, provisional evaluation is not author-readable and the resolver is evaluated again after resolution. Later ROI crops allocation bounds without changing the final valid density. Current-pixel stages separated by a concrete density change now form an explicit `ScaleTransition`; equal-density edges and an `Unbounded` predecessor adopting its successor density remain fusible. Merged binders observe stage-local logical bounds, while all stages use the actual runtime-clamped run density and later stages receive that density as their input effective scale, matching disabled execution.

## FilterEffect compatibility

The authoring entry point and operation-call surface remain:

```csharp
public override void ApplyTo(FilterEffectContext context, Resource resource)
{
    context.Blur(resource.Sigma);
    context.AppendSKColorFilter(...);
    context.CustomEffect(...);
}
```

Existing methods keep their current-main authored ordering. New effects may opt into:

```csharp
context.Shader(shaderDescription);
context.Geometry(geometryDescription);
```

Both methods append in the existing authored order and synchronously update the engine-internal recording bounds before returning. CurrentPixel Shader preserves it; WholeSource Shader and Geometry apply their forward `RenderBoundsContract`. A later operation in the same `ApplyTo` therefore observes the preceding new operation's conservative bounds just as it does for existing bounds-transforming methods. The legacy public `FilterEffectContext.Bounds` property is removed; the engine tracks recording bounds internally and exposes neither them nor a recorded-bounds hint through `ApplyTo` or `RenderFragmentHandle`. When an earlier retained legacy custom item made the recording bounds invalid, the new operation remains in that same opaque sequence and the bounds stay invalid; Shader and Geometry do not split out into planner-visible typed fragments in this case. Scope-domain lowering resolves the symbolic unknown bound to the local owning target domain after enclosing transforms, clips, and target scopes are known, and forward analysis reevaluates retained bounds-transforming items from the resolved input bounds. The final semantic output is cropped to that domain, while internal opaque allocations remain uninspectable. Otherwise, validation/mapping failure leaves the item list and recording bounds unchanged, and a new mapping may not return Invalid. An exception from the surrounding `ApplyTo` invocation rolls its items, recording bounds, owned-resource transfers, and borrows back to the invocation checkpoint. Invalid scale results are failures, never identity/default fallbacks.

Operation-call compatibility does not preserve provisional author-time metadata. The legacy public `FilterEffectContext.Bounds` property is removed (kept as an engine-internal recording tracker), and symbolic or branch-dependent input makes `WorkingScale` unavailable. An effect that derives an operation parameter from unavailable bounds must append deferred pure bounds mapping and an execution factory/callback that bind from the later resolved target bounds. Scale-dependent authoring must call `TryGetWorkingScale` and defer binding when it returns `false`. The engine invokes `ApplyTo` once; it does not replay authoring after resolution. This stricter metadata availability is an intentional break from synchronous author-time inspection, not a replacement lifecycle.

There is no migration to `Describe`, no `EffectGraphBuilder`, and no requirement to convert all built-in effects before the renderer-wide seam is usable.

Authors who return a custom render node from `FilterEffect.Resource.CreateRenderNode()` must migrate that node's `Process` implementation. A working-scale-only customization migrates to `GetWorkingScaleContract()` so it does not duplicate or bypass the base isolation and effect lowering. Effects that directly used `EffectTarget.NodeOperation` or `EffectTarget(RenderNodeOperation)` must also migrate that executable escape; ordinary `FilterEffectContext` operation calls remain available, subject to the intentional author-time metadata availability change above.

## Direct processor consumers

Callers migrate by intent:

| Old use | Replacement |
|---|---|
| `PullToRoot` then render each operation | `RenderNodeRenderer.Render(destination)` |
| `PullToRoot` then union operation `Bounds` for layout/query/selection or hit-test intent | `RenderNodeRenderer.Measure().QueryBounds` |
| `PullToRoot` bounds union used to size/save the subsequent raster result | `RenderNodeRenderer.Measure().OutputBounds` before execution, then the returned `RenderNodeRasterization.Bounds` for the selected actual raster domain |
| actual root write/raster extent (no sound old operation-bounds equivalent) | `RenderNodeRenderer.Measure().OutputBounds` |
| `PullToRoot` then call `HitTest` | `RenderNodeRenderer.HitTest(point)` |
| old `Rasterize` list / `RasterizeAndConcat` | one owned `RenderNodeRasterization` from `RenderNodeRenderer.Rasterize()` |
| retain/wrap one operation in NodeGraph | request-scoped `RecordNode` input binding |
| independent pull to fill render cache | selected capture point in current request |

All in-tree consumers migrate in the same change. No code outside the recorder/executor may enumerate executable operations because no such public object remains.

Golden-image harnesses and save/export paths that previously unioned operation bounds and replayed a list into one target do not reproduce that loop. They call `Measure().OutputBounds` when a preflight size is required, then consume the single owned `RenderNodeRasterization`; its `Bounds` supplies the raster's logical origin/domain and its `Bitmap` is already the complete painter-ordered result. Layout, query, selection, and hit-test callers use `QueryBounds` instead.

A direct consumer constructs the facade with explicit request policy:

```csharp
using var renderer = new RenderNodeRenderer(
    root,
    new RenderNodeRendererOptions
    {
        DefaultRequest = new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = targetDomain,
            OutputScale = outputScale,
            MaxWorkingScale = maxWorkingScale,
            CacheOptions = RenderCacheOptions.Enabled,
            Purpose = RenderRequestPurpose.Frame,
        },
        TargetFactory = targetFactory,
    });

RenderNodeMeasurement measurement = renderer.Measure();
renderer.Render(destination);

using RenderNodeRasterization rasterized = renderer.Rasterize();
if (!rasterized.IsEmpty)
{
    Bitmap bitmap = rasterized.Bitmap!;
    // bitmap pixel (0, 0) represents rasterized.Bounds.Position
    // at rasterized.OutputScale pixels per logical unit.
}
```

`TargetDomain` is needed by target-less `Measure`/`HitTest`/`Rasterize` when the graph publishes Full target access whose enclosing root has no real destination; a query rectangle never substitutes for that target domain. The old union of operation `Bounds` represented value/query metadata and had no separate sound extent for target writes—a Full Clear could write the entire domain while contributing no query bounds. `Measure.OutputBounds` therefore intentionally may differ: it unions contributing value bounds with resolved potentially-writing target-effect regions. `Measure.QueryBounds` remains the independent layout/query/hit-test view. `RequestedRegion = null` selects complete `OutputBounds`; a non-degenerate region is clipped to that output for the final commit, while an explicitly degenerate region preserves its authored empty bounds and origin. It still does not replace the target domain.

For a non-empty selection, `RenderNodeRasterization.Bounds` is the selected rectangle's canonical device-pixel cover converted back to logical coordinates; it is not the unsnapped semantic rectangle. Its position and size therefore replay the owned bitmap without stretching or device-phase shift. A zero-area selection preserves its authored logical bounds and origin as a normal `IsEmpty` result with `Bitmap == null`; a non-empty selection owns a non-null bitmap even if all pixels are transparent. The result, not the renderer or caller separately, owns/disposes that bitmap. A former `RenderNodeProcessor.CreateRenderTarget` override becomes an injected `IRenderTargetFactory`; the renderer pool invokes `Create(RenderTargetAllocationDescriptor)` only on a compatible-pool miss and owns every accepted target until eviction or renderer disposal. The descriptor carries exact device size, the fixed linear-premultiplied RGBA16F format, and the request's backend/device context when bound. A null factory selects the built-in allocator. The renderer borrows `root`, `targetFactory`, the descriptor's callback-scoped graphics context, and `destination` (`src/Beutl.Engine/Graphics/Rendering/RenderNodeRenderer.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderTargetPool.cs`). Request diagnostics remain an internal implementation/evidence seam rather than a public renderer option.

Standalone `RenderNodeRenderer.Render`/`Rasterize` requests preserve `RenderNodeRenderRequest.Purpose`, which defaults to `Auxiliary`; direct frame hosts select `Frame` and warm-up hosts select `CacheWarmup` through that public descriptor. Pixel-executing calls reject metadata-only `Bounds`/`HitTest`. The production `Renderer` sets `Frame` on its default request. `Measure` is always `Bounds` and `HitTest` is always `HitTest`. There is no public list-returning rasterizer because an effectful fragment stream has one painter-ordered `RenderNodeRasterization` result.

## Resource-side authoring dispatch

`Geometry.ApplyTo`, `PathSegment.ApplyTo`, `PathFigure.ApplyTo`, `PathGeometry.HitTestFigure`, and
`Mesh.ApplyTo` move from the engine object to its `Resource`. Their engine-object forms are removed; there is
no forwarding overload.

```csharp
// before
public override void ApplyTo(IGeometryContext context, Geometry.Resource resource)
{
    var r = (Resource)resource;
    context.MoveTo(new Point(r.Width, 0));
}

// after
public partial class Resource
{
    public override void ApplyTo(IGeometryContext context)
    {
        context.MoveTo(new Point(Width, 0));
    }
}
```

An override that read only resource values migrates by moving the body into the generated `Resource` partial,
dropping the `resource` parameter and the `var r = (Resource)resource;` cast, and reading the members directly
— that covers every in-tree override except `SKPathGeometry`, whose `ApplyTo` read `_path`, a field of the
engine object; its owned `SKPath` moves onto the resource in the same change. An override that reaches for
engine-object state the resource does not carry has to move that state across too.
`PathGeometry.HitTestFigure(point, pen, resource)` becomes `PathGeometry.Resource.HitTestFigure(point, pen)`;
`Mesh.ApplyTo(resource, out vertices, out indices)` becomes `Mesh.Resource.ApplyTo(out vertices, out indices)`.

The motivation is that a `Resource` built through its public parameterless constructor has no backing engine
object, so dispatching through `GetOriginal()` made every public member of `Geometry.Resource` and
`Mesh.Resource` throw `NullReferenceException` for a shape the public constructors accept. Those members are
non-virtual on a public subclassable type, so an out-of-tree author had no workaround. Dispatching on the
resource removes the dereference: a hand-built resource produces the same path or mesh as its attached
counterpart once it carries the same property values.

That last qualifier is now supplied by the generator. The public generated `Resource()` initializes each
generated value property from its declared `IProperty.DefaultValue` and materializes non-null EngineObject-valued
defaults as owned nested resources. It obtains them from one initializer-only temporary owner; the attached
`ToResource` fast path does not construct that owner or evaluate defaults that its first update would overwrite.
On the automatic path, `BESG003` rejects generated properties that are unavailable from declaration-time state
or replaced by an ordinary constructor. The accepted computed-getter form directly returns a declaration-initialized
readonly instance field; arbitrary getter logic is not evaluated as storage. `BESG004` rejects a primary
constructor on that path. An ordinary-constructor owner can instead declare exactly one defaults-only instance
constructor marked `[ResourceDefaultValuesConstructor]`. It takes one `ResourceDefaultValuesConstruction`, forwards
that exact marker through the base-constructor chain, and initializes only stable generated property storage from
recognized non-disposable `Property` factories. `BESG005` rejects an invalid or ambiguous defaults constructor,
and `BESG006` requires a generated derived type to declare its own marker-forwarding constructor whenever a base
owner uses one. The most-derived defaults constructor runs once for a direct concrete `Resource()`; abstract owners
may expose a protected marker constructor for the derived chain but are never instantiated as defaults sources.
This constructs the complete ownership-free defaults source without forcing the author to replace the generated
`Resource`/`ToResource` contract.

Generated abstract `Resource` types no longer provide a protected parameterless constructor. A hand-written or
generation-suppressed attached resource explicitly calls `base(skipDefaultInitialization: true)`, while a
hand-written detached resource that promises declared-default parity explicitly calls `base(defaultValues)`.
The missing decision is therefore a compile error rather than a base resource whose properties silently remain
at `default(T)`. See `docs/specs/004-gpu-pass-fusion/contracts/public-api.md`.

The six hand-written attached-only resources for `ParticleEmitter`, `ShakeEffect`, `DelayAnimationEffect`,
`NodeGraphDrawable`, `NodeGraphFilterEffect`, and `RenderNodeDrawable` no longer expose their implicit public
parameterless constructors. Construct the owner and call `ToResource(CompositionContext)`; those resources have
read-only evaluated state and are not detached authoring surfaces.

Generated EngineObject-valued resource properties are owning slots. Replacing one with a different resource
immediately disposes the previous value; assigning the same instance does nothing, and disposing the containing
resource disposes the currently held value. Code that retained or shared the old value must transfer ownership
explicitly instead.

`Geometry.Resource.GetCachedPath` additionally commits its version guard and cached context only after
`ApplyTo` returns. A throwing author no longer installs a partially recorded path that later calls serve; each
call retries the build and rethrows.

`Geometry.Resource`'s stroke-path cache keys the pen through `EngineResourceIdentity.Of` rather than
`Pen.Resource.GetOriginal()`, which is null for every detached pen and therefore made any two of them compare
equal — the cache served the first pen's stroke for the second.

`EngineObject.Resource` gains `IsAttached` and `RequireOriginal()`. `GetOriginal()` and each generated typed
form now declare their nullable return because a detached resource has no backing object. The dereferences this change migrated to
`RequireOriginal()` — which raises `InvalidOperationException` naming the resource type instead of a
`NullReferenceException` — cover `Drawable.Render` on both the immediate and the recording
canvas, `MeasureInternal`, `GetTransformMatrix`, `ZIndex`, the generated `BindNodePortValues`, the hand-written
`Beutl.NodeGraph` resource overrides beside it, and `AvaloniaTypeConverter`'s drawable-brush render.

This document does not claim that list is complete, because a prose list of this kind already failed once: an
earlier draft was written from a `GetOriginal().Member` search and so omitted `GraphicsContext2D.DrawDrawable`,
which spells the same dereference across two statements and still threw. The line is held by
`EngineObjectOriginalAccessCensusTests` instead, which counts call sites syntactically under `src/` and fails
until a new one is accounted for deliberately. The calls that remain are mostly null-safe identity comparisons;
they have not been individually probed for detached reachability, and the census is what forces that question
to be asked when one is added. `EngineResourceIdentity.Of` continues to read `GetOriginal()`
and synthesize an identity when it is null.

## Ownership summary

- Context inputs and fragment handles are borrowed and never disposed by authors.
- `RenderNodeRenderer` owns its persistent plan/program caches, target pool, and accepted factory-created targets, but borrows its root and collaborators.
- Each returned `RenderNodeRasterization` exclusively owns its nullable bitmap until the result is disposed; renderer disposal does not reclaim an already returned result.
- Recorded values are request-owned after transaction commit.
- `Own` transfers disposable ownership once; rollback/teardown disposes it or successful cache publication atomically transfers and discharges it to `RenderNodeCache` ownership.
- `Borrow` releases only its request token; the external owner retains/disposes the raw resource after all executing borrows end.
- Execution sessions borrow inputs/destination/output canvases for callback duration and reject retained use.
- Outputs acquired inside deferred callbacks remain executor-owned until published or discarded.
- Cache capture owns no persistent entry until complete-request success.
- Cleanup continues after individual disposal failures and never replaces the primary render/planning exception.
