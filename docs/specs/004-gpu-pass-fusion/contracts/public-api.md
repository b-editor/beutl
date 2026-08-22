# Public API Contract

## Namespaces

Public render-node authoring lives in `Beutl.Graphics.Rendering`; shader and geometry authoring also uses `Beutl.Graphics.Effects`.

## Render-node contract

```csharp
public abstract class RenderNode : IDisposable
{
    public bool IsDisposed { get; }
    public bool HasChanges { get; set; }
    public virtual ReadOnlySpan<RenderNode> ChildNodes { get; }
    public abstract void Process(RenderNodeContext context);
    protected virtual void OnDispose(bool disposing);
}
```

`Process` records work; it does not draw immediately. The context and every fragment handle obtained from it are valid only for that invocation. Resource tokens from `Own`/`Borrow` are scoped to the active request family instead, so they remain declarable in nested recordings within the same request and are rejected once released. `Dispose` is not virtual; release node-owned state by overriding `OnDispose`, which both `Dispose` and the finalizer route through.

`HasChanges` is the sole public content-invalidation signal. Set it before the next request whenever any node state that can affect pixels, bounds, hit testing, or recorded topology changes. An invalidation resets that node and its recorded ancestors, but does not mark unchanged `ChildNodes` dirty: an independently reusable child may continue warming or serving its retained output while its parent changes every request. Definitions may be reused across requests; changing the state passed to a call still requires the owning node to report the change. `ChildNodes` reports content dependencies for traversal and revalidation, not disposal ownership. A node that discovers what it records through only while processing, and so cannot hold a stable span, leaves `ChildNodes` empty; traversal and revalidation then stop at that node, so it must take itself out of the cache for that recording with `context.DisableRenderCache()`.

Authors do not provide runtime identities, structural identifiers, resource cache identities, or resource content counters. The engine derives operation shape from its immutable definition and manages reusable output state internally.

## Fragment handles and publication

`RenderFragmentHandle` is a non-null, transaction-scoped handle. It is returned by recording methods but is not an output until published. It also exposes the recorded metadata an author may need in order to decide what to record next.

```csharp
public sealed class RenderFragmentHandle
{
    public RenderValueCardinality ValueCardinality { get; }
    public bool ContributesValuesToTarget { get; }
    public bool CanBeUsedAsValueInput { get; }

    public bool TryGetMetadata(out RenderFragmentMetadata metadata);
    public bool TryHitTest(Point point, out bool result);
}

public readonly record struct RenderFragmentMetadata(Rect Bounds, EffectiveScale EffectiveScale);
```

`TryGetMetadata` and `TryHitTest` return `false` instead of throwing when the fragment's bounds still depend on an unresolved owning target domain, which is a legitimate recording state rather than an error. Neither executes deferred work or resolves graph-wide regions of interest.

```csharp
public sealed class RenderNodeContext
{
    public IReadOnlyList<RenderFragmentHandle> Inputs { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
    public Rect? TargetDomain { get; }
    public float OutputScale { get; }
    public float MaxWorkingScale { get; }
    public bool IsRenderCacheEnabled { get; }

    public bool TryCalculateInputBounds(out Rect bounds);
    public void DisableRenderCache();

    public void PassThrough();
    public void Publish(RenderFragmentHandle fragment);
    public void PublishRange(IEnumerable<RenderFragmentHandle> fragments);
    public void PublishMappedInputs(
        Func<RenderFragmentHandle, RenderFragmentHandle> mapper);
    public void PublishMappedInputs<TState>(
        TState state,
        Func<RenderNodeContext, RenderFragmentHandle, TState, RenderFragmentHandle> mapper);
    public void Drop(RenderFragmentHandle fragment);

    public RenderFragmentHandle ContributeValues(RenderFragmentHandle input);
    public RenderFragmentHandle Layer(
        IReadOnlyList<RenderFragmentHandle> inputs,
        Rect domain,
        bool domainIsQueryFootprint = false);
    public RenderFragmentHandle OwningTargetLayer(IReadOnlyList<RenderFragmentHandle> inputs);
    public RenderFragmentHandle TargetLayerScope(
        IReadOnlyList<RenderFragmentHandle> inputs,
        TargetRegion region);
    public RenderFragmentHandle MaterializedInput(MaterializedInputDescription description);
    public RenderFragmentHandle TargetCapture(TargetCaptureDescription description);
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

`ContributeValues` wraps a value-eligible fragment so its values composite into the target when published. `Layer` records a finite off-screen layer over an explicit logical domain and returns its composited single value; it is the explicit boundary that turns a mixed painter sequence into a value. `OwningTargetLayer` is the same boundary for a recording that has no finite domain available yet: it stays symbolic until the owning target domain resolves, and graph finalization rejects it if no enclosing scope or request supplies one. `TargetLayerScope` scopes ordered target work to a symbolic `TargetRegion` and stays effectful rather than becoming a value. `MaterializedInput` adopts a target that is already materialized as a value without copying it, and `TargetCapture` records a declared capture of the active target. None of them publishes automatically.

`TryCalculateInputBounds` unions the current input bounds from concrete recording metadata. It returns `false` when any input still depends on an unresolved owning target domain, and, like the handle-level probes, executes no deferred work.

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

public abstract class RenderResourceSlot { }

public sealed class RenderResourceSlot<T> : RenderResourceSlot
    where T : class
{
    public RenderResourceSlot();
    public RenderResourceBinding Bind(RenderResource<T> resource);
}
```

`Own` transfers disposal responsibility to the active request family. `Borrow` retains caller ownership and requires the raw object to remain usable for the request. Both return opaque request-scoped tokens; neither is a public output-reuse identity.

Each definition declares all of its slots in `resources:`. A definition's `resources:` list is an `IEnumerable<RenderResourceSlot>` of the non-generic base, which is how slots of different resource types travel together; the base is not otherwise part of the authoring surface. Each call binds every declared slot exactly once with `slot.Bind(token)`, and may not bind an undeclared or differently typed token. `RenderResourceBinding` is intentionally created only by a typed slot.

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

A whole-source stage that enlarges what it samples also declares `inputDemand`: a `RenderInputDemandContract` mapping the stage's resolved output demand to the demand it places on `src`. Without it the output demand reaches `src` unchanged, so an unbounded or vector source asked for 1x output rasterizes at 1x and is then stretched. The default leaves demand unchanged, which is correct only for a stage that samples at the density its own consumer asked for.

Several definitions over one source can share its parsed form: `SkslSource.CurrentPixel(source)` and `SkslSource.WholeSource(source)` validate the text once, and the matching `ShaderDefinition<TState>` factories accept the result in place of a raw string. The parsed source is immutable and carries its `Kind` and `IdentityHash`; passing one of the wrong kind is rejected where the definition is declared.

`GeometryDefinition<TState>.Create(render, bounds, hitTest, requiresReadback, resources)` follows the same model and produces `GeometryCall<TState>` for `RenderNodeContext.Geometry`. Geometry callbacks lease declared tokens through slots.

`FilterEffectContext` accepts the same public calls:

```csharp
context.Shader(s_tint.Call(new TintState(_amount)));
context.Geometry(s_geometry.Call(new GeometryState(_radius)));
```

`FilterEffectContext.TryGetWorkingScale(out float)` probes whether the nominal effect-input density is concrete. The `WorkingScale` property throws while that density is unresolved or branch-dependent, so use the probe during `ApplyTo` and defer device-pixel decisions to an execution-time shader, geometry, or custom-effect callback when it returns `false`.

## Metadata contracts

Definitions use `RenderBoundsContract`, `RenderHitTestContract`, `RenderScaleContract`, `RenderValueCardinality`, and, where applicable, `TargetRegion`, `TargetAccess`, `RenderInputReadback`, device-grid sensitivity, and device-grid mapping. Metadata callbacks must be deterministic, side-effect-free, and non-capturing. The engine derives their operation-shape fingerprint from the fixed callback and contract; author code supplies no manual identifier.

`RenderScaleContract.MapInputSupply` declares both directions of the density relationship of an element-wise one-input operation: a pure `Func<EffectiveScale, EffectiveScale>` mapping the input supply forward to the output supply, and a second pure `Func<EffectiveScale, EffectiveScale>` mapping a backward output demand to the input demand that satisfies it. It is the right default for any one-input density map. `RenderScaleContract.MapInputSupplyPreservingDemand` declares the forward callback alone and leaves backward demand unchanged; its name states its precondition, which is that the operation consumes its input at the density its own consumer demands. Either callback may be evaluated again while resolving symbolic upstream metadata. An operation that resamples must use `MapInputSupply`, or an unbounded input materializes at the operation's own output demand instead of the density the operation consumes. For an affine density map, the public statics `TransformRenderNode.RescaleDensity` and `TransformRenderNode.RescaleDemand` supply the two callbacks; they are the two halves of one relationship rather than inverses of each other, because each errs toward more detail through a different axis.

`RenderInputDemandContract` carries a backward demand where a `RenderScaleContract` cannot. `MapOutputDemandToInput` maps one input; `MapOutputDemandPerInput` maps each input separately by its zero-based index, which is what a combine or an expand needs when it resamples its inputs asymmetrically — enlarging the first while passing the second through. `OpaqueRenderDefinition<TState>.Create` accepts one as `inputDemand`, and only a combine or an expand may declare it: a one-input map carries demand back through `RenderScaleContract.MapInputSupply` instead, and a source has no input to demand from. `ShaderDefinition<TState>.WholeSource` accepts one for the same reason, because a whole-source stage resolves its own supply from the working scale and has no forward map to declare.

`RenderHitTestContract.FromSlot` builds a hit test that reads the resource a call bound to a `RenderResourceSlot<T>`, resolved against that call's bindings rather than captured by the definition. `RenderHitTestContext.UseResource` exposes the same resolution to a `Custom` callback. A hit-test callback still may not capture a `RenderResource` itself, because the definition holding it outlives every call.

## Recording rules

- `Opacity`, `Blend`, `OpacityMask`, `ContributeValues`, `Layer`, `OwningTargetLayer`, `TargetLayerScope`, `MaterializedInput`, `TargetCapture`, shader calls, geometry calls, opaque source, map, combine, and expansion calls, and target scopes return unpublished handles.
- Opaque source, map, combine, and expansion calls must match the topology declared by their definition.
- Target commands are ordinary effectful handles; publish them at the intended painter position.
- A fragment may be published or consumed more than once only when it is value-eligible. Publishing or consuming an effectful fragment — a target command or scope, or any wrapper built over one — more than once is rejected and the recording rolls back.
- `RawTargetScope` replays its input exactly once. `RawTargetCommand` has no logical value input.
- `RecordNode` and `RecordSubtree` record nested work in the active request; no transaction-scoped handle may escape the call that produced it.

## Cache and failure rules

The renderer controls retained output and resource lifetime. An author invalidates node content only by setting `HasChanges`; no context method opts a recording out of reuse and no token carries public content metadata. Raw target work remains request-local by definition.

`ContainerRenderNode` sets `HasChanges` itself when its children change — `AddChild`, `RemoveChild`, `RemoveRange`, `SetChild`, and `BringFrom` — because replacing a child changes what the container composes and the container's own state does not otherwise record it. `SetChild` with the child already at that index is a no-op. A container assembled and then rendered is therefore dirty on its first frame, which is one frame before its cache can warm.

*Amended.* The reuse opt-out this contract withheld was reinstated during implementation: `RenderNodeContext.DisableRenderCache()` monotonically removes the current transaction from persistent caching, and `IsRenderCacheEnabled` reports that state. It was published because a node that records a child it cannot list in `ChildNodes` has no other way to stay correct — the cache cannot observe a change reported only by that unlisted child. It is not a second invalidation signal: `HasChanges` remains the only way to invalidate a cached node. The migration is in [breaking-changes.md](breaking-changes.md), which carries the current contract. The rest of the paragraph is unchanged: no token carries public content metadata, and raw target work stays request-local.

If `Process` or a deferred callback fails, the engine preserves the primary failure, releases request-owned resources best-effort, and does not publish a partial result.
