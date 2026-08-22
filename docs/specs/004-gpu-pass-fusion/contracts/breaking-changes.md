# Breaking Changes and Migration Contract

## Summary

BREAKING CHANGE: render-node work is now recorded through `void RenderNode.Process(RenderNodeContext)`. Nodes publish transaction-scoped fragment handles; they do not receive an immediate canvas, return an operation, or control retained output state directly.

BREAKING CHANGE: public callback authoring now uses immutable `*Definition<TState>` objects and per-recording `.Call(state, bindings)` values. The former public callback-record construction path is no longer an authoring API.

BREAKING CHANGE: `RenderNode.HasChanges` is the only public content-invalidation signal. A node sets it when its pixel-, metadata-, or topology-affecting state changes. No public API accepts caller-supplied cache identity, resource content metadata, or a manual operation fingerprint.

The affected public surface is in `Beutl.Engine`. In-tree consumers in `Beutl.Editor`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and the test/benchmark hosts have already migrated, but out-of-tree render-node, filter-effect, geometry, mesh, renderer, target-factory, and brush-construction code must apply the recipes below.

The branch records the public break in `35e7f28b0` (`refactor(engine)!: record then plan the render pipeline and fuse GPU passes`) and the later target-factory/brush additions in `699332cc5` (`feat(engine)!: expose drawable-brush materialization and the cache opt-out`). The remaining fourteen each carry their own footer as well: `999ad728f`, `991f49e70`, `ee507067d`, `2974a6073`, `6dfd0f2d3`, `66cd2dc4c`, `7e2d928b5`, `48318a60f`, `70479b19f`, `a619d8046`, `3c33795ab`, `d53b155e8`, `449e71258` and `c8314e40f`, documented in the sections below. All sixteen contain a literal `BREAKING CHANGE:` footer, so no history rewrite is required.

`main` is squash-only, so the single commit that lands there is built from the pull request's title and body, not from any of those messages. The footer that reaches changelog tooling is therefore the one in the **pull request description**; a branch full of correctly footed commits does not supply it. Keep a `BREAKING CHANGE:` footer in the description that names `Beutl.Engine` and summarises the migrations below, and update it whenever a new breaking commit is added to the branch.

## Removed executable surface

The following executable pull model has no compatibility shim:

- `RenderNodeOperation`, including subclassing, disposal, `Render`, `HitTest`, and the `CreateLambda`, `CreateDecorator`, `CreateFromRenderTarget`, and `CreateFromSurface` factories;
- `RenderNodeOperation[] RenderNode.Process(RenderNodeContext)`;
- `RenderNode.PrepareForProcess(ImmediateCanvas)`;
- public construction or subclassing of `RenderNodeContext`;
- mutable `RenderNodeContext.Input`, `CalculateBounds()`, and the `IsRenderCacheEnabled` setter;
- the static scale helpers on `RenderNodeContext`;
- `RenderNodeProcessor`, including `Pull`, `PullToRoot`, the list-returning rasterizers, and the protected `CreateRenderTarget` override;
- `OperationWrapperRenderNode` and `SetOperations`;
- `EffectTarget(RenderNodeOperation)` and `EffectTarget.NodeOperation`;
- direct public access to `RenderNode.Cache`, `RenderNodeCache`, and `RenderNodeCacheHelper`.

The replacements are `void Process`, the sealed engine-created `RenderNodeContext`, transaction-scoped `RenderFragmentHandle` values, declarative fragment recording, `RenderNodeRenderer`, and request-owned or borrowed resources. `EffectTarget()` and `EffectTarget(RenderTarget, Rect, EffectiveScale)` remain for source-less and caller-materialized filter-effect work.

`RenderNodeContext.Inputs` is read-only. Use `TryCalculateInputBounds(out Rect)` instead of `CalculateBounds()`, `DisableRenderCache()` instead of assigning `IsRenderCacheEnabled = false`, and `RenderScaleUtilities` for `MaxBufferDimension`, `SanitizeMaxWorkingScale`, `ResolveWorkingScale`, and `ClampWorkingScaleToBufferBudget`.

## Migration rules

### Core node migration

Before, a node could prepare immediate work or return an operation. After, it records and publishes fragments:

```csharp
public sealed class PassthroughNode : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.PassThrough();
    }
}
```

`Inputs` is read-only and ordered. Use `TryCalculateInputBounds(out Rect)` and handle its `false` result when an input still depends on an enclosing target domain. Fragment handles expose metadata and hit testing only through their availability-checked APIs and cannot outlive `Process`.

Set `HasChanges` at the point the node's observable state changes:

```csharp
public float Opacity
{
    get => _opacity;
    set
    {
        if (_opacity == value)
            return;

        _opacity = value;
        HasChanges = true;
    }
}
```

### Publication migration

Publication is explicit. Record methods return a handle but do not make it an output.

```csharp
public override void Process(RenderNodeContext context)
{
    context.PublishMappedInputs(
        _opacity,
        static (current, input, opacity) => current.Opacity(input, opacity));
}
```

`PublishMappedInputs` maps every input to exactly one output in the same order. It is the appropriate replacement for a simple independent one-to-one loop. An empty input collection invokes no callback and publishes no output. A mapper may record intermediate fragments, but must not call a publication method itself; that is rejected and rolls back the whole node transaction.

Use `PassThrough`, `Publish`, or `PublishRange` directly for intentional no-output, selection, reorder, combination, expansion, nested work, or target-effect placement.

Publishing nothing is an intentional zero-output result; there is no implicit pass-through:

```csharp
public override void Process(RenderNodeContext context)
{
    if (!_isEnabled)
        return;

    context.PassThrough();
}
```

### Recording-time metadata

Fragment metadata may remain symbolic until the enclosing target domain is known. Replace unconditional operation properties with availability checks:

```csharp
public override void Process(RenderNodeContext context)
{
    bool hasAggregateBounds = context.TryCalculateInputBounds(out Rect aggregateBounds);

    foreach (RenderFragmentHandle input in context.Inputs)
    {
        bool hasMetadata = input.TryGetMetadata(out RenderFragmentMetadata metadata);
        bool hasHitTest = input.TryHitTest(_point, out bool hit);

        RecordWithoutAssumingMetadata(
            input,
            hasAggregateBounds ? aggregateBounds : null,
            hasMetadata ? metadata : null,
            hasHitTest ? hit : null);
    }
}
```

An unavailable value is not permission to drop or pass through an input. Record bounds, hit-test, and scale contracts that can be reevaluated after graph-wide resolution. `ValueCardinality`, `ContributesValuesToTarget`, and `CanBeUsedAsValueInput` remain directly readable on an active handle.

### Nested recording and retained wrappers

Do not retain fragment handles in fields. They are valid only during the active `Process` transaction. Replace retained `OperationWrapperRenderNode` operations with nested recording:

```csharp
public override void Process(RenderNodeContext context)
{
    IReadOnlyList<RenderFragmentHandle> outputs =
        context.RecordNode(_child, context.Inputs);
    context.PublishRange(outputs);
}
```

Use `RecordSubtree(root)` when the nested root should record its own descendants. `RecordNode(node, inputs)` remaps the supplied handles into a child transaction and remaps the child outputs back into the caller. A wrapper that references but does not own a child can use `ReferencesChildRenderNode`; disposing that wrapper does not dispose the referenced child.

### Materialized input

Replace `RenderNodeOperation.CreateFromRenderTarget` with an explicit resource lifetime and physical footprint:

```csharp
public override void Process(RenderNodeContext context)
{
    RenderResource<RenderTarget> target = context.Borrow(_target);
    var description = MaterializedInputDescription.FromRenderTarget(
        target,
        _bounds,
        _effectiveScale,
        _deviceBounds,
        _deviceGridOffset,
        RenderHitTestContract.OutputBounds);

    context.Publish(context.MaterializedInput(description));
}
```

`Borrow` leaves disposal with the caller, which must keep the target alive and unchanged through execution. `Own` transfers a disposable object to the request family. Neither method accepts a cache identity or version; persistent reuse follows `HasChanges`, child dependencies, and request cache policy. The declared `PixelRect` and device-grid offset are the target's actual physical footprint, not values to reconstruct from logical bounds.

### Source, combine, and expansion nodes

A source records deferred work without touching media, GPU objects, or native resources during `Process`:

```csharp
private static readonly OpaqueRenderDefinition<Color> s_source =
    OpaqueRenderDefinition<Color>.Create(
        static (session, color) =>
        {
            using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
            output.Canvas.Use(canvas => canvas.Clear(color));
            session.Publish(output);
        },
        OpaqueRenderBoundsContract.Source(new Rect(0, 0, 64, 64)),
        RenderHitTestContract.OutputBounds,
        RenderValueCardinality.Single,
        RenderScaleContract.MaterializeAtWorkingScale);

public override void Process(RenderNodeContext context)
{
    context.Publish(context.OpaqueSource(s_source.Call(_color)));
}
```

Use `OpaqueCombine(inputs, call)` for many-to-one work and `OpaqueExpand(inputs, call)` for runtime N-to-M work. Every input must be value-eligible. If an ordered stream contains target effects, wrap it intentionally with `Layer(inputs, finiteDomain)` or `OwningTargetLayer(inputs)` before passing it to a value consumer; do not silently discard its effects. The definition must declare aggregate bounds, hit testing, scale, and a compatible cardinality (`Single` for one combined output or `Dynamic` for an expansion). An empty runtime expansion is zero output, not identity.

### Target command, capture, and scope

Guarded target work uses the same definition/call split:

```csharp
private static readonly TargetScopeDefinition<float> s_opacityScope =
    TargetScopeDefinition<float>.Create(
        static (session, opacity) => session.Canvas.Use(canvas =>
        {
            using (canvas.PushOpacity(opacity))
                session.ReplayInput();
        }),
        RenderBoundsContract.Identity,
        RenderHitTestContract.AnyInput,
        RenderScaleContract.PreserveInputSupply);

public override void Process(RenderNodeContext context)
{
    context.PublishMappedInputs(
        _opacity,
        static (current, input, opacity) =>
            current.TargetScope(input, s_opacityScope.Call(opacity)));
}
```

`TargetCommandDefinition<TState>` declares its affected `TargetRegion`, independent query bounds, hit testing, access, per-input readback selectors, and resource slots. `TargetScopeDefinition<TState>` surrounds exactly one input and must call `ReplayInput()` exactly once. Raw variants are explicit opaque-external boundaries and are never persistently reusable.

A target capture is a value read, not an implicit redraw:

```csharp
RenderFragmentHandle capture = context.TargetCapture(
    TargetCaptureDescription.Create(
        TargetRegion.Region(_bounds),
        _bounds,
        RenderHitTestContract.None,
        TargetCaptureScaleContract.PreserveTargetSupply));

RenderFragmentHandle filtered = context.Shader(capture, s_tint.Call(_tint));
context.Publish(context.ContributeValues(filtered));
```

Use `TargetLayerScope(inputs, TargetRegion.Full)` for an ordered current-target isolation that remains non-value-eligible. Use `Layer` or `OwningTargetLayer` when the intentional result is one materializable value for a later Shader, Geometry, or opaque value operation.

### Cache migration

`RenderNodeCache` and `RenderNodeCacheHelper` are engine-internal. `MakeCache`, `CreateDefaultCache`, `CanCacheRecursiveChildrenOnly`, `RejectCache`, `IsCacheRejected`, `StoreCache`, `UseCache`, and direct cache density/state inspection are no longer plugin APIs.

Choose persistent caching per request with `RenderNodeRenderRequest.CacheOptions`. `RenderCacheOptions.Default` is disabled; callers that require it must select `RenderCacheOptions.Enabled` or construct `RenderCacheOptions` with explicit rules. A node reports content changes through `HasChanges`. A node that dynamically records a child it cannot list in `ChildNodes` must call `context.DisableRenderCache()` during that transaction.

### Callback migration

#### Guarded opaque work

Put callback code and fixed metadata in a reusable definition. Put values and tokens for this recording in the call.

```csharp
private sealed record DrawState(float Opacity);

private static readonly RenderResourceSlot<Brush.Resource> s_brush = new();

private static readonly OpaqueRenderDefinition<DrawState> s_draw =
    OpaqueRenderDefinition<DrawState>.Create(
        static (session, state) => session.UseResource(
            s_brush,
            brush => Draw(session, brush, state.Opacity)),
        OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
        RenderHitTestContract.AnyInput,
        RenderValueCardinality.Single,
        RenderScaleContract.PreserveInputSupply,
        resources: [s_brush]);

public override void Process(RenderNodeContext context)
{
    RenderResource<Brush.Resource> brush = context.Borrow(_brush);
    OpaqueRenderCall<DrawState> call = s_draw.Call(
        new DrawState(_opacity),
        [s_brush.Bind(brush)]);

    context.PublishMappedInputs(
        call,
        static (current, input, recordedCall) => current.OpaqueMap(input, recordedCall));
}
```

Use `OpaqueSource`, `OpaqueMap`, `OpaqueCombine`, or `OpaqueExpand` according to the fixed topology in the definition. Reusing a static/shared definition avoids needless allocation, but equivalent definitions recreated later still share the engine-derived plan; no manual identifier or singleton lifetime is required.

#### Target work

Use `TargetScopeDefinition<TState>` for one guarded replay scope and `TargetCommandDefinition<TState>` for a guarded current-target command. Declare bounds, hit testing, scale where applicable, target region/access, readback behavior, and resource slots in the definition; invoke it through `.Call`.

Raw canvas behavior has matching generic definitions. It remains opaque external work, but its binding schema is still checked:

```csharp
private sealed record RawState(RenderResource<IBackdrop> Backdrop);

private static readonly RenderResourceSlot<IBackdrop> s_backdrop = new();

private static readonly RawTargetCommandDefinition<RawState> s_command =
    RawTargetCommandDefinition<RawState>.Create(
        static (session, state) => session.UseResource(
            state.Backdrop,
            backdrop => backdrop.Draw(session.Canvas)),
        queryBounds: new Rect(0, 0, 1, 1),
        hitTest: RenderHitTestContract.None,
        resources: [s_backdrop]);

public override void Process(RenderNodeContext context)
{
    RenderResource<IBackdrop> backdrop = context.Borrow(_backdrop);
    context.Publish(context.RawTargetCommand(
        s_command.Call(new RawState(backdrop), [s_backdrop.Bind(backdrop)])));
}
```

For a raw scope, use `RawTargetScopeDefinition<TState>` and call `ReplayInput` exactly once. The raw session uses the token held in call state; guarded sessions use the declared slot. In both cases, the typed slot in the definition and `slot.Bind(token)` at the call site are mandatory.

#### Resources

Replace keyed or string-named registration with the lifetime-only APIs:

```csharp
RenderResource<Texture> texture = context.Borrow(_texture);
RenderResource<TemporarySurface> scratch = context.Own(new TemporarySurface());
RenderResourceBinding binding = s_texture.Bind(texture);
```

`Borrow` leaves ownership with the caller. `Own` transfers a disposable object to the request family. Neither method accepts identity or content arguments. `RenderResourceBinding` has no public constructor and binding names are not part of the API. A definition declares `RenderResourceSlot<T>` values in `resources:` and its call binds each one exactly once.

#### Shader and geometry work

Use a shader definition for fixed source and binding schema:

```csharp
private sealed record TintState(float Amount);

private static readonly ShaderDefinition<TintState> s_tint =
    ShaderDefinition<TintState>.CurrentPixel(
        """
        uniform float amount;
        half4 apply(half4 color) {
            return half4(color.rgb * amount, color.a);
        }
        """,
        static bindings => bindings.Uniform("amount", static state => state.Amount));

public override void Process(RenderNodeContext context)
{
    context.PublishMappedInputs(
        new TintState(_amount),
        static (current, input, state) => current.Shader(input, s_tint.Call(state)));
}
```

`ShaderDefinition<TState>.WholeSource` declares a whole-input shader and fixed bounds mapping. Shader value providers, custom uniform binders, and resource binders must be non-capturing `static` callbacks so changing values are supplied only by `TState` and invalidate through `HasChanges`. `ShaderDefinitionBuilder<TState>.Resource` declares typed child-shader slots. `GeometryDefinition<TState>.Create` follows the same definition/call pattern for geometry callbacks, bounds, hit testing, optional readback, and slots.

Existing `FilterEffectContext` authoring passes `ShaderCall<TState>` and `GeometryCall<TState>`:

```csharp
context.Shader(s_tint.Call(new TintState(_amount)));
context.Geometry(s_geometry.Call(new GeometryState(_radius)));
```

## FilterEffect compatibility

`FilterEffect.ApplyTo(FilterEffectContext, Resource)` remains the supported authoring entry point. Existing Skia, color, transform, and `CustomEffect` calls remain ordered, and `ShaderCall<TState>` and `GeometryCall<TState>` add typed stages without replacing `ApplyTo`:

```csharp
public override void ApplyTo(FilterEffectContext context, Resource resource)
{
    context.Blur(resource.Sigma);
    context.Shader(s_tint.Call(new TintState(resource.Amount)));
    context.Geometry(s_geometry.Call(new GeometryState(resource.Radius)));
    context.CustomEffect(resource.State, static (state, execution) => Execute(state, execution));
}
```

The former public `FilterEffectContext.Bounds` property is removed. Bounds stay engine-internal because an earlier opaque custom operation can make them symbolic. `WorkingScale` also is not unconditionally available: call `TryGetWorkingScale(out float)` during `ApplyTo`. If it returns `false`, keep authoring scale-independent and move device-pixel calculations into the shader, geometry, or custom-effect execution callback. The engine invokes `ApplyTo` once; it does not replay authoring after metadata resolution.

`FilterEffect.Resource.CreateRenderNode()` remains virtual. A custom `FilterEffectRenderNode` must use the new `void Process` contract. If the customization changes only working-scale semantics, override the protected `GetWorkingScaleContract()` and retain base `Process`; a `null` result selects `RenderScaleContract.MaterializeAtWorkingScale`.

Direct `FilterEffectActivator` consumers must classify execution explicitly:

```csharp
using var activator = new FilterEffectActivator(
    targets,
    builder,
    RenderIntent.Delivery,
    RenderRequestPurpose.Frame,
    outputScale,
    workingScale,
    maxWorkingScale);
```

The public constructor requires `RenderIntent` and `RenderRequestPurpose` before the optional scale arguments. A working-scale ceiling no longer infers either classification. `FilterEffectStageFallbackExecutor` is an internal execution path for typed Shader/Geometry suffixes after opaque work; it is not a public authoring API and does not make `ApplyTo` obsolete.

### EffectTarget and SKSLShader migration

Code that wrapped a `RenderNodeOperation` in `EffectTarget` must instead record the node in the current request or materialize an actual `RenderTarget` before constructing `EffectTarget`. `EffectTarget` no longer renders or disposes an executable operation.

`SKSLShader.Effect` is no longer public, `CreateBuilder()` now returns Beutl's disposable `SKSLShaderBuilder`, and `ApplyToNewTarget` is replaced by rendering into a caller-created target:

```csharp
EffectTarget output = context.CreateTargetLike(input);
try
{
    using SKSLShaderBuilder builder = shader.CreateBuilder();
    builder.Uniforms["amount"] = amount;
    shader.RenderToTarget(context, builder, output);

    input.Dispose();
    context.Targets[index] = output;
}
catch
{
    output.Dispose();
    throw;
}
```

`SKSLShaderBuilder.Uniforms` and `.Children` expose the Skia binding collections, and `Build()` returns a caller-owned `SKShader`. `RenderToTarget` borrows the supplied materialized target and does not transfer or replace its ownership; the caller remains responsible for committing or disposing it on every path.

### Metadata and scale migration

Bounds, hit testing, scale, cardinality, input readback, target access, and device-grid behavior are fixed definition metadata. Their callbacks must be deterministic, side-effect-free, and non-capturing.

For a one-input element-wise density transform, declare both directions of the density relationship:

```csharp
RenderScaleContract scale = RenderScaleContract.MapInputSupply(
    static inputSupply => inputSupply.IsUnbounded
        ? EffectiveScale.Unbounded
        : EffectiveScale.At(inputSupply.Value / 2),
    static outputDemand => EffectiveScale.At(outputDemand.Value * 2));
```

Both callbacks are reevaluated when required to resolve symbolic upstream metadata. Source, capture, combination, and expansion work must choose their own valid scale contract.

An operation that consumes its input at the density its own consumer demands — a supply map that reports a different density without resampling, or one that collapses to `Unbounded` — declares the forward callback alone:

```csharp
RenderScaleContract scale = RenderScaleContract.MapInputSupplyPreservingDemand(
    static inputSupply => inputSupply);
```

The name states that precondition, because the contract leaves backward demand unchanged. Reaching for it from an operation that resamples — an enlargement, a reduction — lets an unbounded input rasterize at the operation's own output demand and then be magnified, so the result is blurred by exactly the enlargement factor. The backward map is not derived from the forward one: the forward map may collapse to `EffectiveScale.Unbounded` and need not be invertible. `mapOutputDemandToInput` receives a concrete output demand and must return a finite positive density; the engine bounds the result by the request ceiling. Both callbacks may be reevaluated during graph-wide metadata resolution.

For a matrix-shaped operation, `TransformRenderNode.RescaleDensity` and `TransformRenderNode.RescaleDemand` supply the two halves; hold the matrix in a non-capturing metadata state and pass their bound methods as the two callbacks. They are not inverses — forward reports the least-scaled axis and backward answers the operator norm, each erring toward more detail — so under an anisotropic or sheared transform a round trip does not return its input.

`RenderScaleContract.Custom` declares no backward map and none can be attached to one, so an output demand reaches its inputs unchanged. A map-topology operation whose density differs from its input's must therefore use `MapInputSupply` rather than a custom resolver.

## Whole-source shader coordinate space

BREAKING CHANGE: a `ShaderDefinition<TState>.WholeSource` stage is now evaluated over its **complete** output. Its `coord` argument spans `[0, SemanticOutputSize]` and `ShaderExecutionContext.DeviceBounds` / `LogicalOrigin` describe the complete output footprint, even when the renderer only required a sub-region (content that overhangs the frame). Previously `coord` started at the required region's origin while `SemanticOutputSize` still described the complete output, so `coord / iResolution` never reached `1.0` and any absolute anchor — a mirror axis, a tile-grid origin, a pivot — moved by the clipped-off overhang.

`RequiredRegion` still reports the region actually being produced, so a stage that wants the destination extent reads it there.

Out-of-tree whole-source shaders and `ShaderResourceCoordinateSpace.OutputDevice` binders that worked around the old behaviour by subtracting `LogicalOrigin` (or by differencing `OutputBounds` against `DeviceBounds`) now compute zero and need no further change. Any binder that instead hard-coded the old required-region origin must drop that correction; leaving it in place double-corrects and moves the stage by the overhang in the opposite direction.

## Direct processor consumers

Replace each `RenderNodeProcessor` use according to its intent:

| Removed use | Current replacement |
|---|---|
| `PullToRoot` followed by rendering every operation | `RenderNodeRenderer.Render(destination)` |
| operation-bounds union for layout, selection, or hit-test queries | `RenderNodeRenderer.Measure().QueryBounds` |
| operation-bounds union used to size a raster | `RenderNodeRenderer.Measure().OutputBounds` |
| `PullToRoot` followed by operation hit tests | `RenderNodeRenderer.HitTest(point)` |
| `Rasterize` or `RasterizeAndConcat` | one owned `RenderNodeRasterization` from `Rasterize()` |
| protected `CreateRenderTarget` override | `RenderNodeRendererOptions.TargetFactory` |

A direct host supplies one complete request:

```csharp
using var renderer = new RenderNodeRenderer(
    root,
    new RenderNodeRendererOptions
    {
        DefaultRequest = new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            Purpose = RenderRequestPurpose.Frame,
            TargetDomain = targetDomain,
            RequestedRegion = requestedRegion,
            OutputScale = outputScale,
            MaxWorkingScale = maxWorkingScale,
            CacheOptions = RenderCacheOptions.Enabled,
        },
        TargetFactory = targetFactory,
    });

RenderNodeMeasurement measurement = renderer.Measure();
using RenderNodeRasterization rasterization = renderer.Rasterize();
if (!rasterization.IsEmpty)
{
    Bitmap bitmap = rasterization.Bitmap!;
    // bitmap pixel (0, 0) maps to rasterization.Bounds.Position
    // at rasterization.OutputScale device pixels per logical unit.
}
```

`OutputBounds` includes contributing values and potential target writes; `QueryBounds` is the independent layout/query view. `TargetDomain` supplies the owning domain for target-less requests that contain `TargetRegion.Full`; `RequestedRegion` does not replace or shrink that domain.

`RenderNodeRasterization` owns its nullable bitmap. A non-empty result has a bitmap even when every pixel is transparent; an empty result has `Bitmap == null`. Dispose the result, not the renderer, to release a returned bitmap.

`IRenderTargetFactory` now has only `Create(RenderTargetAllocationDescriptor)`. Remove `GetMaximumDimension` from custom factories. The descriptor supplies the exact device size, linear-premultiplied RGBA16F format, and current backend/context. A non-null return transfers ownership to the renderer; the factory itself stays caller-owned.

## Resource-side authoring dispatch

Geometry and mesh generation now dispatches on the resource snapshot rather than the engine object. The engine-object forms are removed without forwarding overloads:

- `Geometry.ApplyTo(context, resource)` becomes `Geometry.Resource.ApplyTo(context)`;
- `PathSegment.ApplyTo(context, resource)` becomes `PathSegment.Resource.ApplyTo(context)` for `ArcSegment`, `ConicSegment`, `CubicBezierSegment`, `LineSegment`, and `QuadraticBezierSegment`;
- `PathFigure.ApplyTo(context, resource)` becomes `PathFigure.Resource.ApplyTo(context)`;
- `PathGeometry.HitTestFigure(point, pen, resource)` becomes `PathGeometry.Resource.HitTestFigure(point, pen)`;
- `Mesh.ApplyTo(resource, out vertices, out indices)` becomes `Mesh.Resource.ApplyTo(out vertices, out indices)`.

Move an out-of-tree override into the generated `Resource` partial and read resource members directly:

```csharp
// Before
public override void ApplyTo(IGeometryContext context, Geometry.Resource resource)
{
    var value = (Resource)resource;
    context.MoveTo(new Point(value.Width, 0));
}

// After
public partial class Resource
{
    public override void ApplyTo(IGeometryContext context)
    {
        context.MoveTo(new Point(Width, 0));
    }
}
```

The same rule applies to `CubeMesh`, `PlaneMesh`, `SphereMesh`, and `ModelMesh`: move the override into the `Mesh.Resource` partial, drop the resource parameter and cast, and fill the output arrays from resource members. Do not call `GetOriginal()` from these overrides. A detached resource created through its public constructor has no backing engine object and must still be able to generate its geometry or mesh. State that formerly lived only on the engine object must move into the resource; `SKPathGeometry`, for example, now keeps and disposes its `SKPath` on `SKPathGeometry.Resource`.

`Scene3DRenderNode` is internal. Its in-tree implementation migrated to `void Process(RenderNodeContext)` and consumes the resource-side mesh API; it adds no separate public migration surface.

## Render intent, brushes, and allocation behavior

`Renderer` and `ImmediateCanvas` gain a trailing optional `RenderIntent` that defaults to `RenderIntent.Preview`. Existing call sites still compile, but delivery hosts must opt in explicitly so an intermediate allocation failure throws instead of dropping content:

```csharp
using var renderer = new Renderer(
    width,
    height,
    renderScale,
    maxWorkingScale,
    intent: RenderIntent.Delivery);
```

`BrushConstructor` has the final signature shape `(bounds, brush, blendMode, scale, maxWorkingScale, intent, drawableBrushMaterializer)`. Its allocation-failure policy no longer infers delivery from `float.IsPositiveInfinity(MaxWorkingScale)`; it uses `Intent`. Because `intent` defaults to `Preview`, an old delivery-oriented call such as `new BrushConstructor(bounds, brush, mode, scale, float.PositiveInfinity)` still compiles but changes from fail-fast to transparent degradation. Migrate it explicitly:

```csharp
var constructor = new BrushConstructor(
    bounds,
    brush,
    blendMode,
    scale,
    maxWorkingScale,
    intent: RenderIntent.Delivery,
    drawableBrushMaterializer: materializer);
```

The trailing `DrawableBrushMaterializer` is optional for source compatibility, but a `DrawableBrush` painted without one degrades to transparent. Prefer `ImmediateCanvas.CreateBrushConstructor(...)` when painting through a canvas because it carries the canvas density, working-scale ceiling, intent, and runtime materializer. A direct host that supports drawable brushes must provide a materializer; otherwise the missing nested content is intentional degraded output.

Positional callers after `intent` must be updated for the trailing materializer parameter. Custom `IRenderTargetFactory` implementations must drop `GetMaximumDimension`; the current hard axis bound remains `RenderScaleUtilities.MaxBufferDimension`.

`FilterEffectActivator`'s public constructor takes the same trailing optional `DrawableBrushMaterializer?` for the same reason: the activator is a direct host, and it forwards the materializer into every `CustomFilterEffectContext` it opens. Without one, a `DrawableBrush` used as a displacement map (or any other brush a custom effect paints) degrades to transparent, which for a displacement map silently turns the effect into a no-op:

```csharp
using var activator = new FilterEffectActivator(
    targets,
    builder,
    RenderIntent.Preview,
    RenderRequestPurpose.Auxiliary,
    drawableBrushMaterializer: materializer);
```

## Ownership summary

- `RenderNodeContext.Inputs` and every `RenderFragmentHandle` are borrowed, transaction-scoped values; authors never dispose or retain them.
- `RenderNodeRenderer` borrows its root, target factory, and destinations, and owns its structural/program caches and accepted factory-created targets.
- Each returned `RenderNodeRasterization` exclusively owns its nullable bitmap until disposal.
- `MaterializedDrawableBrush.Image` transfers to the `BrushConstructor` that requested it; the constructor disposes it once the tile shader is built, or once the fill fails, so a materializer returns a fresh image per call and never caches, shares, or disposes one.
- `Own` transfers one disposable resource to the request family; `Borrow` leaves the raw resource with its external owner.
- Definition slots and call bindings declare how deferred callbacks access resources; callbacks borrow session inputs, canvases, and declared resources only for callback duration.
- Deferred outputs remain executor-owned until publication or discard.
- A recording or execution failure publishes no partial output; cleanup continues best-effort without replacing the primary exception.

## Output reuse and failure behavior

The renderer decides whether recorded output is retained. Author code must only report changed node content through `HasChanges`; it cannot force, suppress, seed, or identify retained output. Raw target work is never persistently reusable.

Every `Process` invocation is transactional. An exception from recording or deferred execution preserves the primary failure, releases request-owned values best-effort, and yields no partial output.

## A custom effect's target allocation failure fails a delivery render

BREAKING CHANGE: `CustomFilterEffectContext.CreateTarget` and `CreateTargetLike` throw `InvalidOperationException` when the allocation itself fails during a `RenderIntent.Delivery` render, instead of returning an empty target. The `RenderIntent.Preview` return value is unchanged: the failure is logged and an empty target comes back so the caller can keep the source pixels.

Failure used to be reported the same way for both intents, which left every caller to invent its own policy — `SKSLScriptEffect` threw for both, `GLSLShader` silently kept the raw input, and `CreateTarget` relied on a later `Open()` throwing whatever the intent was. A delivery export could therefore ship an unprocessed frame while a preview failed outright. The policy now lives in the allocator, where the intent is known, so an out-of-tree effect gets it without having to find and use a helper.

An unmaterialized or unbounded `CreateTargetLike` source remains a legitimate skip and still returns an empty target for either intent; only a real allocation failure fails a delivery render. Out-of-tree effects that relied on the empty-target return to skip work under `Delivery` must handle the exception, and an effect that must fail delivery for a case the allocator cannot see — a target that allocated but carries no GPU texture — has to throw for itself.

## An SKSL script reads its semantic output size

BREAKING CHANGE: an `SKSLScriptEffect` script sees the semantic output size in `width`, `height` and `iResolution` rather than the raster-padded backing the old path inherited from the source target, and `iScale` resolves through the supply-driven working scale instead of copying the source target's density. A script that normalizes coordinates with `iResolution` renders slightly differently. Group opacity is no longer rounded to 8 bits, so an opacity of 0.5 composites at 0.5 rather than at 127/255. A whole-source shader may no longer declare a top-level name the fusion merger generates; those names are reserved and rejected when the source is parsed.

The uniform change follows from the script effect no longer recording a legacy custom effect, which used to make the whole enclosing segment opaque. A script now records declaratively — `main(float2)` as a whole-source stage and `apply(half4)` as a fully fusible current-pixel stage — and a declarative stage binds its size uniforms from the stage's own execution context rather than from whatever target the custom path was handed. Scripts the declarative surface cannot express still fall back to the custom-effect path, so no existing script stops running.

Group opacity now rides a runtime colour filter on the layer paint. Skia's two idiomatic alternatives — a paint alpha on the `SaveLayer` paint, and the `DstIn` mask the pop used to draw — both quantize to a byte inside an otherwise 16-bit linear pipeline; dropping the mask also removes the extra `SaveLayer` and `DrawPaint` that pop performed.

## A group's filter effect applies to each child

BREAKING CHANGE: a `FilterEffect` on a `DrawableGroup` is applied to each child separately rather than to the group's composited result. A project that relies on a split, mosaic, stroke or other target-list effect seeing one assembled image renders differently; wrap the children in a nested `Scene` to get the previous behaviour.

Measured before the change, a `SplitEffect` on a group of two children split the assembled image into four tiles; per child it gives eight, and a group holding five children gives twenty rather than four. A `SplitEffect` on a group is now byte-identical to that effect applied to each child individually, across every division setting, scale and budget.

The group's isolation layer is still recorded, but only around the opacity and blend axes, so a group's opacity is still applied exactly once instead of once per nesting level and a child's non-`SrcOver` `BlendMode` still composites against the group.

## An identity colour matrix records no stage

BREAKING CHANGE: `FilterEffectContext.ColorMatrix` and the filters built on it — `Brightness`, `Saturate`, `HueRotate`, `Lighting` — record nothing when the resulting colour matrix is exactly the identity. As with the zero-radius morphology below, a call that used to contribute an item to `CountItems()` no longer does, and a subtree whose only effect was an identity matrix has no isolation fragment of its own, so it takes the working scale of its surroundings rather than resolving one for itself.

Rendered output is unchanged, which is the whole point: the colour matrix stage unpremultiplies, applies the matrix, clamps, and re-premultiplies, so even an identity matrix computes `(c / a) * a`, which is not the identity in floating point, and the clamp at 1.0 can bite at an antialiased edge where `c / a` rounds just above one. A `Brightness` with `Amount` 100 is exactly the identity and still moved the output by one fp16 ULP; SwiftShader happened to round back to the original half and an Intel UHD Graphics 630 does not.

The generic `ColorMatrix<T>(T, Func<T, ColorMatrix>)` overload now evaluates its factory while recording rather than deferring it into the recorded item, and keys identity on the resulting matrix rather than on the `(data, factory)` pair. A factory whose result depends on state mutated between recording and execution therefore yields its record-time value.

## Zero-radius morphology records no stage

BREAKING CHANGE: `FilterEffectContext.Dilate` and `.Erode` clamp each radius per axis at record time and record **no stage at all** when both clamped radii land on zero. A call that used to contribute an item to `CountItems()` no longer does.

A negative radius previously produced three contradictory descriptions of the same operation: the Skia factory returned null (a pass-through), the sampling map clamped to the identity, but the forward bounds map used the raw radius and deflated the declared output by `|r|` per side. Under the region-driven pipeline that deflation applied twice, hard-cropping the content by `2|r|` logical px per side at every scale; past half the shorter side the doubled deflation went negative-extent and failed the render outright.

The early return also repairs the pre-existing zero-radius case: a degenerate morphology stage still re-grids the content through an intermediate and shifts antialiased edges, so recording nothing is now a byte-exact pass-through where an identity-radius stage used to deviate.

A subtree built only from such calls has no isolation fragment of its own and takes the working scale of its surroundings rather than resolving one for itself. Out-of-tree code that kept bounds bookkeeping keyed on `CountItems()`, or derived a cache key from it, must stop assuming a one-to-one mapping from call to recorded item. The per-axis clamp keeps a mixed radius such as `(-6, 5)` a real y-only morphology.

## Built-in Skia filters replay on the destination's device grid

BREAKING CHANGE: `DropShadow`, `DropShadowOnly`, `Dilate`, `Erode`, `MatrixConvolution` and `Transform(Matrix, BitmapInterpolationMode)` now report `SupportsDirectReplay`, and a **chain** of built-in Skia filter segments over a vector drawable is replayed as one device-space save layer whenever the fragment the chain terminates at admits it. Previously only `Blur` took that path, and any chain of two or more segments fell back to materializing each segment in the drawable's local space under a non-pixel-aligned drawable transform.

Filter parameters keep their units — `Drawable.Render` pushes the drawable transform outside `PushFilterEffect`, so the filter's local space still carries it — but every effect in the stack now resolves at the destination's device resolution. Under a drawable transform that scales an axis down, a morphology radius or shadow offset that used to be applied in the drawable's own units is applied after that transform, so a spatial parameter that maps to less than one device pixel rounds away instead of growing the content.

This is what stops a drawable squeezed below one device pixel from losing its ink: measured on a 6 x 100 bar under `ScaleTransform(10%, 100%)`, blur kept 0.06% of the unfiltered ink at output scale 0.5 and 2.8% at 0.333, while drop-shadow-only and dilate went to exactly zero at 0.25 and 0.5.

Output under an identity or pixel-aligned drawable transform is unchanged.

The filter's save layer is opened one device pixel wider than the content on every side, because a layer whose device bounds hug the content loses the coverage of content thinner than one device pixel. The layer therefore guarantees a **bound, not an exclusion**: nothing more than one device pixel outside the content is reachable, and that margin starts transparent because `SaveLayer` clears it, so it can never carry pixels nobody wrote. A spatial filter that relied on the layer clipping exactly at the content bound now samples up to one device pixel further.

## A pending Skia colour filter is applied once

BREAKING CHANGE: `SKImageFilterBuilder.GetFilter()` now clears the pending colour filter once it has folded it into the returned image filter, so repeated calls return the same chain instead of stacking another copy on each call.

`AppendSkiaFilter` calls `GetFilter()` mid-chain to take the filter built so far as its input, and the flush that materializes the chain calls it again. A colour filter recorded through `FilterEffectContext.ColorMatrix`, `LuminanceToAlpha`, `BlendMode(Color, BlendMode)` or `AppendSKColorFilter` and followed by any Skia image filter was therefore folded twice and applied twice. Measured on `Split(2, 2)` wrapping `Delay(250ms, Group(animated Brightness, animated Blur))`, the per-tile factors came out as the exact squares of the correct ones: 1.6890 / 1.1564 / 0.7224 / 0.3906 against 1.30 / 1.075 / 0.85 / 0.625.

No built-in effect reaches this path on the current branch — `Brightness` and the other colour operations record a `CurrentPixel` shader stage instead — so in-tree rendering is unchanged. A plugin that compensated for the doubling will render differently.

## A custom effect's input is rasterized on the grid it crops on

BREAKING CHANGE: the executor strips the sub-pixel phase from the device grid a filter-effect segment containing a custom (imperative) effect executes on, and **every nested execution frame that materializes that segment's inputs inherits the stripped grid**.

An imperative callback crops and re-lays-out its targets in whole device pixels, and its input is anchored on the whole-pixel part of the ambient translation. Handing it a grid with the fraction intact made the flush resample the input onto the whole-pixel grid instead; a bilinear half-pixel shift over an edge already at 0.5 coverage leaves 0.75, so the effect's outer edge lost coverage before the callback ever saw it. The inheritance is load-bearing: `FilterEffectRenderNode.Process` emits a separate fragment per shader and geometry stage, so an ordinary colour effect in front of the custom one moves that rasterization into a nested frame that would otherwise re-derive the fractional grid.

Content rasterized in those frames is snapped rather than resampled, so it keeps its edge coverage but moves by the phase that was stripped — anywhere in `[0, 1)` device pixels, since the grid origin drops `frac(offset x density)`. A fragment that feeds both the segment and a consumer outside it is materialized once, so whichever consumer reaches it first fixes the grid for both; in practice the outside consumer runs at top level, where the grid is already zero-phase, so the segment still gets a snapped input.

Two phases are deliberately not touched, and are therefore not snapped: the phase carried by a callback's own target bounds, and the grid of a separate render request such as a `DrawableBrush` source materialized below the segment.

This affects `SplitEffect`, `PartsSplitEffect`, `LayerEffect`, `Clipping`, `TransformEffect`, `StrokeEffect`, `FlatShadow`, `PixelSortEffect`, `PathFollowEffect`, `ShakeEffect`, `DelayAnimationEffect`, the displacement-map effects, the script effects, and any plugin effect built on `FilterEffectContext.CustomEffect`.

## One rectangle-bounds map

BREAKING CHANGE: `Rect.TransformToClippedAABB` is gone. `Rect.TransformToAABB` takes its place, gaining an optional `nearPlane` parameter and clipping the rectangle at the matrix's camera plane before mapping it. The raw mapped-corner box is no longer public surface.

Rename `TransformToClippedAABB` calls to `TransformToAABB`; they are otherwise unchanged, including the default near plane, so a caller that opted into `Rect.RasterizerNearPlane` keeps that behaviour. Existing `TransformToAABB` calls compile unchanged and return the same box for every affine matrix, and for every perspective matrix the rectangle does not straddle.

Where the rectangle **does** straddle the `w = 0` plane, the answer changes from a box on the wrong side of the image to one that contains it. The two methods were bit-identical everywhere except in precisely that broken case, so a caller could not discover the difference by testing — which is why only the safe one is published now. Code that genuinely wants the raw mapped corners there must map the four corners itself.

`Rect.DefaultNearPlane` (0.05) is a pragmatic bound, not the rasterizer's: it sits 820x in front of `Rect.RasterizerNearPlane` (Skia's `1 / 16384`), so a near-edge-on layer declares bounds that exclude pixels Skia still draws. Clipping at the exact value is not affordable as a default — a 1200x54 layer at the default Depth of 500 rotated 60 degrees about Y would declare a box 4.73 million px wide and collapse the working scale by ~289x. Callers that intersect the result with their own target before sizing a buffer should pass `Rect.RasterizerNearPlane`.

## A sheared filter layer keeps its perpendicular pixel

BREAKING CHANGE: the apron the engine opens around a directly replayed Skia filter (internally `ImmediateCanvas.PushFilterLayer`) is derived from the transformed basis **area**, not from the transformed basis lengths. Every edge of the content now sits exactly one device pixel inside the layer whatever basis the canvas carries, so content under a sheared transform — a `SkewTransform`, or any transform group that composes one — renders differently: its layer is wider and keeps antialiased coverage that used to be clipped away.

`Drawable.Render` pushes the drawable transform outside `PushFilterEffect`, so a shear is live on the destination canvas whenever the executor replays a built-in Skia filter chain onto it. Inflating the bounds by `dx` along x moves a vertical edge perpendicular to itself by `dx * |det| / devicePerY` device pixels, not by `dx * devicePerX`; the two agree only when the basis is orthogonal, and a shear drives `|det|` below the product of the basis lengths. The apron each axis needs is therefore the **other** axis's basis length over the determinant. Measured on the basis an 80 degree `SkewTransform` produces at output scale 2 — rows `(2, 0)` and `(1.134, 0.2)` — the previous apron bought 0.174 device pixels instead of one, and a 100 x 6 bar under it lost 9.4% of its ink to a blur too small to move a pixel, against 0.35% now.

The visible change is confined to filters whose own margin is smaller than that shortfall. Skia grows a save layer by the image filter's own radius, so a blur of sigma 0.5 logical units or more at output scale 2 already covered the deficit; a near-identity blur, a zero-radius morphology, and any plugin filter with no spatial extent did not.

Unsheared transforms are unchanged bit-for-bit, not merely within rounding: the apron keeps the reciprocal-of-basis-length form whenever the basis rows are orthogonal to within `1e-5` of the product of their lengths. Composing a rotation with an anisotropic scale leaves the rows orthogonal but misses an exactly zero dot product by up to `1e-7` relative, while the shallowest shear that can move a device pixel misses it by `1e-3`, so the split separates float rounding from real shear with three orders of magnitude to spare on each side. This matters because an apron landing on a whole device pixel would otherwise round out to a layer one pixel larger.

A basis that collapses the plane now leaves the bounds uninflated, joining the existing non-finite and non-positive guards. It previously inflated by the reciprocal of its collapsed basis length, which is an arbitrary amount of logical space for content that has no area to preserve coverage for.

## ChromaKey matches its key colour in linear light

BREAKING CHANGE: `ChromaKey` no longer relies on a `1/255` widening of the hue and saturation edges as its match tolerance. That widening survives as smoothstep edge slack, but the match itself is now tested against the key colour in premultiplied linear light, within half an 8-bit code per channel plus one half-precision ulp, and a match there is a mask of zero whatever the hue and saturation differences say. The hue term is additionally weighted by the smaller of the pixel's and the key's linear chroma, ramping in between one and two linear codes, so hue stops voting where quantization alone could have manufactured it. `Boundary` still controls only how gradually the mask ramps past the threshold.

The tolerance was applied in the wrong colour space. A constant paint colour reaches the shader folded to 8 bits in the destination colour space, and the render targets are linear F16, so the grid the error lands on is linear — but the tolerance sat after `linearToSrgb`, where half a linear code is not a fixed quantity. Near black it spans about ten sRGB levels, roughly forty times the tolerance; near white it spans a fifth of one.

The consequence was that a fill did not key against its own colour. `rgb(20,18,22)` has all three channels round to linear code 2, so the pixel arrives as an exact grey: saturation disagreed with the key by 0.1818 and hue by 0.2500 against a 0.0039 threshold. `rgb(10,40,20)` disagreed by 0.0821, `rgb(5,5,60)` by 0.0835, and even `rgb(60,180,75)` — bright and saturated — by 0.0073, enough to leave 95% of its alpha at `Boundary` 0. Sampling 225 solid fills across the cube, 118 failed to self-key; all 225 now key to zero alpha.

This was never confined to the fused pipeline, and never to rectangles. Only an axis-aligned rectangle gave Skia a full-coverage quad, so an `EllipseShape` or `RoundedRectShape` with the same dark fill self-keyed at the same 0.1818 and 0.0820 residuals well before this branch.

Content that already keyed is unaffected: the tolerated neighbourhood of `rgb(206,92,42)`, `rgb(240,240,250)` and `rgb(12,12,12)` measured identical before and after, level for level. Because the band is tested premultiplied, it is independent of coverage, so the antialiased edge of a keyed shape now keys with its interior; the same property means a pixel faint enough that half a linear code swamps its colour matches any key, which at that alpha is a change of at most a fraction of a percent of coverage.

The chroma gate has one visible consequence beyond the fix. A neutral fill has no hue to compare, so it can no longer be kept out of a key by the hue term alone: with `SaturationRange` widened to 100, a mid grey that a lime key used to leave alone is now removed. At any narrower `SaturationRange` the saturation term still keeps it, as before. This replaces the previous behaviour, where a neutral pixel took the `h = 0` that `rgb2hsv` returns at zero chroma and therefore matched a red key while surviving every other hue — a distinction the pixel did not carry.

## The short supply-mapping name is the one that carries demand back

BREAKING CHANGE: `RenderScaleContract.MapInputSupply(Func<EffectiveScale, EffectiveScale>)` is renamed to `RenderScaleContract.MapInputSupplyPreservingDemand(Func<EffectiveScale, EffectiveScale>)`. The two-callback `RenderScaleContract.MapInputSupply(Func<EffectiveScale, EffectiveScale>, Func<EffectiveScale, EffectiveScale>)` keeps its name and is unchanged.

| Before | After |
|---|---|
| `RenderScaleContract.MapInputSupply(map)` | `RenderScaleContract.MapInputSupplyPreservingDemand(map)` |
| `RenderScaleContract.MapInputSupply(map, mapOutputDemandToInput)` | unchanged |

Nothing about how either contract resolves changed; this is a rename and a documentation change. `spec.md` FR-030 records the earlier plan for a state-first `MapInputSupply<TState>(state, map, structuralKey)`; that shape was never built, and the pair documented here is the delivered surface.

The two forms were overloads of one name, and the name described only the forward half both of them share. An author reaching for a one-input density map met the one-callback signature first and had no signal that a second existed, so a map that resampled — an out-of-tree `OpaqueMap` that enlarges — declared its output supply, silently fell back to the identity backward map, and let a downstream materialization rasterize the source below the density the enlargement needed. The failure is invisible until someone looks at a blurry frame.

The sibling `RenderBoundsContract` had already settled this: `Create` takes both directions and the narrower `CreateFullInput` names its own backward behaviour. `RenderScaleContract` now reads the same way, and splitting the overload set means the narrow form can no longer be reached by dropping an argument — each name carries its own documentation, and the compiler rejects a one-argument `MapInputSupply` outright.

The new name is a precondition, not a warning label. Leaving demand unchanged is exactly right for a supply map that reports a different density without resampling, or one that collapses to `Unbounded`, which is the common case; `MapInputSupplyPreservingDemand` says which operations it fits rather than implying the contract is a degraded variant. It also pairs with the well-known `PreserveInputSupply`, whose demand pass-through is correct by construction.

`RenderScaleContract.Custom` has the same unchanged-demand fallback and no way to attach a backward map. It is deliberately left alone here — adding a demand callback to a custom resolver is a design change, not a rename — but its documentation now states the fallback and points at `MapInputSupply`.

## The graphics backend contract gained members and lost a default

BREAKING CHANGE: `Beutl.Graphics.Backend` changed shape for anyone implementing or calling it directly. Every
item below is source-breaking; none has a default implementation or an overload that preserves the old call.

| Surface | Before | After |
|---|---|---|
| `ITexture2D` | — | adds `bool RequiresSkiaFlushForBackendInterop { get; }` |
| `ITexture2D` | — | adds `void PrepareForSkiaRendering()` |
| `ITexture2D` | — | adds `void PrepareForSkiaSampling(bool requireCompletion)` |
| `IGraphicsContext.CreateRenderPass3D` | `TextureFormat depthFormat = TextureFormat.Depth32Float` | `TextureFormat? depthFormat` — required, `null` for a colour-only pass |
| `IGraphicsContext.CreateFramebuffer3D` | `ITexture2D depthTexture` | `ITexture2D? depthTexture` |
| `IFramebuffer3D.DepthTexture` | `ITexture2D` | `ITexture2D?` |
| `PipelineOptions` | — | adds `ImmutableArray<SpecializationConstant> SpecializationConstants { get; set; }` |

The three `ITexture2D` members exist because the fused pipeline hands one texture back and forth between Skia
and the backend within a frame. Skia records into a surface it owns while the backend records into the same
image, and neither can see the other's pending work, so the hand-off needs an explicit point at which the
preceding side submits and establishes visibility. `RequiresSkiaFlushForBackendInterop` lets a caller skip that
cost on a backend where the two never share, and `PrepareForSkiaSampling`'s `requireCompletion` distinguishes a
hand-off that can be expressed with GPU synchronization from one that has to wait on the host. An
implementation that has no Skia interop answers `false` and leaves the two methods empty.

The nullable depth attachment is what lets a pass declare that it writes colour only. The old default silently
gave every render pass a `Depth32Float` attachment, including the several passes in this pipeline that never
read or write depth, and a default cannot be removed while keeping the parameter optional without changing
what existing call sites mean. Making it required is the change that makes those call sites state their
intent; `null` is the colour-only pass, and `depthLoadOp` is ignored for one.

`SpecializationConstant` is additive: an implementation that ignores `SpecializationConstants` compiles and
behaves as before, and only a backend that wants compile-time specialization needs to read it.

## A source declares the room its rasterization needs instead of publishing it

BREAKING CHANGE: `OpaqueRenderBoundsContract.Source` takes an optional `Thickness rasterOutset`, and
`RenderNodeContext.PaintedSource` takes a matching optional argument. Existing calls compile unchanged;
both signatures moved, so a caller compiled against the previous assembly must be rebuilt.

| Before | After |
|---|---|
| `OpaqueRenderBoundsContract.Source(outputBounds)` | unchanged, or `Source(outputBounds, rasterOutset)` |
| `PaintedSource(..., resources)` | unchanged, or `PaintedSource(..., resources, rasterOutset)` |

The outset is logical room per side that widens only the buffer the source draws into. Nothing
downstream sees it: the fragment still publishes `outputBounds`, and that is what places it.

The pipeline already had a fixed one-device-pixel raster apron for a source whose rasterization spills
past its bounds, and that is what this generalizes. A fixed pixel is the wrong unit whenever the spill
is measured in logical units and varies with density, which is exactly the text case below. An author
whose source draws entirely inside its bounds — every built-in but text — declares nothing and gets the
previous behaviour.

## Text publishes the rectangle it occupies, not the one its masks need

BREAKING CHANGE: a `TextRenderNode` fragment publishes `FormattedText.ActualBounds`. It previously
published `FormattedText.GetRasterBounds(OutputScale)`. Code that read those bounds to place, measure or
lay out text now sees the text's own rectangle, unchanged by render density.

The bounds a fragment publishes are what place it, so a density-dependent value moved the text whenever
the density changed. Measured on this branch, one string published `(0, -44, 355.56, 60)` at a 50%
preview and `(2, -41, 351.56, 54)` at 100% and at a 2x export — the same project, three compositions.
`RasterBounds` documents this itself: only the allocated footprint may use it, and layout stays on the
semantic bounds.

Publishing the semantic bounds alone would clip the hinted glyph masks, which reach two to three logical
units outside them and by a different amount at every density. The masks' room is therefore declared as
the raster outset above, so the buffer still clears them while the published rectangle stays put.

The emptiness gate still tests the mask: a glyph can have a degenerate outline and rasterize something,
and that case has no scale-independent rectangle to be placed by, so it falls back to the mask footprint.

`main` published `ActualBounds`, so this restores the composition a project had before the branch.

## A particle covers the rectangle it is turned into, and is resampled into it

BREAKING CHANGE: a particle's extent is the bounding box of its scaled and rotated source rather than a
square of the source's longer side, and the blit resamples with Mitchell rather than point sampling.
Both change the pixels a `ParticleEmitter` produces.

The extent is what the layer buffer is allocated from, so the previous square clipped whatever the
rotation pushed outside it — a 20x20 source turned 45 degrees reached about 4.14 further along each axis.
The new extent is exact rather than merely larger: an unrotated non-square source now allocates less than
it did.

Point sampling reduced each particle to whichever texels its sample points landed on, which is visible as
a stair-stepped edge on every particle whose size is not exactly the source's. Measured on a magnified
particle, its edge carried 81 distinct alpha values against 314 resampled. Mitchell is the resampler the
canvas applies to any other scaled bitmap, and is what the pipeline used before this branch.

## What these three change in the corpus

The differential harness renders the case corpus on two builds and compares every shot. Between the
commit before these three changes and the commit after, 2,217 of 94,862 shots differ on Linux and 2,190
on Windows, with no render errors, no dimension mismatches, no shot blank on one side only, and no
non-finite pixel on either. Every differing case draws text, particles, or a chroma key; no case without
one of those moved.
