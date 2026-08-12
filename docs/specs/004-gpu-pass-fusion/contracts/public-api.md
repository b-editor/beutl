# Public API Contract

## Namespaces

Public render-node authoring lives in `Beutl.Graphics.Rendering`; shader and geometry authoring also uses `Beutl.Graphics.Effects`.

## Render-node contract

```csharp
public abstract class RenderNode : IDisposable
{
    public bool HasChanges { get; set; }
    public virtual ReadOnlySpan<RenderNode> ChildNodes { get; }
    public abstract void Process(RenderNodeContext context);
}
```

`Process` records work; it does not draw immediately. The context and every fragment or resource token obtained from it are valid only for that invocation.

`HasChanges` is the sole public content-invalidation signal. Set it before the next request whenever any node state that can affect pixels, bounds, hit testing, or recorded topology changes. An invalidation resets that node and its recorded ancestors, but does not mark unchanged `ChildNodes` dirty: an independently reusable child may continue warming or serving its retained output while its parent changes every request. Definitions may be reused across requests; changing the state passed to a call still requires the owning node to report the change. `ChildNodes` reports content dependencies for traversal and revalidation, not disposal ownership.

Authors do not provide runtime identities, structural identifiers, resource cache identities, or resource content counters. The engine derives operation shape from its immutable definition and manages reusable output state internally.

## Fragment handles and publication

`RenderFragmentHandle` is a non-null, transaction-scoped handle. It is returned by recording methods but is not an output until published.

```csharp
public sealed class RenderNodeContext
{
    public IReadOnlyList<RenderFragmentHandle> Inputs { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
    public Rect? TargetDomain { get; }
    public float OutputScale { get; }
    public float MaxWorkingScale { get; }

    public void PassThrough();
    public void Publish(RenderFragmentHandle fragment);
    public void PublishRange(IEnumerable<RenderFragmentHandle> fragments);
    public void PublishMappedInputs(
        Func<RenderFragmentHandle, RenderFragmentHandle> mapper);
    public void PublishMappedInputs<TState>(
        TState state,
        Func<RenderNodeContext, RenderFragmentHandle, TState, RenderFragmentHandle> mapper);
    public void Drop(RenderFragmentHandle fragment);
}
```

`PassThrough` republishes all inputs in order. `PublishMappedInputs` is explicit one-to-one publication: it invokes its mapper once per input in painter order and publishes exactly the returned handle for that input. It produces no output for an empty input list. A mapper may record intermediate fragments, but it must not publish anything itself; nested publication is rejected and the invocation rolls back.

Use `Publish`, `PublishRange`, or `PassThrough` for every other topology: no output, selection, reordering, combining, expansion, nested recording, or placement of target effects. The generic overload enables a non-capturing `static` mapper in allocation-sensitive code.

```csharp
public override void Process(RenderNodeContext context)
{
    context.PublishMappedInputs(
        _opacity,
        static (current, input, opacity) => current.Opacity(input, opacity));
}
```

The context validates publication ownership, topology, resource transfer, and callback completion as one transaction. An exception leaves no partial recording and releases transferred resources best-effort.

## Immutable definitions and per-recording calls

Public callbacks are authored in two layers:

- A `*Definition<TState>` fixes callback code, metadata contracts, resource-slot schema, and operation kind.
- `.Call(state, bindings)` supplies the values and request-scoped resource tokens for one recording.

Definitions are immutable. Reuse a static/shared definition when its callback and metadata are fixed to avoid needless allocation. Equivalent definitions recreated later still reuse the same internal plan because the engine derives equivalence from the callback and declared metadata, not object lifetime. Use a distinct immutable definition when those fixed characteristics differ. Put per-recording values only in call state and bindings. This keeps an operation's schema stable without requiring application-provided identity values.

```csharp
public RenderFragmentHandle OpaqueSource<TState>(OpaqueRenderCall<TState> call);
public RenderFragmentHandle OpaqueMap<TState>(RenderFragmentHandle input, OpaqueRenderCall<TState> call);
public RenderFragmentHandle OpaqueCombine<TState>(IReadOnlyList<RenderFragmentHandle> inputs, OpaqueRenderCall<TState> call);
public RenderFragmentHandle OpaqueExpand<TState>(IReadOnlyList<RenderFragmentHandle> inputs, OpaqueRenderCall<TState> call);

public RenderFragmentHandle TargetScope<TState>(RenderFragmentHandle input, TargetScopeCall<TState> call);
public RenderFragmentHandle TargetCommand<TState>(IReadOnlyList<RenderFragmentHandle> inputs, TargetCommandCall<TState> call);
public RenderFragmentHandle RawTargetScope<TState>(RenderFragmentHandle input, RawTargetScopeCall<TState> call);
public RenderFragmentHandle RawTargetCommand<TState>(RawTargetCommandCall<TState> call);
```

`OpaqueRenderDefinition<TState>.Create` declares source, map, combine, or expansion metadata through its bounds, hit-test, cardinality, scale, and optional input-readback contracts. `TargetScopeDefinition<TState>.Create` and `TargetCommandDefinition<TState>.Create` declare their guarded target behavior. `RawTargetScopeDefinition<TState>.Create` and `RawTargetCommandDefinition<TState>.Create` declare the same binding schema while preserving a deliberately request-local canvas boundary.

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

The callback for a guarded operation receives a session that addresses resources through the declared `RenderResourceSlot<T>`. It cannot use an arbitrary request token that was not declared by the definition.

## Resources and lifetimes

```csharp
public RenderResource<T> Own<T>(T resource) where T : class, IDisposable;
public RenderResource<T> Borrow<T>(T resource) where T : class;

public sealed class RenderResourceSlot<T> where T : class
{
    public RenderResourceBinding Bind(RenderResource<T> resource);
}
```

`Own` transfers disposal responsibility to the active request family. `Borrow` retains caller ownership and requires the raw object to remain usable for the request. Both return opaque request-scoped tokens; neither is a public output-reuse identity.

Each definition declares all of its slots in `resources:`. Each call binds every declared slot exactly once with `slot.Bind(token)`, and may not bind an undeclared or differently typed token. `RenderResourceBinding` is intentionally created only by a typed slot.

Guarded opaque, geometry, and target callbacks lease a resource through their slot:

```csharp
static (session, state) => session.UseResource(
    s_brush,
    brush => Draw(session, brush, state.Opacity))
```

## Raw target calls

Raw calls are for existing behavior that must access an unguarded canvas and cannot be represented with typed operations. They are opaque external work and never become persistently reusable output. They still use a generic immutable definition and typed binding schema.

The raw session intentionally receives resources by token. Put the token in per-call state and bind that same token to the declared slot, so the declaration is validated before execution.

```csharp
private sealed record BackdropState(RenderResource<IBackdrop> Backdrop);

private static readonly RenderResourceSlot<IBackdrop> s_backdrop = new();

private static readonly RawTargetCommandDefinition<BackdropState> s_backdropCommand =
    RawTargetCommandDefinition<BackdropState>.Create(
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
        s_backdropCommand.Call(
            new BackdropState(backdrop),
            [s_backdrop.Bind(backdrop)])));
}
```

Use a guarded target definition when the operation's region and access can be declared. Use raw definitions only for the unavoidable external-canvas boundary.

## Shader and geometry calls

Shader source, entry-point kind, fixed bounds behavior, uniform schema, and resource-slot schema belong to `ShaderDefinition<TState>`. Per-recording uniform values and tokens belong to `ShaderCall<TState>`.

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

Use `ShaderDefinition<TState>.CurrentPixel` for a `half4 apply(half4 color)` stage. Use `.WholeSource` for a `half4 main(float2 coord)` stage with its required `uniform shader src;` input and a fixed `RenderBoundsContract`. `ShaderDefinitionBuilder<TState>.Uniform` maps state to canonical uniforms. Its value providers, custom binders, and `.Resource` binders must be non-capturing `static` callbacks, so every changing value flows through `TState` and is covered by the owning node's `HasChanges` update. `.Resource` declares a typed child-shader slot and coordinate space; bind its token with that slot in `.Call`.

`GeometryDefinition<TState>.Create(render, bounds, hitTest, requiresReadback, resources)` follows the same model and produces `GeometryCall<TState>` for `RenderNodeContext.Geometry`. Geometry callbacks lease declared tokens through slots.

`FilterEffectContext` accepts the same public calls:

```csharp
context.Shader(s_tint.Call(new TintState(_amount)));
context.Geometry(s_geometry.Call(new GeometryState(_radius)));
```

## Metadata contracts

Definitions use `RenderBoundsContract`, `RenderHitTestContract`, `RenderScaleContract`, `RenderValueCardinality`, and, where applicable, `TargetRegion`, `TargetAccess`, `RenderInputReadback`, device-grid sensitivity, and device-grid mapping. Metadata callbacks must be deterministic, side-effect-free, and non-capturing. The engine derives their operation-shape fingerprint from the fixed callback and contract; author code supplies no manual identifier.

`RenderScaleContract.MapInputSupply` takes only a pure `Func<EffectiveScale, EffectiveScale>` mapping for an element-wise one-input operation. It may be evaluated again while resolving symbolic upstream metadata.

## Recording rules

- `Opacity`, `Blend`, `OpacityMask`, shader calls, geometry calls, opaque maps, and target scopes return unpublished handles.
- Opaque source, map, combine, and expansion calls must match the topology declared by their definition.
- Target commands are ordinary effectful handles; publish them at the intended painter position.
- `RawTargetScope` replays its input exactly once. `RawTargetCommand` has no logical value input.
- `RecordNode` and `RecordSubtree` record nested work in the active request; no transaction-scoped handle may escape the call that produced it.

## Cache and failure rules

The renderer controls retained output and resource lifetime. An author invalidates node content only by setting `HasChanges`; no context method opts a recording out of reuse and no token carries public content metadata. Raw target work remains request-local by definition.

If `Process` or a deferred callback fails, the engine preserves the primary failure, releases request-owned resources best-effort, and does not publish a partial result.
