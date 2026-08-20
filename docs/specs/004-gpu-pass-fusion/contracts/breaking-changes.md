# Breaking Changes and Migration Contract

## Summary

BREAKING CHANGE: render-node work is now recorded through `void RenderNode.Process(RenderNodeContext)`. Nodes publish transaction-scoped fragment handles; they do not receive an immediate canvas, return an operation, or control retained output state directly.

BREAKING CHANGE: public callback authoring now uses immutable `*Definition<TState>` objects and per-recording `.Call(state, bindings)` values. The former public callback-record construction path is no longer an authoring API.

BREAKING CHANGE: `RenderNode.HasChanges` is the only public content-invalidation signal. A node sets it when its pixel-, metadata-, or topology-affecting state changes. No public API accepts caller-supplied cache identity, resource content metadata, or a manual operation fingerprint.

The affected public surface is in `Beutl.Engine`. In-tree consumers in `Beutl.Editor`, `Beutl.NodeGraph`, `Beutl.ProjectSystem`, `Beutl.AgentToolkit`, the application, and the test/benchmark hosts have already migrated, but out-of-tree render-node, filter-effect, geometry, mesh, renderer, target-factory, and brush-construction code must apply the recipes below.

The branch records the public break in `35e7f28b0` (`refactor(engine)!: record then plan the render pipeline and fuse GPU passes`) and the later target-factory/brush additions in `699332cc5` (`feat(engine)!: expose drawable-brush materialization and the cache opt-out`). The rendering-correctness follow-ups each carry their own footer as well: `14caa5eab`, `c144a917d`, `db8ed2af2`, `6cd58b87d`, `ef2bc38f7`, `c644647a6` and `ea03db4c2`, documented in the sections below. Every one of those commits contains a literal `BREAKING CHANGE:` footer, so no history rewrite is required.

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

For a one-input element-wise density transform, use:

```csharp
RenderScaleContract scale = RenderScaleContract.MapInputSupply(
    static inputSupply => inputSupply);
```

The map is reevaluated when required to resolve symbolic upstream metadata. Source, capture, combination, and expansion work must choose their own valid scale contract.

That forward-only overload passes backward demand through unchanged, so an operation whose density differs from its input's — an enlargement, a reduction — must declare both directions:

```csharp
RenderScaleContract scale = RenderScaleContract.MapInputSupply(
    static inputSupply => inputSupply.IsUnbounded
        ? EffectiveScale.Unbounded
        : EffectiveScale.At(inputSupply.Value / 2),
    static outputDemand => EffectiveScale.At(outputDemand.Value * 2));
```

Without the second callback an unbounded input rasterizes at the operation's own output demand and is then magnified, so the result is blurred by exactly the enlargement factor. The backward map is not derived from the forward one: the forward map may collapse to `EffectiveScale.Unbounded` and need not be invertible. `mapOutputDemandToInput` receives a concrete output demand and must return a finite positive density; the engine bounds the result by the request ceiling. Both callbacks may be reevaluated during graph-wide metadata resolution.

For a matrix-shaped operation, `TransformRenderNode.RescaleDensity` and `TransformRenderNode.RescaleDemand` supply the two halves; hold the matrix in a non-capturing metadata state and pass their bound methods as the two callbacks. They are not inverses — forward reports the least-scaled axis and backward answers the operator norm, each erring toward more detail — so under an anisotropic or sheared transform a round trip does not return its input.

`RenderScaleContract.Custom` has no backward map and keeps the identity fallback, so a map-topology operation whose density differs from its input's must use the bidirectional `MapInputSupply` rather than a custom resolver.

## Whole-source shader coordinate space

BREAKING CHANGE: a `ShaderDescription.WholeSource` stage is now evaluated over its **complete** output. Its `coord` argument spans `[0, SemanticOutputSize]` and `ShaderExecutionContext.DeviceBounds` / `LogicalOrigin` describe the complete output footprint, even when the renderer only required a sub-region (content that overhangs the frame). Previously `coord` started at the required region's origin while `SemanticOutputSize` still described the complete output, so `coord / iResolution` never reached `1.0` and any absolute anchor — a mirror axis, a tile-grid origin, a pivot — moved by the clipped-off overhang.

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

BREAKING CHANGE: the apron `ImmediateCanvas.PushFilterLayer` opens around a directly replayed Skia filter is derived from the transformed basis **area**, not from the transformed basis lengths. Every edge of the content now sits exactly one device pixel inside the layer whatever basis the canvas carries, so content under a sheared transform — a `SkewTransform`, or any transform group that composes one — renders differently: its layer is wider and keeps antialiased coverage that used to be clipped away.

`Drawable.Render` pushes the drawable transform outside `PushFilterEffect`, so a shear is live on the destination canvas whenever the executor replays a built-in Skia filter chain onto it. Inflating the bounds by `dx` along x moves a vertical edge perpendicular to itself by `dx * |det| / devicePerY` device pixels, not by `dx * devicePerX`; the two agree only when the basis is orthogonal, and a shear drives `|det|` below the product of the basis lengths. The apron each axis needs is therefore the **other** axis's basis length over the determinant. Measured on the basis an 80 degree `SkewTransform` produces at output scale 2 — rows `(2, 0)` and `(1.134, 0.2)` — the previous apron bought 0.174 device pixels instead of one, and a 100 x 6 bar under it lost 9.4% of its ink to a blur too small to move a pixel, against 0.35% now.

The visible change is confined to filters whose own margin is smaller than that shortfall. Skia grows a save layer by the image filter's own radius, so a blur of sigma 0.5 logical units or more at output scale 2 already covered the deficit; a near-identity blur, a zero-radius morphology, and any plugin filter with no spatial extent did not.

Unsheared transforms are unchanged bit-for-bit, not merely within rounding: the apron keeps the reciprocal-of-basis-length form whenever the basis rows are orthogonal to within `1e-5` of the product of their lengths. Composing a rotation with an anisotropic scale leaves the rows orthogonal but misses an exactly zero dot product by up to `1e-7` relative, while the shallowest shear that can move a device pixel misses it by `1e-3`, so the split separates float rounding from real shear with three orders of magnitude to spare on each side. This matters because an apron landing on a whole device pixel would otherwise round out to a layer one pixel larger.

A basis that collapses the plane now leaves the bounds uninflated, joining the existing non-finite and non-positive guards. It previously inflated by the reciprocal of its collapsed basis length, which is an arbitrary amount of logical space for content that has no area to preserve coverage for.

## ChromaKey matches its key colour in linear light

BREAKING CHANGE: `ChromaKey` no longer carries its match tolerance as a `1/255` widening of the hue and saturation edges. It tests the pixel against the key colour in premultiplied linear light, within half an 8-bit code per channel plus one half-precision ulp, and treats a match there as a mask of zero whatever the hue and saturation differences say. The hue term is additionally weighted by the smaller of the pixel's and the key's linear chroma, ramping in between one and two linear codes, so hue stops voting where quantization alone could have manufactured it. `Boundary` still controls only how gradually the mask ramps past the threshold.

The tolerance was applied in the wrong colour space. A constant paint colour reaches the shader folded to 8 bits in the destination colour space, and the render targets are linear F16, so the grid the error lands on is linear — but the tolerance sat after `linearToSrgb`, where half a linear code is not a fixed quantity. Near black it spans about ten sRGB levels, roughly forty times the tolerance; near white it spans a fifth of one.

The consequence was that a fill did not key against its own colour. `rgb(20,18,22)` has all three channels round to linear code 2, so the pixel arrives as an exact grey: saturation disagreed with the key by 0.1818 and hue by 0.2500 against a 0.0039 threshold. `rgb(10,40,20)` disagreed by 0.0821, `rgb(5,5,60)` by 0.0835, and even `rgb(60,180,75)` — bright and saturated — by 0.0073, enough to leave 95% of its alpha at `Boundary` 0. Sampling 225 solid fills across the cube, 118 failed to self-key; all 225 now key to zero alpha.

This was never confined to the fused pipeline, and never to rectangles. Only an axis-aligned rectangle gave Skia a full-coverage quad, so an `EllipseShape` or `RoundedRectShape` with the same dark fill self-keyed at the same 0.1818 and 0.0820 residuals well before this branch.

Content that already keyed is unaffected: the tolerated neighbourhood of `rgb(206,92,42)`, `rgb(240,240,250)` and `rgb(12,12,12)` measured identical before and after, level for level. Because the band is tested premultiplied, it is independent of coverage, so the antialiased edge of a keyed shape now keys with its interior; the same property means a pixel faint enough that half a linear code swamps its colour matches any key, which at that alpha is a change of at most a fraction of a percent of coverage.

The chroma gate has one visible consequence beyond the fix. A neutral fill has no hue to compare, so it can no longer be kept out of a key by the hue term alone: with `SaturationRange` widened to 100, a mid grey that a lime key used to leave alone is now removed. At any narrower `SaturationRange` the saturation term still keeps it, as before. This replaces the previous behaviour, where a neutral pixel took the `h = 0` that `rgb2hsv` returns at zero chroma and therefore matched a red key while surviving every other hue — a distinction the pixel did not carry.
