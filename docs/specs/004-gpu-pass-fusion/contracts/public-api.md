# Public API Contract

## Namespaces

Public render-node authoring lives in `Beutl.Graphics.Rendering`; shader and geometry authoring also uses `Beutl.Graphics.Effects`.

## Render-node contract

```csharp
public abstract class RenderNode : IDisposable
{
    public bool IsDisposed { get; }
    public bool HasChanges { get; }
    public void MarkChanged();
    public virtual ReadOnlySpan<RenderNode> ChildNodes { get; }
    public abstract void Process(RenderNodeContext context);
    protected virtual void OnDispose(bool disposing);
}
```

`Process` records work; it does not draw immediately. The context and every fragment handle obtained from it are valid only for that invocation. Resource tokens from `Own`/`Borrow` are scoped to the active request family instead, so they remain declarable in nested recordings within the same request and are rejected once released. `Dispose` is not virtual; release node-owned state by overriding `OnDispose`, which both `Dispose` and the finalizer route through.

`HasChanges` is the sole public content-invalidation signal, and it is read-only: `MarkChanged()` raises it and the renderer clears it as part of consuming a recording. Call `MarkChanged()` before the next request whenever any node state that can affect pixels, bounds, hit testing, or recorded topology changes. An invalidation resets that node and its recorded ancestors, but does not mark unchanged `ChildNodes` dirty: an independently reusable child may continue warming or serving its retained output while its parent changes every request. Re-recording an operation with different values is not itself an invalidation signal: a node whose recording may be replayed rather than re-run has to report the change that made the values different. `ChildNodes` reports content dependencies for traversal and revalidation, not disposal ownership. A node that discovers what it records through only while processing, and so cannot hold a stable span, leaves `ChildNodes` empty; traversal and revalidation then stop at that node, so it must take itself out of the cache for that recording with `context.DisableRenderCache()`.

Authors do not provide runtime identities, structural identifiers, resource cache identities, or resource content counters. The engine derives operation shape from the immutable description an operation was recorded from and manages reusable output state internally.

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

## Operation descriptions

Every callback-defined operation is recorded through one immutable *description*. A description states, in a single expression, everything the recorded operation is made of: the execution callback, the state that callback reads, the metadata contracts the planner reads, the resource slots the operation declares, and the bindings that fill those slots. Build it where you record it and hand it straight to the matching context method.

```csharp
public RenderFragmentHandle OpaqueSource(OpaqueRenderDescription description);
public RenderFragmentHandle OpaqueMap(RenderFragmentHandle input, OpaqueRenderDescription description);
public RenderFragmentHandle OpaqueCombine(IReadOnlyList<RenderFragmentHandle> inputs, OpaqueRenderDescription description);
public RenderFragmentHandle OpaqueExpand(IReadOnlyList<RenderFragmentHandle> inputs, OpaqueRenderDescription description);

public RenderFragmentHandle TargetScope(RenderFragmentHandle input, TargetScopeDescription description);
public RenderFragmentHandle TargetCommand(IReadOnlyList<RenderFragmentHandle> inputs, TargetCommandDescription description);
public RenderFragmentHandle RawTargetScope(RenderFragmentHandle input, RawTargetScopeDescription description);
public RenderFragmentHandle RawTargetCommand(RawTargetCommandDescription description);
```

Rebuilding a description each frame does not cost a plan. The engine keys a compiled plan on the *shape* of the work — the callback's method and the declared contracts — and never on the values a recording carries, so a description built inside `Process` compiles the same plan every request. Hoist only what is genuinely fixed and worth not rebuilding: a `RenderResourceSlot<T>`, the `RenderResourceSlot[]` a description declares, a `RenderHitTestContract` over a slot, a parsed `SkslSource`. The values that change belong in the `state` argument, where the callback reaches them through its `TState` parameter, and in the bindings.

`RenderNode.PrepareForRequest(RenderNodePreparation)` runs on every request before that node's children are recorded. Recording walks children first, so a node whose children depend on the request - one that records a nested graph at the request's density - cannot rebuild them from `Process`, where they are already recorded. `RenderNodePreparation` carries only what is settled before any fragment exists: the request's output scale, working-scale ceiling, intent, purpose, and target domain. It runs on every request, so an override that changes nothing must cost nothing.

`OpaqueRenderDescription.Create` declares source, map, combine, or expansion metadata through its bounds, hit-test, cardinality, scale, and optional input-readback and input-demand contracts. `TargetScopeDescription.Create` and `TargetCommandDescription.Create` declare their guarded target behavior. A scope also declares the space its replay transform lives in through `RenderScopeTransformSpace`: the default `AmbientTarget` is a transform defined against the surrounding target transform, which already carries the scope's own scale, while `InputLogical` states that the transform is expressed in the input's own coordinates so the declared `RenderScaleContract`'s backward map carries an output demand back to that input. Declaring `InputLogical` for a scope that in fact appends to the destination matrix rasterizes the input enlarged and then draws it enlarged again. `RawTargetScopeDescription.Create` and `RawTargetCommandDescription.Create` declare the same binding schema while preserving a deliberately request-local canvas boundary.

```csharp
private sealed record DrawState(float Opacity);

private static readonly RenderResourceSlot<Brush.Resource> s_brush = new();
private static readonly RenderResourceSlot[] s_slots = [s_brush];

public override void Process(RenderNodeContext context)
{
    RenderResource<Brush.Resource> brush = context.Borrow(_brush);

    context.PublishMappedInputs(
        OpaqueRenderDescription.Create(
            new DrawState(_opacity),
            static (session, state) => session.UseResource(
                s_brush,
                brush => Draw(session, brush, state.Opacity)),
            OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            RenderScaleContract.PreserveInputSupply,
            resources: [s_brush.Bind(brush)],
            slots: s_slots),
        static (current, input, description) => current.OpaqueMap(input, description));
}
```

The callback for a guarded operation receives a session that addresses resources through the declared `RenderResourceSlot<T>`. It cannot use an arbitrary request token the description did not declare.

## Painted sources

A source that paints itself through an `ImmediateCanvas` has no description type and is recorded directly:

```csharp
public RenderFragmentHandle PaintedSource<TState>(
    TState state,
    PaintedSourceDraw<TState> draw,
    Brush.Resource? fill,
    Pen.Resource? pen,
    Rect outputBounds,
    RenderHitTestContract hitTest,
    RenderScaleContract scale,
    RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
    bool supportsDirectDstOut = true,
    IEnumerable<RenderResourceBinding>? bindings = null,
    IEnumerable<RenderResourceSlot>? slots = null,
    Thickness rasterOutset = default)
    where TState : notnull;

public delegate void PaintedSourceDraw<TState>(
    ImmediateCanvas canvas,
    Brush.Resource? fill,
    Pen.Resource? pen,
    TState state);
```

It cannot be a description, because it borrows its fill and its pen from the recording transaction and whether either of them paints a drawable brush decides the plan key while the call is being made. The `fill` and `pen` reach the callback as ordinary values; everything else the source reads arrives as `bindings`, under exactly the rules below, because a bare token is addressable by nothing and a declared hit test could never find it again. Compute `outputBounds` with `PenHelper.GetBounds(Rect, Pen.Resource)` so a stroked source follows the same stroke-alignment convention as the built-in shape nodes, and use `rasterOutset` for filtering or anti-aliasing that spills past those bounds.

```csharp
private static readonly RenderResourceSlot<Geometry.Resource> s_geometry = new();
private static readonly RenderResourceSlot[] s_geometrySlots = [s_geometry];

public override void Process(RenderNodeContext context)
{
    Geometry.Resource geometry = _geometry;
    Brush.Resource? fill = Fill?.Resource;
    Pen.Resource? pen = Pen?.Resource;
    RenderResource<Geometry.Resource> resource = context.Borrow(geometry);

    context.Publish(context.PaintedSource(
        geometry,
        static (canvas, fill, pen, state) => canvas.DrawGeometry(state, fill, pen),
        fill,
        pen,
        PenHelper.GetBounds(geometry.Bounds, pen),
        RenderHitTestContract.OutputBounds,
        RenderScaleContract.Vector,
        bindings: [s_geometry.Bind(resource)],
        slots: s_geometrySlots));
}
```

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

Every factory takes its slots and its bindings together, and always checks one against the other. `slots:` is an `IEnumerable<RenderResourceSlot>` of the non-generic base, which is how slots of different resource types travel together; the base is not otherwise part of the authoring surface. `resources:` — spelled `bindings:` on `PaintedSource` and `hitTestResources:` on the shader factories, where the bindings serve only a declared hit test — is a sequence of `RenderResourceBinding`, each produced by `slot.Bind(token)`. It must bind every declared slot exactly once and may not bind an undeclared or differently typed token.

Declaring the slots is what makes that check possible, and it is also what orders the bindings: they are reordered into the `slots:` order before the operation is recorded, so the order the author happened to write them in never reaches the plan the recording compiles. Omitting `slots:` therefore declares no slots rather than skipping the check — binding a resource without declaring its slot is refused. `RenderResourceBinding` is intentionally created only by a typed slot.

Guarded opaque, geometry, and target callbacks lease a resource through their slot:

```csharp
static (session, state) => session.UseResource(
    s_brush,
    brush => Draw(session, brush, state.Opacity))
```

## Raw target work

Raw work is for existing behavior that must access an unguarded canvas and cannot be represented with typed operations. It is opaque external work and never becomes persistently reusable output. It still declares typed slots and binds them like every other description.

Raw sessions accept a resource either way: `UseResource(slot, use)` addresses the binding the description declared, and `UseResource(token, use)` addresses a token directly. Prefer the slot from a state-passing callback, and put the token in state only where the callback needs it by identity — binding it to a declared slot as well is what gets the declaration checked before execution.

```csharp
private readonly record struct BackdropState(RenderResource<IBackdrop> Resource);

private static readonly RenderResourceSlot<IBackdrop> s_backdrop = new();
private static readonly RenderResourceSlot[] s_slots = [s_backdrop];

public override void Process(RenderNodeContext context)
{
    RenderResource<IBackdrop> backdrop = context.Borrow(_backdrop);

    context.Publish(context.RawTargetCommand(
        RawTargetCommandDescription.Create(
            new BackdropState(backdrop),
            static (session, state) => session.UseResource(
                state.Resource,
                value => value.Draw(session.Canvas)),
            queryBounds: _bounds,
            hitTest: RenderHitTestContract.OutputBounds,
            resources: [s_backdrop.Bind(backdrop)],
            slots: s_slots)));
}
```

Raw work is unreusable because its canvas is opaque to the renderer, not because it cannot pass state. `RawTargetScopeDescription.Create<TState>` and `RawTargetCommandDescription.Create<TState>` both take state, and what that buys is the identity the planner keys the *shape* of the work by: a static callback recorded twice is one plan. Use a guarded target description when the operation's region and access can be declared, and raw work only for the unavoidable external-canvas boundary.

## Shader and geometry stages

A shader stage is a `ShaderDescription`, recorded through `RenderNodeContext.Shader(input, description)` or `FilterEffectContext.Shader(description)`.

```csharp
private static readonly SkslSource s_tintSource = SkslSource.CurrentPixel(
    """
    uniform float amount;
    half4 apply(half4 color) {
        return half4(color.rgb * amount, color.a);
    }
    """);

public override void Process(RenderNodeContext context)
{
    float amount = _amount;

    context.PublishMappedInputs(
        ShaderDescription.CurrentPixel(
            s_tintSource,
            bindings => bindings.Uniform("amount", amount)),
        static (current, input, description) => current.Shader(input, description));
}
```

Use `ShaderDescription.CurrentPixel` for a `half4 apply(half4 color)` stage. Use `.WholeSource` for a `half4 main(float2 coord)` stage with its required `uniform shader src;` input and a fixed `RenderBoundsContract`.

The `bindings` argument is an `Action<ShaderBindingBuilder>` invoked immediately, while the description is being constructed, and never retained — so it may close over the values this recording needs, as the example does. `ShaderBindingBuilder.Uniform(name, value)` copies a canonical scalar, vector, or matrix value straight into the description. Two members do retain a callback: `Uniform(name, value, bind)`, whose binder produces the value at execution time from a `ShaderExecutionContext`, and `Resource(name, token, coordinateSpace, bind)`, which declares a typed child-shader slot and its coordinate space. Those binders may read nothing beyond their arguments and the `RenderNode` declaring them, so every other changing value has to arrive as the `value` beside them; either way what they read is covered by the owning node's `HasChanges` update.

A whole-source stage that relocates its content also declares `hitTest`: a `RenderHitTestContract` for the pixels it produces. The stage's `bounds` say where the output lands and its SkSL puts it there, but only the author knows the inverse of that mapping, so without a declaration the hit test is forwarded to the input unchanged and is answered at a point the moved content no longer covers — missing the visible pixels and hitting the vacated ones. The parameter is optional and defaults to that forwarding, which is exactly right for a stage that leaves its content where it found it; passing an uninitialized `default(RenderHitTestContract)` is rejected where the description is created. `CurrentPixel` has no equivalent: it maps one resolved pixel in place and can never relocate anything.

A whole-source stage that enlarges what it samples also declares `inputDemand`: a `RenderInputDemandContract` mapping the stage's resolved output demand to the demand it places on `src`. Without it the output demand reaches `src` unchanged, so an unbounded or vector source asked for 1x output rasterizes at 1x and is then stretched. The default leaves demand unchanged, which is correct only for a stage that samples at the density its own consumer asked for.

Several descriptions over one source can share its parsed form: `SkslSource.CurrentPixel(source)` and `SkslSource.WholeSource(source)` validate the text once, and the matching `ShaderDescription` factories accept the result in place of a raw string. Hold that parsed source in a `static readonly` field, as the example does, so recording a stage does not re-tokenize it. The parsed source is immutable and carries its `Kind` and `IdentityHash`; passing one of the wrong kind is rejected where the description is created.

`GeometryDescription.Create(state, render, bounds, hitTest, requiresReadback, inputDemand, resources, slots)` follows the same model for `RenderNodeContext.Geometry(input, description)` and `FilterEffectContext.Geometry(description)`. Geometry callbacks lease declared tokens through slots.

`FilterEffectContext` takes the same descriptions, without an input handle — the effect chain supplies the input:

```csharp
public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
{
    var state = (Resource)resource;
    context.Shader(ShaderDescription.CurrentPixel(
        s_tintSource,
        bindings => bindings.Uniform("amount", state.Amount)));
}
```

`FilterEffectContext.TryGetWorkingScale(out float)` probes whether the nominal effect-input density is concrete. The `WorkingScale` property throws while that density is unresolved or branch-dependent, so use the probe during `ApplyTo` and defer device-pixel decisions to an execution-time shader, geometry, or custom-effect callback when it returns `false`.

## Metadata contracts

Descriptions use `RenderBoundsContract`, `RenderHitTestContract`, `RenderScaleContract`, `RenderValueCardinality`, and, where applicable, `TargetRegion`, `TargetAccess`, `RenderInputReadback`, device-grid sensitivity, and device-grid mapping. Metadata callbacks must be deterministic and side-effect-free. The engine derives their operation-shape fingerprint from the callback's method and the declared contract; author code supplies no manual identifier.

A metadata callback may read the `RenderNode` that declares it. A node writing `RenderBoundsContract.Create(r => r.Translate(new Vector(Offset, 0)), r => r.Translate(new Vector(-Offset, 0)))` reads its own `Offset` and states the mapping in terms of what the node is set to, rather than threading that value through `TState`. Because the fingerprint is the callback's method, two nodes of one type reading different values of their own share one compiled plan, exactly as two calls of one `static` callback do. What the callback reads is request data: the plan is re-run over it, and a recorded answer that no longer holds when graph-wide metadata resolution asks for it again fails the request rather than silently winning. Everything else a captured value may not be is unchanged — a callback may still not *be* a `RenderResource`, a `RenderNodeContext`, a request, a recorded graph, a resource slot or registration, a fragment handle, an execution session, canvas, or writer, or a mutable collection.

`RenderScaleContract.MapInputSupply` declares both directions of the density relationship of an element-wise one-input operation: a pure `Func<EffectiveScale, EffectiveScale>` mapping the input supply forward to the output supply, and a second pure `Func<EffectiveScale, EffectiveScale>` mapping a backward output demand to the input demand that satisfies it. It is the right default for any one-input density map. `RenderScaleContract.MapInputSupplyPreservingDemand` declares the forward callback alone and leaves backward demand unchanged; its name states its precondition, which is that the operation consumes its input at the density its own consumer demands. Either callback may be evaluated again while resolving symbolic upstream metadata. An operation that resamples must use `MapInputSupply`, or an unbounded input materializes at the operation's own output demand instead of the density the operation consumes. For an affine density map, the public statics `TransformRenderNode.RescaleDensity` and `TransformRenderNode.RescaleDemand` supply the two callbacks; they are the two halves of one relationship rather than inverses of each other, because each errs toward more detail through a different axis.

`RenderInputDemandContract` carries a backward demand where a `RenderScaleContract` cannot. `MapOutputDemandToInput` maps one input; `MapOutputDemandPerInput` maps each input separately by its zero-based index, which is what a combine or an expand needs when it resamples its inputs asymmetrically — enlarging the first while passing the second through. `OpaqueRenderDescription.Create` accepts one as `inputDemand`, and only a combine or an expand may declare it: a one-input map carries demand back through `RenderScaleContract.MapInputSupply` instead, and a source has no input to demand from. `ShaderDescription.WholeSource` accepts one for the same reason, because a whole-source stage resolves its own supply from the working scale and has no forward map to declare. `GeometryDescription.Create` and `TargetCommandDescription.Create` accept one too: geometry is a materialization boundary that can draw its input through an enlarging transform, and a target command can resample any of the inputs it draws onto the target, so the command's contract is resolved per input index. In every case the default leaves demand unchanged, which is correct only for an operation that consumes its input at the density its own consumer asked for. A target scope has no equivalent: the backward half of its `RenderScaleContract` is read only for the engine's internal value-replay map, because an ordinary scope replays its input onto the target at the target's own density.

`RenderHitTestContract.FromSlot` builds a hit test that reads the resource the recording bound to a `RenderResourceSlot<T>`, resolved against the description's own bindings rather than captured by the contract. This is what lets a contract be hoisted into a `static readonly` field while the token it reads changes every request. A whole-source shader's hit test resolves against the bindings passed as `hitTestResources`, even though execution addresses those same tokens by SkSL child-shader name. `RenderHitTestContext.UseResource` exposes the same resolution to a `Custom` callback. A hit-test callback still may not be a `RenderResource`'s own method, because the contract holding it outlives the token.

An *execution* callback — a painted source's `draw`, a shader binding's `bind`, and the `execute` or `render` a description retains — may read the `RenderNode` that declares it on the same terms a metadata callback may, and its operation-shape fingerprint is then the callback's method, so two nodes of one type share one compiled plan and each re-runs it over its own values. A callback that reads anything else keeps being separated by its delegate. Reading the node is not a way around `RenderNode.MarkChanged`: it is the same contract a callback handed immutable state already stands on, because a recording whose node reports no change may be replayed rather than re-recorded. BESG005 reads a lambda written inside `Process` as part of `Process`, so an unmarked write to state only the callback reads is reported exactly as one to state the method reads directly.

## Recording rules

- `Opacity`, `Blend`, `OpacityMask`, `ContributeValues`, `Layer`, `OwningTargetLayer`, `TargetLayerScope`, `MaterializedInput`, `TargetCapture`, `PaintedSource`, `Shader`, `Geometry`, `OpaqueSource`, `OpaqueMap`, `OpaqueCombine`, `OpaqueExpand`, and target scopes return unpublished handles.
- An opaque description does not name its topology; the bounds, scale, cardinality, and hit-test contracts it declares decide which of `OpaqueSource`, `OpaqueMap`, `OpaqueCombine`, and `OpaqueExpand` may record it. An incompatible pairing is rejected at the recording method, before any fragment exists.
- Target commands are ordinary effectful handles; publish them at the intended painter position.
- A fragment may be published or consumed more than once only when it is value-eligible. Publishing or consuming an effectful fragment — a target command or scope, or any wrapper built over one — more than once is rejected and the recording rolls back.
- `RawTargetScope` replays its input exactly once. `RawTargetCommand` has no logical value input.
- `RecordNode` and `RecordSubtree` record nested work in the active request; no transaction-scoped handle may escape the call that produced it.

## Cache and failure rules

The renderer controls retained output and resource lifetime. An author invalidates node content only by calling `MarkChanged()`; no context method opts a recording out of reuse and no token carries public content metadata. Raw target work remains request-local by definition.

`ContainerRenderNode` calls `MarkChanged()` itself when its children change — `AddChild`, `RemoveChild`, `RemoveRange`, `SetChild`, and `BringFrom` — because replacing a child changes what the container composes and the container's own state does not otherwise record it. `SetChild` with the child already at that index is a no-op. A container assembled and then rendered is therefore dirty on its first frame, which is one frame before its cache can warm.

*Amended.* The reuse opt-out this contract withheld was reinstated during implementation: `RenderNodeContext.DisableRenderCache()` monotonically removes the current transaction from persistent caching, and `IsRenderCacheEnabled` reports that state. It was published because a node that records a child it cannot list in `ChildNodes` has no other way to stay correct — the cache cannot observe a change reported only by that unlisted child. It is not a second invalidation signal: `HasChanges` remains the only way to invalidate a cached node. The migration is in [breaking-changes.md](breaking-changes.md), which carries the current contract. The rest of the paragraph is unchanged: no token carries public content metadata, and raw target work stays request-local.

If `Process` or a deferred callback fails, the engine preserves the primary failure, releases request-owned resources best-effort, and does not publish a partial result.
