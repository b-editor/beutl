# Breaking Changes and Migration Contract

Feature 004 intentionally replaces the executable render-node pull API. It is breaking for custom `RenderNode`/`RenderNodeOperation` authors and direct `RenderNodeProcessor` consumers in `Beutl.Engine`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and downstream plugins. Ordinary `FilterEffect.ApplyTo` implementations remain source-compatible unless they directly use the removed operation-backed `EffectTarget` members or subclass/consume the changed render-node API.

The implementation commit carrying this public change must use a breaking Conventional Commit such as:

```text
refactor(engine)!: record complete render requests before execution

BREAKING CHANGE: Beutl.Engine, Beutl.NodeGraph, Beutl.ProjectSystem, Beutl.AgentToolkit, and application render-node consumers now use void Process(RenderNodeContext), context-owned RenderFragmentHandle values, and high-level request entry points. Executable/disposable RenderNodeOperation, RenderNodeProcessor Pull APIs, OperationWrapperRenderNode.SetOperations, and operation-backed EffectTarget members were removed. RenderNodeContext is now an engine-created sealed recorder: Input/CalculateBounds/the cache setter migrate to Inputs/CalculateInputBounds/DisableRenderCache, and its static scale helpers move to RenderScaleUtilities. Rasterize now returns one owned RenderNodeRasterization carrying its logical Bounds, OutputScale, and nullable Bitmap; Measure reports separate OutputBounds and QueryBounds. Existing FilterEffect.ApplyTo implementations remain supported unless they used the removed executable members; custom nodes returned by FilterEffect.Resource.CreateRenderNode must migrate.
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
- mutable `RenderNodeContext.Input` array, `CalculateBounds()` name, and `IsRenderCacheEnabled` setter; replacements are read-only `Inputs`, `CalculateInputBounds()`, and `DisableRenderCache()`;
- static scale helpers on `RenderNodeContext`; `MaxBufferDimension`, `SanitizeMaxWorkingScale`, `ResolveWorkingScale`, and `ClampWorkingScaleToBufferBudget` move to the independent `RenderScaleUtilities` type;
- public `RenderNodeProcessor`, including `Pull`/`PullToRoot` operation arrays and the protected `CreateRenderTarget` override seam; it is replaced by `RenderNodeRenderer` plus injected `IRenderTargetFactory`;
- public `OperationWrapperRenderNode`/`SetOperations(RenderNodeOperation[])` retention across recording/request boundaries;
- `EffectTarget(RenderNodeOperation)` and `EffectTarget.NodeOperation`; `EffectTarget` remains an execution-time materialized-target type and no longer renders or disposes an operation handle.

The replacement is `void Process`, `RenderNodeContext.Inputs`, explicit fragment/value/target-scope recording, unified ordered publication, monotonic `DisableRenderCache`, nested recording, and high-level render/single-result-rasterize/measure/hit-test entry points. `RenderNodeRasterization` owns the one optional bitmap together with its logical bounds and output density, so shifted and empty output domains are not lost.

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

Use finite `Layer(inputs, finiteNonEmptyDomain)` to replay an arbitrary mixed sequence as exactly one materializable value. This is required before Shader, Geometry, or another public value consumer when `CanBeUsedAsValueInput` is false and that localization is the intended semantics. The value constructor deliberately does not accept Full because a later enclosing scope is not known when its immediate value bounds/density are read. A non-default finite `LayerRenderNode` limit records this value form.

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

Both forms remain in the current request. Separate-target nested rendering is recorded as an internal nested request before execution, not started from a render callback.

### EffectTarget and NodeGraph operation wrappers

`EffectTarget` itself remains because existing `FilterEffectContext.CustomEffect` callbacks use materialized targets. The engine now invokes those callbacks only at execution with render-target-backed `EffectTarget` instances. The public operation-backed constructor/property are removed; `Draw` and `Dispose` act only on the materialized target. Code that previously inspected `NodeOperation` migrates to Shader/Geometry/opaque recording before execution or consumes the execution-time `RenderTarget` supplied by the legacy custom-effect context.

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

`RenderScaleContract.PreserveInputSupply` is valid only where topology supplies one unambiguous source density per surviving output: an element-wise `OpaqueMap` (including zero-or-one discard) or per-fragment `TargetScope`/`RawTargetScope`. Source, capture, combine, and expansion descriptions must choose another valid scale contract; combine/expansion cannot silently preserve a conveniently selected input. `TargetLayerScope` has no author scale argument and always reports `EffectiveScale.Unbounded`. Validation occurs when the description is attached to its context method.

### Custom working-scale render node

`FilterEffect.Resource.CreateRenderNode()` remains. A custom `FilterEffectRenderNode` can still record custom scale semantics, but it does not allocate a working buffer in `Process`. It records an opaque/typed descriptor whose pure scale contract is resolved immediately from input supplies and complete conservative output bounds so the returned handle has a concrete `EffectiveScale`; it cannot depend on the later ROI.

Custom nodes must not use `OutputScale` as an intermediate ceiling. A non-supply custom scale choice must be declared in its operation's scale contract and bounded by `MaxWorkingScale` plus the per-buffer dimension clamp against complete output bounds during recording. A `Custom` resolver must return a finite value greater than zero; a throw, NaN, infinity, zero, or negative value fails and rolls back the current recording transaction rather than falling back to `OutputScale`. Later ROI crops allocation bounds without changing a valid published density.

## FilterEffect compatibility

This remains unchanged:

```csharp
public override void ApplyTo(FilterEffectContext context, Resource resource)
{
    context.Blur(resource.Sigma);
    context.AppendSKColorFilter(...);
    context.CustomEffect(...);
}
```

Existing methods keep their current-main ordering and behavior. New effects may opt into:

```csharp
context.Shader(shaderDescription);
context.Geometry(geometryDescription);
```

Both methods append in the existing authored order and synchronously update `context.Bounds` before returning. CurrentPixel Shader preserves it; WholeSource Shader and Geometry apply their forward `RenderBoundsContract`. A later operation in the same `ApplyTo` therefore observes the preceding new operation's conservative bounds just as it does for existing bounds-transforming methods. When an earlier retained legacy custom item already made Bounds Invalid, the new operation remains in that render-time opaque sequence and Bounds stays Invalid until execution supplies actual target bounds. Otherwise, validation/mapping failure leaves the item list and Bounds unchanged, and a new mapping may not return Invalid. An exception from the surrounding `ApplyTo` invocation rolls its items, Bounds, owned-resource transfers, and borrows back to the invocation checkpoint. Invalid scale results are failures, never identity/default fallbacks.

There is no migration to `Describe`, no `EffectGraphBuilder`, and no requirement to convert all built-in effects before the renderer-wide seam is usable.

Authors who return a custom render node from `FilterEffect.Resource.CreateRenderNode()` must migrate that node's `Process` implementation. Effects that directly used `EffectTarget.NodeOperation` or `EffectTarget(RenderNodeOperation)` must also migrate that executable escape; ordinary `FilterEffectContext` operation calls remain unchanged.

## Direct processor consumers

Callers migrate by intent:

| Old use | Replacement |
|---|---|
| `PullToRoot` then render each operation | `RenderNodeRenderer.Render(destination)` |
| `PullToRoot` then union operation `Bounds` | `RenderNodeRenderer.Measure().QueryBounds` |
| actual root write/raster extent (no exact old operation-bounds equivalent) | `RenderNodeRenderer.Measure().OutputBounds` |
| `PullToRoot` then call `HitTest` | `RenderNodeRenderer.HitTest(point)` |
| old `Rasterize` list / `RasterizeAndConcat` | one owned `RenderNodeRasterization` from `RenderNodeRenderer.Rasterize()` |
| retain/wrap one operation in NodeGraph | request-scoped `RecordNode` input binding |
| independent pull to fill render cache | selected capture point in current request |

All in-tree consumers migrate in the same change. No code outside the recorder/executor may enumerate executable operations because no such public object remains.

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

`TargetDomain` is needed by target-less `Measure`/`HitTest`/`Rasterize` when the graph publishes Full target access whose enclosing root has no real destination; a query rectangle never substitutes for that target domain. `Measure.OutputBounds` unions contributing value bounds with resolved potentially-writing target-effect regions, while `Measure.QueryBounds` remains the independent bounds/hit-test view. `RequestedRegion = null` selects complete `OutputBounds`; a non-null region is the exact final output/commit crop and still does not replace the target domain.

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
