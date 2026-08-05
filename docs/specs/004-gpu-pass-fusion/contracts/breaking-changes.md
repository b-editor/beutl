# Breaking Changes and Migration Contract

Feature 004 intentionally replaces the executable render-node pull API. It is breaking for custom `RenderNode`/`RenderNodeOperation` authors and direct `RenderNodeProcessor` consumers in `Beutl.Engine`, `Beutl.Editor`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and downstream plugins. Existing `FilterEffect.ApplyTo` operation calls remain source-compatible unless they directly use the removed operation-backed `EffectTarget` members or subclass/consume the changed render-node API, but synchronous author-time metadata access is intentionally stricter: symbolic `Bounds` is unavailable and symbolic or branch-dependent `WorkingScale` must be probed with `TryGetWorkingScale` and bound later in an execution callback.

The slice-2 auditor's stored integration message carrying this public change must use a breaking Conventional Commit. It must contain a literal `BREAKING CHANGE:` footer; a Markdown heading is not a substitute. Use this template:

```text
refactor(engine)!: record complete render requests before execution

BREAKING CHANGE: Beutl.Engine, Beutl.Editor, Beutl.NodeGraph, Beutl.ProjectSystem, Beutl.AgentToolkit, application render-node consumers, and out-of-tree Engine/plugin RenderNode implementations now use void Process(RenderNodeContext), context-owned RenderFragmentHandle values, and high-level request entry points. Executable/disposable RenderNodeOperation, RenderNodeProcessor Pull APIs, OperationWrapperRenderNode.SetOperations, and operation-backed EffectTarget members were removed. RenderNodeContext is now an engine-created sealed recorder: Input/CalculateBounds/the cache setter migrate to Inputs/TryCalculateInputBounds/DisableRenderCache, and its static scale helpers move to RenderScaleUtilities. RenderFragmentHandle no longer exposes direct Bounds, EffectiveScale, or HitTest members; authors use TryGetMetadata and TryHitTest and must handle symbolic owning-target dependencies. Rasterize now returns one owned RenderNodeRasterization carrying its logical Bounds, OutputScale, and nullable Bitmap; Measure reports separate OutputBounds and QueryBounds. Existing FilterEffect.ApplyTo operation calls remain available, but the FilterEffectContext.Bounds property is removed (engine-internal only) and symbolic inputs may make WorkingScale unavailable: effect authors must use TryGetWorkingScale and defer bounds/scale-dependent parameters to Shader, Geometry, or CustomEffect execution callbacks. Custom nodes returned by FilterEffect.Resource.CreateRenderNode must migrate.

BREAKING CHANGE: `RenderNodeCacheHelper.MakeCache`, `CreateDefaultCache`, and `CanCacheRecursiveChildrenOnly`, together with `RenderNodeCache.RejectCache` and `IsCacheRejected`, are removed. Cache lookup, miss capture, and atomic publication now occur only inside the complete request after dependency and region analysis; callers render through `RenderNodeRenderer`/the production `Renderer` and use `Invalidate` or `RenderNodeCacheHelper.ClearCache` to discard retained entries.

BREAKING CHANGE: `RenderNodeCache.Density` is no longer public, `UseCache` remains an internal inspection-only accessor, and `StoreCache` is removed entirely. Cache payloads are renderer-owned and may contain multiple outputs at independent effective scales, so a scalar public density or target/bounds-only tuple is not a sound inspection contract. Plugin code controls retention through `ReportRenderCount`, `CanCache`, `Invalidate`, and `RenderNodeCacheHelper.ClearCache` rather than reading or seeding engine payloads; seeding a cache entry is not a plugin operation.

BREAKING CHANGE: `TargetInputReadback` is renamed to the operation-neutral `RenderInputReadback` and is shared by `TargetCommandDescription.InputReadbacks` and `OpaqueRenderDescription.InputReadbacks`. Opaque `requiresReadback: true` migrates to one selector per authored input, normally `inputReadbacks: [RenderInputReadback.All, ...]`; `None` and `Values` avoid synchronizing unrelated runtime values. `OpaqueRenderSession.CreateOutput(bounds, density)` may select an independent finite positive density per runtime output. Direct `RenderNodeRenderer.Render`/`Rasterize` frame hosts set `RenderNodeRenderRequest.Purpose = RenderRequestPurpose.Frame`.

BREAKING CHANGE: `SKSLShader.ApplyToNewTarget` and the disposable `SKSLShader.Effect` escape hatch are removed in favor of explicit legacy-custom allocation, input mapping, building, and rendering. `SKSLShader.CreateBuilder()` returns the new `SKSLShaderBuilder` instead of SkiaSharp's `SKRuntimeShaderBuilder`, which is a source and binary break for out-of-tree callers that stored or passed the previous type. Use `CustomFilterEffectContext.CreateTargetLike` for same-bounds output or `CreateTarget` for changed bounds, borrow a GPU-backed mapped input through `UseMappedInputShader`, configure and build through `SKSLShaderBuilder`, and finish with `SKSLShader.RenderToTarget` inside that scope. The caller owns shaders returned by `SKSLShaderBuilder.Build()`. Uniforms must use the allocated destination's actual `Scale` and backing dimensions.

BREAKING CHANGE: `SKSLScriptEffect` normalizes its script uniforms against the physical output target rather than against a recomputed logical buffer size. `width`, `height`, and `iResolution` now report the replacement target's backing dimensions (`RenderTarget.Width`/`Height`, including the source's raster apron and fractional-origin rounding pixel) instead of `DeviceBufferSize(bounds, ResolveTargetDensity(bounds))`, and `iScale` now reports that target's actual `Scale` instead of the context's resolved target density. Existing user-authored SKSL scripts that derive coordinates or sampling from these uniforms observe different values wherever the effect input's physical footprint or density differs from the context's nominal working scale; no script source change is required for scripts that only pass them through.

BREAKING CHANGE: `IRenderer.GetBoundaries`, `IRenderer.GetBoundary`, and `Renderer.RecalculateBoundaries` are render-thread-affine queries. Bounds are resolved lazily from the recorded render graph after `Render` or `UpdateFrame`, so callers must dispatch these queries through `RenderThread.Dispatcher` instead of reading them from arbitrary threads.

BREAKING CHANGE: `RenderCacheOptions.Default` now denotes the same disabled policy as `RenderCacheOptions.Disabled`, so unchanged `Beutl.Engine` and plugin callers no longer opt into persistent render caching implicitly. `RenderNodeRenderRequest.UseRenderCache` is removed and replaced by `CacheOptions`: migrate `true`/`false` to `RenderCacheOptions.Enabled`/`RenderCacheOptions.Disabled`, and migrate request-specific admission rules to `new RenderCacheOptions(enabled, rules)`. Callers that require persistent caching must select `RenderCacheOptions.Enabled` explicitly or set `RenderNodeRenderRequest.CacheOptions = RenderCacheOptions.Enabled`.

BREAKING CHANGE: `RenderNodeRenderer` operations now accept an optional complete `RenderNodeRenderRequest`. `RenderNodeRendererOptions` composes a sanitized `DefaultRequest` with the renderer-lifetime `TargetFactory`; request intent, target domain, requested region, output/working scales, and cache policy move under that descriptor. A null operation argument selects the default snapshot, while a supplied descriptor completely replaces it, allowing one persistent renderer to serve changing regions and scales without discarding its structural/program caches or target pool.
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
        execute: session =>
        {
            using var output = session.CreateOutput(session.OutputBounds);
            output.Canvas.Use(canvas => session.Inputs[0].Draw(canvas));
            session.Publish(output);
        },
        bounds: OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
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
private readonly OpaqueRenderBoundsContract _operationBoundsContract =
    OpaqueRenderBoundsContract.FullInputs(CalculateExpandedBounds);

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
            execute: session => session.Canvas.Use(canvas =>
            {
                foreach (RenderExecutionInput input in session.Inputs)
                    input.Draw(canvas);
            }),
            affectedRegion: TargetRegion.Region(_bounds),
            queryBounds: _bounds,
            hitTest: RenderHitTestContract.OutputBounds,
            access: TargetAccess.ReadWrite,
            inputReadbacks: null,
            structuralKey: typeof(BackdropCommandNode),
            runtimeIdentity: new RenderRuntimeIdentity(_contentVersion)));

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

Use `RenderScaleContract.MapInputSupply(Func<EffectiveScale, EffectiveScale> map, object? structuralKey)` for a pure density transform over an element-wise one-input map. Unlike `Custom`, it receives exactly the corresponding input's resolved supply and may return `EffectiveScale.Unbounded`. The delegate and optional immutable key identify the mapping shape. Transform and DrawableGroup use this contract, so a symbolic upstream supply is mapped again after graph-wide resolution rather than freezing a provisional recording value. It is rejected for source, capture, combine, and expansion topologies.

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

`RenderNodeRasterization.Bounds` preserves that selected logical domain, including shifted origins. A zero-area selection is a normal `IsEmpty` result with `Bitmap == null`; a non-empty selection owns a non-null bitmap even if all pixels are transparent. The result, not the renderer or caller separately, owns/disposes that bitmap. A former `RenderNodeProcessor.CreateRenderTarget` override becomes an injected `IRenderTargetFactory`; the renderer pool invokes `Create(RenderTargetAllocationDescriptor)` only on a compatible-pool miss and owns every accepted target until eviction or renderer disposal. The descriptor carries exact device size, the fixed linear-premultiplied RGBA16F format, and the request's backend/device context when bound. A null factory selects the built-in allocator. The renderer borrows `root`, `targetFactory`, the descriptor's callback-scoped graphics context, and `destination` (`src/Beutl.Engine/Graphics/Rendering/RenderNodeRenderer.cs`, `src/Beutl.Engine/Graphics/Rendering/Planning/RenderTargetPool.cs`). Request diagnostics remain an internal implementation/evidence seam rather than a public renderer option.

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

That last qualifier is load-bearing. The generator emits each value-property backing field as
`private T _field = default!;`, so a detached resource does **not** inherit the default its `IProperty`
declares: `new Pen.Resource { Thickness = 4, Brush = black }` has `TrimEnd = 0` where the attached counterpart
has `100`, which trims the stroke away and makes `GetRenderBounds` return `0,0,0,0`; `new
SolidColorBrush.Resource { Color = red }` has `Opacity = 0`, which is why `ColorExtensions.ToBrushResource`
sets it by hand. Emitting the declared default into the field was measured and rejected — see
`docs/specs/004-gpu-pass-fusion/contracts/public-api.md`.

`Geometry.Resource.GetCachedPath` additionally commits its version guard and cached context only after
`ApplyTo` returns. A throwing author no longer installs a partially recorded path that later calls serve; each
call retries the build and rethrows.

`Geometry.Resource`'s stroke-path cache keys the pen through `EngineResourceIdentity.Of` rather than
`Pen.Resource.GetOriginal()`, which is null for every detached pen and therefore made any two of them compare
equal — the cache served the first pen's stroke for the second.

`EngineObject.Resource` gains `IsAttached` and `RequireOriginal()`. `GetOriginal()` keeps its declared
non-nullable return and still returns null for a detached resource. The dereferences this change migrated to
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
