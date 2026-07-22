# Breaking Changes and Migration Contract

Feature 004 intentionally replaces the executable render-node pull API. It is breaking for custom `RenderNode`/`RenderNodeOperation` authors and direct `RenderNodeProcessor` consumers in `Beutl.Engine`, `Beutl.Editor`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and downstream plugins. Existing `FilterEffect.ApplyTo` operation calls remain source-compatible unless they directly use the removed operation-backed `EffectTarget` members or subclass/consume the changed render-node API, but synchronous author-time metadata access is intentionally stricter: symbolic `Bounds` is unavailable and symbolic or branch-dependent `WorkingScale` must be probed with `TryGetWorkingScale` and bound later in an execution callback.

The implementation commit carrying this public change must use a breaking Conventional Commit such as:

```text
refactor(engine)!: record complete render requests before execution

BREAKING CHANGE: Beutl.Engine, Beutl.Editor, Beutl.NodeGraph, Beutl.ProjectSystem, Beutl.AgentToolkit, and application render-node consumers now use void Process(RenderNodeContext), context-owned RenderFragmentHandle values, and high-level request entry points. Executable/disposable RenderNodeOperation, RenderNodeProcessor Pull APIs, OperationWrapperRenderNode.SetOperations, and operation-backed EffectTarget members were removed. RenderNodeContext is now an engine-created sealed recorder: Input/CalculateBounds/the cache setter migrate to Inputs/TryCalculateInputBounds/DisableRenderCache, and its static scale helpers move to RenderScaleUtilities. RenderFragmentHandle no longer exposes direct Bounds, EffectiveScale, or HitTest members; authors use TryGetMetadata and TryHitTest and must handle symbolic owning-target dependencies. Rasterize now returns one owned RenderNodeRasterization carrying its logical Bounds, OutputScale, and nullable Bitmap; Measure reports separate OutputBounds and QueryBounds. Existing FilterEffect.ApplyTo operation calls remain available, but symbolic inputs expose Rect.Invalid Bounds and may make WorkingScale unavailable: effect authors must use TryGetWorkingScale and defer bounds/scale-dependent parameters to Shader, Geometry, or CustomEffect execution callbacks. Direct FilterEffectActivator callers must now pass RenderIntent and RenderRequestPurpose explicitly instead of inferring failure policy from MaxWorkingScale. Custom nodes returned by FilterEffect.Resource.CreateRenderNode must migrate.

BREAKING CHANGE: `RenderNodeCacheHelper.MakeCache`, `CreateDefaultCache`, and `CanCacheRecursiveChildrenOnly`, together with `RenderNodeCache.RejectCache` and `IsCacheRejected`, are removed. Cache lookup, miss capture, and atomic publication now occur only inside the complete request after dependency and region analysis; callers render through `RenderNodeRenderer`/the production `Renderer` and use `Invalidate` or `RenderNodeCacheHelper.ClearCache` to discard retained entries.
```

No `[Obsolete]` shim, returning overload, `V2` type, or executable compatibility wrapper remains after the same change.

## Removed executable surface

The following public model is removed:

- `RenderNodeOperation[] RenderNode.Process(RenderNodeContext)`;
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
- `EffectTarget(RenderNodeOperation)` and `EffectTarget.NodeOperation`; `EffectTarget` remains an execution-time materialized-target type and no longer renders or disposes an operation handle.

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
    if (!context.TryCalculateInputBounds(out Rect inputBounds))
    {
        // The inputs still depend on an owning target domain. Record without
        // authoritative bounds, or establish an explicit finite Layer below.
        return;
    }

    foreach (RenderFragmentHandle input in context.Inputs)
    {
        if (!input.TryGetMetadata(out RenderFragmentMetadata metadata))
            continue;

        bool containsPoint = input.TryHitTest(_point, out bool hit) && hit;
        RecordUsingConcreteMetadata(input, inputBounds, metadata, containsPoint);
    }
}
```

`TryGetMetadata` returns `Bounds` and `EffectiveScale` together only when both are concrete. `TryGetMetadata`, `TryHitTest`, and `TryCalculateInputBounds` return `false` with default out values for an `OwningTargetDomain` fragment and every ordinary descendant, including handles returned through nested recording; internal finite hints are not public metadata. `ValueCardinality`, `ContributesValuesToTarget`, and `CanBeUsedAsValueInput` remain directly readable. `TryCalculateInputBounds` succeeds for an empty input list with `default(Rect)`.

When a downstream author genuinely needs one reusable value with concrete conservative metadata, wrap the symbolic sequence in `Layer(inputs, finiteNonEmptyDomain)`. A finite Layer publishes `EffectiveScale.Unbounded`. If any input is symbolic, it reports the complete domain as bounds and domain containment for hit testing; it still preserves its internal symbolic dependencies for final graph-wide resolution and fan-out analysis. With only concrete inputs it retains the normal tight child-derived bounds and hit test.

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
        execute: session =>
        {
            using var output = session.CreateOutput(session.OutputBounds);
            output.Canvas.Use(canvas => session.Inputs[0].Draw(canvas));
            session.Publish(output);
        },
        bounds: RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
        hitTest: RenderHitTestContract.AnyInput,
        valueCardinality: RenderValueCardinality.Single,
        scale: RenderScaleContract.PreserveInputSupply,
        structuralKey: typeof(MyDecoratorNode),
        runtimeIdentity: new RenderRuntimeIdentity(typeof(MyDecoratorNode)));
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
private readonly RenderOperationBoundsContract _operationBoundsContract =
    RenderOperationBoundsContract.FullInputs(CalculateExpandedBounds);

public override void Process(RenderNodeContext context)
{
    if (context.Inputs.Any(input => !input.CanBeUsedAsValueInput))
        throw new InvalidOperationException("MyExpansionNode requires value inputs.");

    RenderFragmentHandle outputs = context.OpaqueExpand(
        context.Inputs,
        OpaqueRenderDescription.Create(
            execute: ExpandAtExecution,
            bounds: _operationBoundsContract,
            hitTest: RenderHitTestContract.OutputBounds,
            valueCardinality: RenderValueCardinality.Dynamic,
            scale: RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: typeof(MyExpansionNode),
            runtimeIdentity: new RenderRuntimeIdentity((Count, Seed))));

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
        RenderHitTestContract.OutputBounds);
    context.Publish(context.MaterializedInput(description));
}
```

`Borrow` leaves disposal with the node/producer, requires a stable identity/version, and requires the target to remain alive and unmodified through each executing request. A genuinely one-shot producer instead calls `context.Own(detachedTarget, cacheKey, version)`; that request disposes the raw value on rollback/teardown, so it must not be used for a repeatable node that will also service `Measure` or `HitTest`. In-tree cache/3D/decoder sources may use internal leases with the same explicit lifetime model. Raw targets are never wrapped with ambiguous ownership.

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
            execute: session => session.Canvas.Use(canvas =>
            {
                foreach (RenderExecutionInput input in session.Inputs)
                    input.Draw(canvas);
            }),
            affectedRegion: TargetRegion.Region(_bounds),
            queryBounds: _bounds,
            hitTest: RenderHitTestContract.OutputBounds,
            access: TargetAccess.ReadWrite,
            requiresInputReadback: false,
            structuralKey: typeof(BackdropCommandNode),
            runtimeIdentity: new RenderRuntimeIdentity(_contentVersion)));

    context.Publish(command);
}
```

Public access is `ReadWrite` or `Readback`; only an engine-enforced clear/source-replace primitive may use an internal write-only classification. `TargetRegion.Full`, `Empty`, and finite `Region` describe access, while `QueryBounds`/hit testing independently describe visible query contribution. A command remains ordered even when both are empty. Target `Readback` snapshots the immutable pre-command target exactly once; `requiresInputReadback` separately enables `UseSnapshot` on materialized value inputs.

A target-to-value read is explicit and non-contributing until its later draw:

```csharp
RenderFragmentHandle capture = context.TargetCapture(
    TargetCaptureDescription.Create(
        TargetRegion.Region(_bounds),
        _bounds,
        RenderHitTestContract.None,
        RenderScaleContract.MaterializeAtWorkingScale));

context.Publish(capture); // Orders the read, but does not redraw it.
RenderFragmentHandle filtered = context.Shader(capture, _shader);
context.Publish(context.ContributeValues(filtered));
```

This public capture is an explicit resampling boundary, not a lossless copy at the enclosing target's density. `MaterializeAtWorkingScale` derives a concrete density from request `OutputScale`, `MaxWorkingScale`, the capture bounds, and the buffer clamp. `Custom` may derive a different concrete density, but its `InputSupplies` list is empty and it may use only `OutputBounds`, `OutputScale`, and `MaxWorkingScale`; it receives no density supply from the enclosing root, finite Layer, or `TargetLayerScope`. Capturing inside a denser scope may therefore intentionally downsample before the Shader runs. The engine-internal backdrop capture may late-bind the resolved scope density; while its owning domain is symbolic, a nested public handle for it returns false from `TryGetMetadata` and `TryHitTest` rather than exposing the internal placeholder.

Use `TargetScope(input, description)` for exactly one same-target replay surrounded only by allocation-free transform/clip state. Opacity, Blend, and brush-backed OpacityMask are typed scope operations. Group isolation that remains an ordered current-target effect uses the normal bottom-up typed scope:

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

Use finite `Layer(inputs, finiteNonEmptyDomain)` to replay an arbitrary mixed sequence as exactly one materializable value. This is required before Shader, Geometry, or another public value consumer when `CanBeUsedAsValueInput` is false and that localization is the intended semantics. The value constructor deliberately does not accept Full because it needs a finite conservative recording-time metadata boundary, and it publishes `EffectiveScale.Unbounded`. With symbolic inputs it reports the complete domain/domain hit test while retaining the symbolic internal edge; with concrete inputs it reports tight child-derived bounds/hit testing. A non-default finite `LayerRenderNode` limit records this value form.

### Raw canvas migration

A decorator whose only behavior is `PushLayer` plus one replay now uses typed `TargetLayerScope` as shown above, not a raw callback. An old decorator with additional unguarded canvas behavior that cannot be expressed by typed scopes migrates to `RawTargetScope`, and a zero-input/current-target `CreateLambda` migrates to `RawTargetCommand`:

```csharp
public override void Process(RenderNodeContext context)
{
    foreach (RenderFragmentHandle input in context.Inputs)
    {
        context.Publish(context.RawTargetScope(
            input,
            RawTargetScopeDescription.Create(
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
    RawTargetCommandDescription.Create(
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

Materialized targets now expose immutable `DeviceBounds` and derived `RasterBounds`. `CustomFilterEffectContext.DeviceBufferBounds(bounds, w)` is the canonical allocation footprint, and `DeviceBufferSize` returns its size. This intentionally replaces the old independent width/height rounding: fractional logical origins may contribute an additional device pixel. Legacy effects must use the returned target `Scale`, `DeviceBounds`, or `RasterBounds` for buffer-coordinate math and keep semantic `Bounds` for effect bounds/placement metadata.

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

Use `RenderScaleContract.MapInputSupply(Func<EffectiveScale, EffectiveScale> map, object? structuralKey)` for a pure density transform over an element-wise one-input map. Unlike `Custom`, it receives exactly the corresponding input's resolved supply and may return `EffectiveScale.Unbounded`. The delegate and optional immutable key identify the mapping shape. Transform and DrawableGroup use this contract, so a symbolic upstream supply is mapped again after graph-wide resolution rather than freezing a provisional recording value. It is rejected for source, capture, combine, and expansion topologies.

### Custom working-scale render node

`FilterEffect.Resource.CreateRenderNode()` remains. A custom `FilterEffectRenderNode` that only changes working-scale semantics overrides the protected `GetWorkingScaleContract()` hook and retains the base `Process` lowering. Returning `null` selects the standard `MaterializeAtWorkingScale` policy and its `s_out` floor; an explicit `Custom` result may intentionally be below `s_out`. After the base identifies finite or owner-relative isolation for mixed/value-ineligible inputs, it folds that standard or custom policy into the first surviving Shader, Geometry, or legacy operation. The callback is evaluated for each surviving branch with exactly one input supply and that branch's isolated effect-input bounds. Legacy multi-input work takes the densest concrete mapped result and falls back to `OutputScale` only when every branch is `Unbounded`. Allocation footprints are independent of callback count: before an opaque Custom callback they retain each branch's local-origin transforms and every intermediate/forced Flush, while the first Custom callback unions those transformed branch results and collapses later analysis to that aggregate domain because its implementation may combine or split targets. The largest known pre-callback backing footprint is carried forward at the transformed semantic position because Custom may retain the backing while moving or shrinking only `EffectTarget.Bounds`. No identity fragment or extra opaque/pass boundary is recorded. A no-item effect publishes the original inputs, commits no provisional isolation, and rolls back untransferred resources; its hook/resolver remains lazy unless `ApplyTo` probes the author-time scale. With a concrete single input, `FilterEffectContext.TryGetWorkingScale` returns the nominal effect-input density and `WorkingScale` remains readable; a later expanding operation can still clamp its own buffer. With symbolic or branch-dependent inputs, `TryGetWorkingScale` returns `false` and the getter throws rather than exposing a provisional/aggregate value. Forward analysis reevaluates the pure contract only after the owning scope is resolved. The contract cannot depend on the later ROI. Opaque runtime bounds that exceed the pure declaration are defensively exact-clamped during normalization, resampled at the reduced density, and published with that actual `EffectiveScale`/`DeviceBounds`; existing pixels are never retagged at a different density.

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

Both methods append in the existing authored order and synchronously update `context.Bounds` before returning. CurrentPixel Shader preserves it; WholeSource Shader and Geometry apply their forward `RenderBoundsContract`. A later operation in the same `ApplyTo` therefore observes the preceding new operation's conservative bounds just as it does for existing bounds-transforming methods. When the render-node input is symbolic, the legacy public `FilterEffectContext` starts with `Bounds == Rect.Invalid`; the engine may use a separate internal recorded-bounds hint to retain the provisional opaque segment, but that hint is not exposed to `ApplyTo` or through `RenderFragmentHandle`. When an earlier retained legacy custom item made Bounds Invalid, the new operation remains in that same opaque sequence and Bounds stays Invalid; Shader and Geometry do not split out into planner-visible typed fragments in this case. Scope-domain lowering resolves the symbolic unknown bound to the local owning target domain after enclosing transforms, clips, and target scopes are known, and forward analysis reevaluates retained bounds-transforming items from the resolved input bounds. The final semantic output is cropped to that domain, while internal opaque allocations remain uninspectable. Otherwise, validation/mapping failure leaves the item list and Bounds unchanged, and a new mapping may not return Invalid. An exception from the surrounding `ApplyTo` invocation rolls its items, Bounds, owned-resource transfers, and borrows back to the invocation checkpoint. Invalid scale results are failures, never identity/default fallbacks.

Operation-call compatibility does not preserve provisional author-time metadata. Symbolic owning-domain input intentionally presents `Bounds == Rect.Invalid`, and symbolic or branch-dependent input makes `WorkingScale` unavailable. An effect that derives an operation parameter from unavailable bounds must append deferred pure bounds mapping and an execution factory/callback that bind from the later resolved target bounds. Scale-dependent authoring must call `TryGetWorkingScale` and defer binding when it returns `false`. The engine invokes `ApplyTo` once; it does not replay authoring after resolution. This stricter metadata availability is an intentional break from synchronous author-time inspection, not a replacement lifecycle.

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
        Intent = RenderIntent.Preview,
        TargetDomain = targetDomain,
        OutputScale = outputScale,
        MaxWorkingScale = maxWorkingScale,
        UseRenderCache = true,
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

`RenderNodeRasterization.Bounds` preserves that selected logical domain, including shifted origins. A zero-area selection is a normal `IsEmpty` result with `Bitmap == null`; a non-empty selection owns a non-null bitmap even if all pixels are transparent. The result, not the renderer or caller separately, owns/disposes that bitmap. A former `RenderNodeProcessor.CreateRenderTarget` override becomes an injected `IRenderTargetFactory`; the renderer pool invokes it only on a compatible-pool miss and owns every accepted target until eviction or renderer disposal. A null factory selects the built-in current-backend RGBA16F allocator. The renderer borrows `root`, `targetFactory`, and `destination`. Request diagnostics remain an internal implementation/evidence seam rather than a public renderer option.

Standalone `RenderNodeRenderer.Render`/`Rasterize` requests have purpose `Auxiliary`; `Measure` is `Bounds` and `HitTest` is `HitTest`. Only the production `Renderer` creates `Frame` requests through an internal entry point. There is no public list-returning rasterizer because an effectful fragment stream has one painter-ordered `RenderNodeRasterization` result.

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
