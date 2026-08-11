# Breaking Changes and Migration Contract

## Summary

BREAKING CHANGE: render-node work is now recorded through `void RenderNode.Process(RenderNodeContext)`. Nodes publish transaction-scoped fragment handles; they do not receive an immediate canvas, return an operation, or control retained output state directly.

BREAKING CHANGE: public callback authoring now uses immutable `*Definition<TState>` objects and per-recording `.Call(state, bindings)` values. The former public callback-record construction path is no longer an authoring API.

BREAKING CHANGE: `RenderNode.HasChanges` is the only public content-invalidation signal. A node sets it when its pixel-, metadata-, or topology-affecting state changes. No public API accepts caller-supplied cache identity, resource content metadata, or a manual operation fingerprint.

## Core node migration

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

## Publication migration

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

## Callback migration

### Guarded opaque work

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

### Target work

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

### Resources

Replace keyed or string-named registration with the lifetime-only APIs:

```csharp
RenderResource<Texture> texture = context.Borrow(_texture);
RenderResource<TemporarySurface> scratch = context.Own(new TemporarySurface());
RenderResourceBinding binding = s_texture.Bind(texture);
```

`Borrow` leaves ownership with the caller. `Own` transfers a disposable object to the request family. Neither method accepts identity or content arguments. `RenderResourceBinding` has no public constructor and binding names are not part of the API. A definition declares `RenderResourceSlot<T>` values in `resources:` and its call binds each one exactly once.

### Shader and geometry work

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

## Metadata and scale migration

Bounds, hit testing, scale, cardinality, input readback, target access, and device-grid behavior are fixed definition metadata. Their callbacks must be deterministic, side-effect-free, and non-capturing.

For a one-input element-wise density transform, use:

```csharp
RenderScaleContract scale = RenderScaleContract.MapInputSupply(
    static inputSupply => inputSupply);
```

The map is reevaluated when required to resolve symbolic upstream metadata. Source, capture, combination, and expansion work must choose their own valid scale contract.

## Output reuse and failure behavior

The renderer decides whether recorded output is retained. Author code must only report changed node content through `HasChanges`; it cannot force, suppress, seed, or identify retained output. Raw target work is never persistently reusable.

Every `Process` invocation is transactional. An exception from recording or deferred execution preserves the primary failure, releases request-owned values best-effort, and yields no partial output.
