# Public API Contract

This contract fixes the public authoring shape for feature 004. Names and responsibilities below are normative for implementation; ordinary framework details such as XML documentation and nullable annotations may be completed without changing the model.

## Namespaces

- Render-node/request authoring: `Beutl.Graphics.Rendering`
- Shared Shader/Geometry authoring: `Beutl.Graphics.Effects`
- Existing geometry, color, and media value types remain in their current namespaces.

No `EffectGraphBuilder`, `RenderPlanBuilder`, public executable operation hierarchy, or parallel compatibility namespace is introduced.

## Construction convention

For new or reshaped types in these excerpts, constructor accessibility is normative: a public class is externally constructible only when a public constructor is explicitly shown. `RenderFragmentHandle`, `RenderNodeContext`, `RenderNodeRasterization`, both `RenderResource` classes, all description/binding/source classes created through a displayed factory or builder, and every hit-test/scale/execution/session/input/output/canvas/writer context have internal constructors. In particular, out-of-tree code cannot instantiate or subclass an unvalidated description, fabricate a resource token, or create an execution facade. Public value structs may be constructed as shown, but every contract struct whose `default` is documented invalid is validated before use. `RenderNodeRendererOptions` and `RenderNodeRenderRequest` keep public parameterless constructors for object-initializer use. Existing `FilterEffectContext` construction and members retain their current accessibility unless this contract explicitly changes them; its public `(Rect bounds, float outputScale = 1f, float workingScale = 1f)` constructor remains.

## Render-node contract

```csharp
namespace Beutl.Graphics.Rendering;

public abstract class RenderNode : IDisposable
{
    protected RenderNode();

    public RenderNodeCache Cache { get; }
    public bool IsDisposed { get; private set; }
    public bool HasChanges { get; set; }

    public abstract void Process(RenderNodeContext context);

    public void Dispose();
    protected virtual void OnDispose(bool disposing);
}
```

The old `RenderNodeOperation[] Process(RenderNodeContext)` signature is removed in the same change. There is no returning overload or obsolete bridge. Construction, finalization, idempotent disposal, `Cache` disposal, and the protected `OnDispose` customization point otherwise retain the current-main lifecycle so an out-of-tree derived node remains constructible and disposable.

## Fragment handle

```csharp
namespace Beutl.Graphics.Rendering;

public readonly record struct RenderFragmentMetadata(
    Rect Bounds,
    EffectiveScale EffectiveScale);

public sealed class RenderFragmentHandle
{
    public RenderValueCardinality ValueCardinality { get; }
    public bool ContributesValuesToTarget { get; }
    public bool CanBeUsedAsValueInput { get; }

    public bool TryGetMetadata(out RenderFragmentMetadata metadata);
    public bool TryHitTest(Point point, out bool result);
}

public readonly struct RenderValueCardinality : IEquatable<RenderValueCardinality>
{
    public int Minimum { get; }
    public int? Maximum { get; }

    public static RenderValueCardinality None { get; }
    public static RenderValueCardinality Single { get; }
    public static RenderValueCardinality ZeroOrOne { get; }
    public static RenderValueCardinality Dynamic { get; }

    public static RenderValueCardinality Exactly(int count);
    public static RenderValueCardinality Range(int minimum, int? maximum);
}
```

The constructor is private. `Exactly` and `Range` reject negative minima/counts and a maximum smaller than the minimum. `default(RenderValueCardinality)` is uninitialized and rejected by every recording/description factory, so accidental zeroed structs cannot become dynamic value declarations.

`RenderFragmentHandle` has an internal constructor. It does not implement `IDisposable`, cannot be subclassed, and has no `Render`, `CreateLambda`, `CreateDecorator`, `CreateFromRenderTarget`, or `CreateFromSurface` members.

The handle denotes one ordered render-fragment stream, not necessarily one runtime bitmap. A fragment may contain a semantic value contribution, a target command, an ordered sequence, or a target-local scope around other fragments. Commands therefore travel through parent `Inputs` and remain inside finite Layer, `TargetLayerScope`, opacity, transform, and filter scopes instead of escaping early to the root target. `RenderValueCardinality` counts materializable values only; an effectful target command can be a real published fragment while its value cardinality is `None`. Fragment existence/order is tracked separately inside the recorder.

`TryGetMetadata` and `TryHitTest` expose only conservative pure metadata that is already concrete during recording. They never wait for graph-wide ROI analysis or execute deferred work. On success, `RenderFragmentMetadata.Bounds` is the concrete logical value/query bound recorded so far and `EffectiveScale` is its materializable supply; runtime shrink/discard may narrow the declaration later. `TryHitTest` evaluates only the corresponding CPU metadata contract. On failure, their out values are respectively `default(RenderFragmentMetadata)` and `false`.

An `OwningTargetDomain` fragment has symbolic recording metadata even when its internal reference temporarily carries finite placeholder bounds or scale. Every ordinary descendant of such a fragment remains symbolic, including handles returned by nested `RecordNode` or `RecordSubtree`; placeholder values are never exposed as authoritative metadata. A finite public `Layer` is the explicit resolution barrier: if every input is concrete it preserves the normal tight child-derived bounds and hit test, while any symbolic input makes the Layer publish its complete finite domain as conservative bounds and use domain containment as its conservative hit test. The Layer remains internally connected to symbolic dependencies for final graph-wide resolution and fan-out analysis. `ValueCardinality`, `ContributesValuesToTarget`, and `CanBeUsedAsValueInput` remain readable whether metadata is concrete or symbolic. Every public member validates that the handle is still active in its owning recording transaction.

`CanBeUsedAsValueInput` is conservative transaction-memoized metadata. Public recording APIs return true only when the fragment exposes every possible runtime value as a materializable value stream without replaying a target-state scope or an effect-only fragment. The engine-owned value-replay-map exception below is restricted mechanically rather than exposed as a general authoring capability. A target capture is true even though its explicit preceding-token dependency remains scheduled, so this property does not promise purity or request-independent execution. The result is fixed at recording by these rules:

| Recorder/result | Input requirement | Result |
|---|---|---|
| `OpaqueSource`, `MaterializedInput`, `TargetCapture`, `Layer` | Their own descriptor validation; finite `Layer` accepts mixed fragments | `true` |
| `OwningTargetLayer` | Accepts mixed fragments; graph finalization MUST resolve one finite owning target domain | `true` |
| `Shader`, `Geometry`, `OpaqueMap` | Primary input MUST already be `true` | `true` |
| `OpaqueCombine`, `OpaqueExpand` | Every input MUST be `true`; an allowed empty input list is vacuously eligible | `true` |
| `ContributeValues` | Input MUST be `true` | preserves `true` |
| `Opacity` | Any fragment may be wrapped | `true` only for a value-input-eligible child; otherwise `false` |
| `OpacityMask` | Any fragment may be wrapped | `true` only when the primary child and every lowered mask dependency are value-input eligible and no `LegacyRawCanvas` fallback is required; otherwise `false` |
| `Blend` | Any fragment may be wrapped | `false`, because the result retains a dependency on the current destination even for a pure child |
| `TargetLayerScope`, public `TargetScope`, `RawTargetScope`, `TargetCommand`, `RawTargetCommand` | As declared by their public APIs | `false` |
| engine-owned TargetScope value-replay map | One contributing, self-contained `Single` input that is already `true` | preserves `true` |
| nested `RecordSubtree`/`RecordNode` result | Child-defined | preserves each returned handle's recorded value |

The `TargetScope` row describes the public `TargetScopeDescription.Create` path. An engine-owned scope may use the
internal value-replay-map descriptor only when its callback is mechanically restricted to allocation-free target
state plus one replay. That internal result is eligible only for one contributing, value-eligible, self-contained
input with `Single` cardinality; it does not relax the public authoring rule.

A mixed command/value fragment is therefore false until an explicit `Layer` localizes and materializes its painter result. It is legal to publish the same handle more than once as explicit fan-out when the fragment is pure; effectful fragment fan-out is rejected except for a target capture, whose one scheduled materialization may feed multiple pure consumers. Non-friend public-contract tests MUST assert every public table row, including a `Shader -> Opacity -> Shader` chain whose opacity result remains true and a pure-child `Blend` result that remains false.

## Request classifications

```csharp
namespace Beutl.Graphics.Rendering;

public enum RenderIntent
{
    Preview,
    Delivery,
}

public enum RenderRequestPurpose
{
    Frame,
    HitTest,
    Bounds,
    CacheWarmup,
    Auxiliary,
}
```

`RenderIntent` selects the existing preview/delivery allocation-failure behavior. `RenderRequestPurpose` selects persistent-state and execution behavior. They are independent and inherited by nested recording.

## High-level node renderer

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class RenderNodeRenderer : IDisposable
{
    public RenderNodeRenderer(
        RenderNode root,
        RenderNodeRendererOptions? options = null);

    public RenderNode Root { get; }
    public RenderNodeRendererOptions Options { get; }
    public bool IsDisposed { get; }

    public void Render(
        ImmediateCanvas destination,
        RenderNodeRenderRequest? requestOptions = null);
    public RenderNodeRasterization Rasterize(
        RenderNodeRenderRequest? requestOptions = null);
    public RenderNodeMeasurement Measure(
        RenderNodeRenderRequest? requestOptions = null);
    public bool HitTest(
        Point point,
        RenderNodeRenderRequest? requestOptions = null);
    public void Dispose();
}

public sealed class RenderNodeRendererOptions
{
    public RenderNodeRenderRequest DefaultRequest { get; init; } = new();
    public IRenderTargetFactory? TargetFactory { get; init; }
}

public sealed record RenderNodeRenderRequest
{
    public RenderIntent Intent { get; init; } = RenderIntent.Preview;
    public Rect? TargetDomain { get; init; }
    public Rect? RequestedRegion { get; init; }
    public float OutputScale { get; init; } = 1f;
    public float MaxWorkingScale { get; init; } = float.PositiveInfinity;
    public Cache.RenderCacheOptions CacheOptions { get; init; } = Cache.RenderCacheOptions.Default;
    public RenderRequestPurpose Purpose { get; init; } = RenderRequestPurpose.Auxiliary;
}

public enum RenderTargetPixelFormat : byte
{
    LinearPremultipliedRgba16Float,
}

public readonly record struct RenderTargetAllocationDescriptor
{
    internal RenderTargetAllocationDescriptor(
        PixelSize deviceSize,
        GRRecordingContext? graphicsContext,
        nint? graphicsContextHandle);

    public PixelSize DeviceSize { get; }
    public RenderTargetPixelFormat PixelFormat { get; }
    public GRRecordingContext? GraphicsContext { get; }
    public nint? GraphicsContextHandle { get; }
    public GRBackend? GraphicsBackend { get; }
}

public interface IRenderTargetFactory
{
    RenderTarget? Create(RenderTargetAllocationDescriptor allocation);
}

public sealed class RenderTargetDomainRequiredException : InvalidOperationException
{
    public RenderTargetDomainRequiredException(string message);
}

public sealed class RenderNodeRasterization : IDisposable
{
    public Rect Bounds { get; }
    public float OutputScale { get; }
    public Bitmap? Bitmap { get; }
    public bool IsEmpty { get; }

    public void Dispose();
}

public readonly record struct RenderNodeMeasurement(
    Rect OutputBounds,
    Rect QueryBounds,
    EffectiveScale EffectiveScale,
    RenderValueCardinality ValueCardinality,
    bool HasFragments,
    bool HasContributingValues,
    bool HasTargetEffects);

```

Request-wide diagnostics remain an internal renderer/evidence seam in this feature. No public provider, mutable writer, sink, snapshot factory, or telemetry schema is added to `IRenderer` or `RenderNodeRenderer`; the normative internal counters/events are fixed in [diagnostics-and-evidence.md](diagnostics-and-evidence.md#request-wide-diagnostics). This keeps the plugin-facing breaking change focused on render authoring rather than freezing planner telemetry as a second extensibility surface.

`RenderNodeRendererOptions.DefaultRequest` is copied, validated, and sanitized when the renderer is constructed. Passing `null` to an operation selects that snapshot; passing a request supplies a complete replacement rather than a partial overlay. Callers can derive reusable variants with `renderer.Options.DefaultRequest with { RequestedRegion = region, OutputScale = scale, Purpose = purpose }` while the renderer retains its structural/program caches and target pool. `TargetFactory` is renderer-lifetime ownership configuration. Intent, purpose, requested region, scale limits, and render-cache policy are request-specific and may vary between calls. `Render(destination, requestOptions)` derives `TargetDomain` and `OutputScale` from the destination and clamps the request's `MaxWorkingScale` to the destination limit; the descriptor's target domain and output scale govern target-less operations.

The node-renderer constructor snapshots and sanitizes `DefaultRequest`, and each supplied per-call descriptor is copied and sanitized before use. A non-finite or non-positive `OutputScale` becomes `1`; a NaN or non-positive `MaxWorkingScale` becomes positive infinity, while a positive finite value or positive infinity is preserved. A positive finite ceiling is authoritative even when it is below `OutputScale`: the standard scale calculation applies the `OutputScale` floor before the ceiling, so `OutputScale = 2` with `MaxWorkingScale = 1` materializes at `1`. `TargetDomain` and `RequestedRegion` are expressed in the root node's composition-logical coordinate space before the destination canvas's active transform. A non-null `TargetDomain` must be finite and non-empty. It supplies the root target only for target-less `Rasterize`, `Measure`, and `HitTest`; `Render(ImmediateCanvas)` and production Frame requests always use the actual destination viewport and ignore this option. A target-less request with null `TargetDomain` remains valid for self-bounded work, but graph finalization rejects every published root `TargetRegion.Full` access whose enclosing target still has no finite domain by throwing `RenderTargetDomainRequiredException`. Authors set `TargetDomain` or use a finite `TargetRegion.Region`; neither `RequestedRegion` nor query bounds are inferred as a substitute. `RequestedRegion = null` selects the complete conservative `OutputBounds` computed for the root. A non-degenerate non-null rectangle selects its intersection with that output as the final output requirement/commit crop, so a wholly outside selection is a successful empty result rather than a transparent padded bitmap. An explicitly degenerate rectangle is a valid empty request and preserves its authored bounds and shifted origin; `HitTest` returns false even for a point on the retained line or origin. An invalid non-null rectangle is rejected. `RequestedRegion` never replaces or shrinks the available `TargetDomain` used by target reads and scope-relative effects.

Standalone `RenderNodeRenderer.Render` and `Rasterize` preserve the request's public `Purpose`, whose default is `Auxiliary`; executable calls accept `Frame`, `CacheWarmup`, or `Auxiliary`, so direct frame hosts do not bypass the public renderer. Supplying the metadata-only `Bounds` or `HitTest` purpose to either pixel-executing call is rejected. The production `Renderer` sets `Frame` on its default request. `Measure` and `HitTest` intentionally override the supplied purpose with the metadata-only `Bounds`/`HitTest` purposes and emit internal diagnostic snapshots. `OutputScale` is the density for target-less rasterize/metadata calls. `Render(ImmediateCanvas)` instead uses the destination's active `Density` as the request output scale and the lesser of the request and destination maximum-working-scale ceilings; it never silently resamples through the option scale. `TargetFactory` replaces `RenderNodeProcessor.CreateRenderTarget` extensibility and is called only when the renderer-owned pool cannot satisfy a materialization. It receives the exact device size, fixed linear-premultiplied RGBA16F format, and current backend/device context in `RenderTargetAllocationDescriptor`: a positive context handle and borrowed `GraphicsContext` identify GPU, zero identifies a bound CPU destination, and null identifies a target-less request whose backend is not bound yet. The borrowed context is valid only for the synchronous `Create` call. A null factory selects the engine's standard allocator; a selected factory returning null follows the characterized `Intent` failure policy. Targets created by the factory are owned by that pool and are reused or evicted there (`RenderNodeRendererOptions.TargetFactory` with `IRenderTargetFactory`, consumed by `RenderTargetPool.CreateTarget`).

`Render` executes against the borrowed destination exactly as if the root fragments were drawn at the call site: it honors the canvas's active logical transform, clip, opacity, blend mode, coordinate-space density, and prior destination pixels. The finite root target domain is the canvas logical viewport mapped conservatively back through the active transform; `RequestedRegion` is a separate final-output requirement/commit clip, not a shrink of the available target. A singular active destination transform has no two-dimensional visible result, so a value-only self-bounded root is recorded and finalized as a successful no-op without allocating or executing pixel callbacks, leaving the borrowed destination unchanged. Domain-independent target effects still execute against that destination to preserve command ordering; for example, an `Empty` command invokes its callback without producing pixels. A root that requires the destination's owning target domain remains invalid under that transform because no inverse domain can be supplied; metadata resolution rejects its reachable `TargetRegion.Full` access instead of silently suppressing the command. `TargetRegion.Full` resolves during scope-token lowering to the complete finite domain of its current external root, resolved `TargetLayerScope`, or finite value Layer, so backward ROI may expand target reads for blur/filter aprons up to that domain. The active clip remains an additional exact execution constraint. The renderer snapshots destination state for the synchronous request, restores it before returning, and never implicitly clears, closes, disposes, flushes, submits, or snapshots the caller's canvas. A direct-to-root optimization is legal only when it is observably equivalent to that state; otherwise the planner materializes and performs the final composite.

`Measure.OutputBounds` is the root's complete conservative pixel-output extent before `RequestedRegion`: it unions contributing value bounds with the resolved affected regions of every potentially pixel-writing root target effect, applies enclosing scope mappings/clips, and clips to the finite root target domain when one exists. Effects localized inside a finite value `Layer` contribute through that Layer's value bounds instead of being counted again at the root. Engine-proven read-only captures and order-only effects do not enlarge it; public target callbacks and raw forms are conservatively potentially writing. A Full write therefore contributes the resolved root domain even when its query metadata is empty. `Measure.QueryBounds` separately unions contributing value query bounds and target-command/scope query metadata for bounds queries and hit testing; non-contributing capture/read anchors do not enlarge it. Both are independent of `RequestedRegion`.

`ValueCardinality` counts all materializable values, including non-contributing captures, so a command-only root reports `None`. `HasFragments` distinguishes that command from a node that published nothing; `HasTargetEffects` is true for any command, scope, capture, or other target-token/read dependency and distinguishes those from a pure value stream; `HasContributingValues` reports whether automatic value compositing is present. Publication, effect, value, contribution, output extent, and query extent remain distinct facts. Effective scale is the densest declared value supply and is `EffectiveScale.Unbounded` when no materializable value declares a finite supply. `HitTest` examines contributing values and target-command/scope query metadata in reverse painter order but returns false for a point outside a non-null `RequestedRegion`. Neither metadata call executes dynamic shrink/discard/expansion or pixel callbacks.

`Rasterize` creates a transparent private target with identity transform, `SrcOver`, opacity one, and the effective request's `OutputScale`, then executes the complete fragment stream including target commands and internal target-read values. `RenderNodeRasterization.Bounds` is the clipped final commit bounds for a non-degenerate `RequestedRegion`, the authored zero-area bounds for an explicitly degenerate request, or `Measure.OutputBounds` when no region was requested. Before allocation, a non-empty selected rectangle is expanded to the canonical device-pixel cover at `OutputScale`; the returned `Bounds` is that physical footprint converted back to logical coordinates, not the unsnapped semantic selection. The non-null owned bitmap's local `(0, 0)` therefore represents this returned `Bounds.Position`, and its dimensions equal the returned footprint at `OutputScale`. Callers replay at the returned position without an implicit stretch or device-phase shift. The planner may allocate a larger internal target/read apron than the returned crop when backward ROI requires neighboring destination pixels; those pixels are never exposed in the result.

A finite zero-area selected domain is a successful empty result: `IsEmpty == true`, `Bitmap == null`, and `Bounds` preserves that logical empty domain, including its origin; no target is allocated and no pixel callback executes. For a non-empty selected domain, `IsEmpty == false` and `Bitmap != null`, even when every returned pixel is transparent. `RenderNodeRasterization` exclusively owns its bitmap; `Dispose` is idempotent and disposes it, while disposing the renderer does not dispose already returned rasterizations. Callers dispose the result rather than retaining or independently disposing its `Bitmap`. Allocation failures for non-empty results continue to follow `Intent` and are never reported as a successful empty result. There is no list-returning compatibility rasterizer: a fragment stream has one painter-ordered result, and callers that need individual semantic values model them as separate roots/requests.

Each non-null `IRenderTargetFactory.Create` result transfers exclusive ownership to the renderer immediately. It must satisfy its `RenderTargetAllocationDescriptor`: a fresh, unleased target of exactly `DeviceSize`, on the supplied backend/device context when bound, in the declared linear premultiplied RGBA16F `PixelFormat`; returning an external, shared, cached elsewhere, already-leased, or previously returned live target is invalid. The renderer validates observable compatibility before use. It disposes an invalid non-null return under the transferred-ownership rule and then follows the request's allocation-failure policy; a factory exception remains the primary failure. The factory is invoked only on the owning render lifetime/thread and must not retain the borrowed `GraphicsContext` or a lease to its return value.

`RenderNodeRenderer` owns its persistent structural-plan/program caches, target pool, and every factory-created target while it remains in that pool or a request lease. Successful render-cache publication transfers the captured payload into the existing `RenderNodeCache` ownership/invalidation lifecycle; it is no longer a pool lease. Cached output payloads are engine-owned and may retain independent effective scales, so public cache control exposes invalidation/count policy rather than raw `Density`, `UseCache`, or `StoreCache` inspection/seeding. `Dispose` is idempotent, rejects every later public call, and releases renderer-owned resources best-effort while preserving the first disposal failure. It does not dispose `Root`, `Root.Cache`, `TargetFactory`, a borrowed render destination, or an already returned `RenderNodeRasterization`. Concurrent calls on one instance are unsupported. Distinct instances may execute concurrently only when their node/cache graphs, destinations, and externally borrowed mutable resources are disjoint; callers must serialize instances that share any of them.

## RenderNodeContext

```csharp
namespace Beutl.Graphics.Rendering;

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

    public RenderFragmentHandle ContributeValues(RenderFragmentHandle input);

    public RenderFragmentHandle Opacity(RenderFragmentHandle input, float opacity);
    public RenderFragmentHandle Blend(RenderFragmentHandle input, BlendMode blendMode);
    public RenderFragmentHandle OpacityMask(
        RenderFragmentHandle input,
        Brush.Resource mask,
        Rect brushBounds,
        bool invert = false);
    public RenderFragmentHandle Shader(
        RenderFragmentHandle input,
        ShaderDescription description);
    public RenderFragmentHandle Geometry(
        RenderFragmentHandle input,
        GeometryDescription description);

    public RenderFragmentHandle PaintedSource<TState>(
        TState state,
        Action<PaintedRenderSession, TState> draw,
        (Brush.Resource Resource, int Version)? fill,
        (Pen.Resource Resource, int Version)? pen,
        Rect brushBounds,
        Rect outputBounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object structuralKey,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.Insensitive,
        IEnumerable<RenderResource>? resources = null)
        where TState : notnull;

    public RenderFragmentHandle PaintedSource<TResource, TState>(
        RenderResource<TResource> primary,
        TState state,
        Action<PaintedRenderSession, TResource, TState> draw,
        (Brush.Resource Resource, int Version)? fill,
        (Pen.Resource Resource, int Version)? pen,
        Rect brushBounds,
        Rect outputBounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object structuralKey,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.Insensitive,
        IEnumerable<RenderResource>? resources = null)
        where TResource : class
        where TState : notnull;

    public RenderFragmentHandle OpaqueSource(OpaqueRenderDescription description);
    public RenderFragmentHandle OpaqueMap(
        RenderFragmentHandle input,
        OpaqueRenderDescription description);
    public RenderFragmentHandle OpaqueCombine(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description);
    public RenderFragmentHandle OpaqueExpand(
        IReadOnlyList<RenderFragmentHandle> inputs,
        OpaqueRenderDescription description);

    public RenderFragmentHandle MaterializedInput(
        MaterializedInputDescription description);

    public RenderFragmentHandle TargetCapture(
        TargetCaptureDescription description);

    public RenderFragmentHandle Layer(
        IReadOnlyList<RenderFragmentHandle> inputs,
        Rect domain);

    public RenderFragmentHandle OwningTargetLayer(
        IReadOnlyList<RenderFragmentHandle> inputs);

    public RenderFragmentHandle TargetLayerScope(
        IReadOnlyList<RenderFragmentHandle> inputs,
        TargetRegion region);

    public RenderFragmentHandle TargetScope(
        RenderFragmentHandle input,
        TargetScopeDescription description);

    public RenderFragmentHandle RawTargetScope(
        RenderFragmentHandle input,
        RawTargetScopeDescription description);

    public RenderFragmentHandle RawTargetCommand(
        RawTargetCommandDescription description);

    public RenderFragmentHandle TargetCommand(
        IReadOnlyList<RenderFragmentHandle> inputs,
        TargetCommandDescription description);

    public IReadOnlyList<RenderFragmentHandle> RecordSubtree(RenderNode root);
    public IReadOnlyList<RenderFragmentHandle> RecordNode(
        RenderNode node,
        IReadOnlyList<RenderFragmentHandle> inputs);

    public RenderResource<T> Own<T>(
        T resource,
        object? cacheKey = null,
        long version = 0)
        where T : class, IDisposable;

    public RenderResource<T> Borrow<T>(
        T resource,
        object? cacheKey = null,
        long version = 0)
        where T : class;

    public RenderResource<T> Borrow<T>((T Resource, int Version) captured)
        where T : EngineObject.Resource;
}

public static class EngineResourceIdentity
{
    public static object Of(EngineObject.Resource resource);
}

public static class RenderScaleUtilities
{
    public const int MaxBufferDimension = 16384;
    public static float SanitizeMaxWorkingScale(float maxWorkingScale);
    public static float ResolveWorkingScale(
        ReadOnlySpan<EffectiveScale> inputs,
        float outputScale,
        float maxWorkingScale = float.PositiveInfinity);
    public static float ClampWorkingScaleToBufferBudget(
        Rect logicalBounds,
        float workingScale,
        int maxDimension = MaxBufferDimension);
}
```

```csharp
namespace Beutl.Composition;

public class CompositionContext
{
    public Rect? TargetDomain { get; set; }
}
```

`CompositionContext.TargetDomain` carries the finite scene-frame domain into auxiliary composition consumers. `GraphSnapshot` copies and refreshes it on every evaluation, and standalone `PreviewNode`/`MeasureNode` pass it to `RenderNodeRendererOptions.DefaultRequest.TargetDomain`. `RenderNodeContext.TargetDomain` exposes the same request value to nested semantic consumers without resolving an owning-target-dependent handle early.

`EngineResourceIdentity.Of` is the only safe way to key on an `EngineObject.Resource`, and is renderer-wide for the same reason: nodes, brushes, filter effects, and 3D all key on the same resources, and a node needs the identity outside `Borrow` whenever it feeds a hit-test or structural key rather than a declared-resource registration. It returns the backing `EngineObject.Id`, or a synthesized identity for a resource with no backing object. That synthesized value is a distinct type, so no id a caller assigns can be made to collide with it, and it is stable per `Resource` instance and held weakly — a caller that reallocates the resource every frame gets a new identity every frame. The public `Borrow((Resource, Version))` overload derives its key the same way, but registers a borrow as well, so it is not a substitute when the identity is only wanted for comparison.

`RenderScaleUtilities` owns feature 003's pure density calculations because they are also used by 3D, brushes, export policy, and planner code outside a node-recording transaction. The old static members on `RenderNodeContext` are removed and all in-tree callers migrate in the same breaking change; no forwarding compatibility members remain on the context.

`TryCalculateInputBounds(out bounds)` succeeds only when every input's recording metadata is concrete. On success it unions every input's `RenderFragmentMetadata.Bounds` using the normal conservative `Rect.Union` behavior; input order does not affect the result, and an empty input list succeeds with `default(Rect)`. If any input is symbolic it returns false and assigns `default(Rect)`. It never executes deferred work or resolves graph-wide ROI.

### Publication rules

- Inputs are borrowed, read-only fragment streams flattened from child publications in exact painter order. The node does not own or dispose them.
- Recording a value, command, sequence/scope, or opaque fragment returns a handle but does not publish it automatically.
- `PassThrough()` publishes all input streams in input order.
- `Publish` publishes the complete fragment stream represented by one handle.
- `PublishRange` publishes the supplied handles in enumeration order.
- `ContributeValues` is the only operation that changes a non-contributing value fragment to a value-contributing fragment. It requires `CanBeUsedAsValueInput == true`, preserves order, metadata, cardinality, and single execution of effect dependencies, and is idempotent for an already value-contributing fragment. The property describes automatic value compositing only; target commands may still mutate the target while it is false.
- Publishing nothing intentionally yields zero node outputs. There is no implicit pass-through.
- `TargetCommand` returns an ordinary effectful fragment handle. The author places it relative to value fragments with `Publish`/`PublishRange`; no command is auto-published or stored in a separate global side list.
- `DisableRenderCache` is monotonic for the current result and its affected ancestors. There is no public setter and no enable operation.

### Recording rules

- `Opacity`, `Blend`, `OpacityMask`, `Shader`, and unary target scopes preserve input ordering and value cardinality exactly. `TargetLayerScope` preserves the ordered aggregate cardinality represented by all supplied streams without exposing those values as an outer value stream. Geometry is an order-preserving zero-or-one value map. OpaqueMap is likewise either exactly one or zero-or-one per value input, as declared. Value-input eligibility follows the normative table in [Fragment handle](#fragment-handle); cardinality preservation never implies eligibility preservation.
- A semantic pixel map over a pure value fragment records a typed value edge. A target-state semantic such as `Opacity` may instead wrap a command-bearing fragment in a target-local scope that preserves command order; the planner may canonicalize that scope to a typed value edge only after proving equivalence. It never moves a target command across the map or through a parent scope.
- Public `Layer(inputs, domain)` composes every supplied fragment stream into one target-local ordered sequence bounded by a finite, non-empty `Rect`; invalid or empty domains are rejected because Layer promises exactly one materializable value. It is a normal bottom-up value constructor over already-recorded handles. A nested command consumes this layer's local target token, not the external root token. When every input has concrete metadata, the Layer value's content bounds are the union of contributing child-value bounds and every potentially pixel-writing child target effect's affected region after scope maps, clipped to `domain`, and hit testing uses explicit child query metadata. If any input is symbolic, the explicit finite Layer resolves its public metadata conservatively to the complete `domain` for bounds and domain containment for hit testing. Public `TargetCommand` and both raw target forms are conservatively pixel-writing whenever their resolved region is non-empty; engine-proven captures/read-only effects do not add their access region.
- Public `OwningTargetLayer(inputs)` is the symbolic-domain counterpart for plugin nodes that must turn an ordered painter stream into one value before a finite parent domain is known. It preserves `TargetRegion.Full` or raw full-target writes symbolically, remains value-eligible, and resolves its allocation domain only during graph-wide scope lowering against the actual external root, parent `TargetLayerScope`, or finite value Layer. Graph finalization rejects it when no single finite owning domain exists. Authors use the finite `Layer` whenever the domain is already known; neither form guesses or freezes the root domain during recording.

`FilterEffectRenderNode` uses the same public isolation primitives available to plugin nodes. If every contributing value and target write has an owner-independent finite affected region, it records the ordinary finite Layer over that union. If a Full/raw write still needs its parent scope, it records `OwningTargetLayer`, threads it through parent transforms/clips normally, and resolves its allocation domain during graph-wide scope lowering. In both cases the filter remains in authored order and no root-sized recording placeholder becomes authoritative.
- `TargetLayerScope(inputs, region)` is the typed current-target counterpart for group isolation without exposing an outer value. It accepts an authored mixed fragment sequence and any initialized `TargetRegion`. It records bottom-up like every other context method, keeps Full symbolic while later parents add transform/clip/Layer scopes, and resolves the region only during final scope-token lowering against the actual current external root, parent `TargetLayerScope`, or finite value Layer domain; `Empty` is a valid order-only/no-pixel scope. Its handle preserves the supplied streams' ordered aggregate `RenderValueCardinality`, because those dependency values still exist inside the scoped fragment, but has `ContributesValuesToTarget == false` and `CanBeUsedAsValueInput == false`: replaying a target-dependent scope is required to reach them. Its internal recorded hints contain child query metadata only and never pretend that an unresolved affected region is authoritative reusable metadata; public Try queries return false when that dependency is symbolic. It remains an ordered potentially-writing target effect when its resolved region and child writes are non-empty, so root `OutputBounds` includes that affected region even when query metadata is empty.
- At execution, a non-empty `TargetLayerScope` replays the complete mixed stream once into a transparently initialized planner-owned local target and composites that target once into the preceding current target; an Empty scope preserves ordering while scheduling no pixel work. The isolation target and composite are retained unless the planner proves their removal observationally equivalent; direct replay is not a default optimization because overlapping translucent children and destination interaction can change pixels. A Full scope is target-dependent and cannot be cached independently of its preceding target token. Authors that need pixels for Shader, Geometry, another value consumer, or a reusable cache value deliberately wrap the effect fragment in finite `Layer(inputs, domain)`.
- Existing `GraphicsContext2D.PushLayer(default)`/`LayerRenderNode(default)` records `TargetLayerScope(context.Inputs, TargetRegion.Full)` from its ordinary bottom-up `Process`; there is no pre-order traversal exception or early domain resolution. A non-default finite legacy limit records the finite value `Layer`. Target-less finalization of a published root Full scope still requires the effective `RenderNodeRenderRequest.TargetDomain`. Engine-owned semantic consumers may carry an ineligible `TargetLayerScope` through typed effect lowering until its target domain is known, but they do not fabricate a public value-eligible handle; public value consumers use finite `Layer` explicitly.
- `TargetScope` is an order/cardinality-preserving per-fragment map for allocation-free transform/clip state. It replays each input exactly once on the same target. `Opacity`, `Blend`, and `OpacityMask` are planner-visible typed layer scopes. `OpacityMask` declaratively snapshots the `Brush.Resource` during recording; `brushBounds` remains the brush coordinate/mapping frame used by the existing `PushOpacityMask`, not a clip or transparent-outside region. The recorder copies scalar brush state synchronously, includes brush version, mapping bounds, invert, and nested resource identities in output-cache identity, converts every retained image/drawable/native payload to request-owned internal borrow slots before `Process` returns, lowers solid/gradient/perlin/image masks to internal shader/resource dependencies, and records DrawableBrush content as inherited nested fragments. It never retains an undeclared raw brush, invokes `BrushConstructor`, or starts a renderer in the execution callback. Unknown retained custom-brush behavior lowers to `LegacyRawCanvas` rather than being mislabeled exact. A scope is distinct from `Layer`: it does not independently define a mixed child sequence. The planner may canonicalize a typed scope into a pure value edge only after proving target-state equivalence.
- `RawTargetScope` is the explicit migration escape hatch for an old custom decorator whose raw canvas behavior cannot be expressed by the typed vocabulary. It is always opaque-external, cannot fuse or make exact whole-request pass/synchronization claims, and is not the default for new code.
- `RawTargetCommand` is the zero-input counterpart for an existing raw callback that directly reads or mutates the current painter target. It is never used as a guarded value-source API; new independent sources use `OpaqueSource` or `MaterializedInput`.
- `PaintedSource` is the one public entry point for drawing under a `Brush.Resource`/`Pen.Resource` pair whose nested `DrawableBrush` content must be lowered. It lowers the paint during recording — the content becomes ordinary inherited fragments of this request, so its identity reaches the output-cache key — then records the drawing as an `OpaqueSource` (declarative paint) or an `OpaqueCombine` over the lowered content (drawable paint). It publishes `RenderValueCardinality.Single` and is value-input eligible; it never produces a target scope, and a brush the recorder can only execute raw disables render caching for the result. The callback receives a `PaintedRenderSession` carrying the canvas and the resolved `LoweredBrush`/`LoweredPen`, which the public `ImmediateCanvas` draw overloads accept. Ownership of the lowered content stays with the renderer: it is leased for the callback and the author never sees a shader, a native handle, or its resource slot. `PaintedRenderSession.UseDeclaredResource` addresses only the author's own `resources` argument, so the recorder's brush/pen slots cannot shift an author index. `structuralKey` is non-optional for the shared-helper reason stated under [Opaque compatibility descriptions](#opaque-compatibility-descriptions): the execution callback belongs to the recorder helper, not to the calling node.
- `PaintedSource` has a second, primary-resource form. Every painted source in the tree draws exactly one resource, and every one of them reached it the same way: `UseDeclaredResource<TResource>(0, value => ...)` inside the draw callback. That is four things the author had to keep in step by hand — the index, the type argument, the call itself, and a nested closure — for a resource the recorder was going to declare anyway. The primary form takes `RenderResource<TResource> primary` and an `Action<PaintedRenderSession, TResource, TState> draw`; the recorder declares `primary` ahead of `resources`, leases it for the duration of the callback, and passes the leased value in as a parameter. The resource is type-checked at compile time, and the form has no ambiguity mode: there is exactly one primary and it is named, not looked up.
  - The declared list is unchanged. `primary` is element 0 followed by `resources` and then the recorder's paint slots, which is exactly what an author previously wrote as `resources: [primary, ...]`, so nothing an output-cache key is built from moves. `DeclaredResourceOrderTests` still pins `GeometryRenderNode`'s `[geometry, hit-test, fill, pen, penBrush]`, and `PrimaryPaintedSourceTests` pins the two forms declaring the same identities in the same order.
  - `primary` is outside the callback's positional space, exactly like the recorder's paint slots, so `UseDeclaredResource` indices still count from the author's own first resource and adopting the primary form renumbers nothing. It is also unreachable that way: it is already leased for the callback, and a nested lease of the same resource is rejected.
  - The output-cache identity is unchanged: `state` is still the whole runtime identity, and the wrapper the recorder builds around the three-argument delegate is not part of it, so an unchanged second frame is still served from the first frame's cached output.
  - It is cheaper, not merely no more expensive: the recorder's single lease closure replaces the author's, and the author's `resources` array is one element shorter. `PrimaryPaintedSourceTests` measures both forms over a whole rasterization, taking the quietest of seven rounds of 200 rasterizations, and pins the direction of the comparison twice. On the two-resource painted node the fixture builds for the purpose, the primary form allocates 56 bytes/rasterization less. Against a reconstruction of the shipped `GeometryRenderNode`'s own `Process` body recorded through the positional overload, measured in the same process, it allocates 56 bytes/rasterization and 24 bytes/recording less; an independent audit of the same comparison measured 57 and 24. The reconstruction is not byte-identical to the shipped node — it re-captures its snapshots each `Process`, derives from `RenderNode` rather than `BrushRenderNode`, and carries local copies of the node's private hit-test types — but none of those differ in what they allocate — an audit built a third node keeping all three differences while recording through the primary overload and measured it byte-identical to the shipped node — so they do not bias the difference. The absolute totals drift with the shared rasterization scaffold; the differences do not.
  - `UseDeclaredResource` stays, and is exactly the fallback for a callback that needs two or more resources; the primary form addresses one. `DeclaredResourceAddressingTests.TwoResourcesOfTheSameTypeAreDistinguishedOnlyByPosition` still pins that supported capability.
- The same shape is deliberately **not** added to `OpaqueRenderDescription.Create`, `TargetScopeDescription.Create`, or `TargetCommandDescription.Create`, even though `ParticleRenderNode`, `GeometryClipRenderNode`, and `NodeGraphFilterEffectRenderNode` each address one resource at index 0 through them. This is a cost decision, not a capability distinction: those three sites keep the positional hazard, and every route away from it today costs something else.
  - Those are caller-owned description factories, not recorder methods. `RenderDescriptionValidation.CopyResources` rejects a null or released element and copies; it does not merge, de-duplicate, or reorder, and each factory hands that copy straight to the description, so a description's `Resources` holds exactly its caller's elements in its caller's order, and that list is what its session is handed at execution. `BrushRecorder` does own such a merge — it folds `primary`, the author's `resources`, and the lowered paint's slots into one declared list, de-duplicated by slot identity — and `PaintedRenderSession` addresses the author's own list separately from it, which is what keeps `primary` outside the positional space. The other three sessions address the description's declared list itself, so a `primary` parameter on those factories would either renumber every `UseDeclaredResource` index its caller wrote, or push that same two-list split into three public description types.
  - Adding the parameter to three more factories would triple the new public surface to remove an index from three call sites.
  - `UseResource` is reachable at these three sites, but never for free, which is what the earlier "supported alternative" rationale got wrong. All three record through the state-passing `Create(state, static callback, …)` overload — `OpaqueRenderDescription.Create` in `ParticleRenderNode.RecordFallbackSource`, `TargetScopeDescription.Create` in `GeometryClipRenderNode.Process`, `TargetCommandDescription.Create` in `FilterEffectInputBinding.PublishDeferredPreviews` — and every one of those overloads rejects a callback that captures the token and rejects a token carried in a `state` tuple element, so nothing those factories validate hands these callbacks a token. Two routes get one anyway, and each has a price. Moving the recording to the sibling `CreateRequestLocal` is compile-time safe and needs nothing trusted: the callback simply captures the token. It buys that with a fresh request-local identity every recording, so the result is never served from the output cache — an unchanged second frame re-executes instead of being served. Keeping the state-passing recording and routing the token past the state rules — through a sealed non-tuple holder carried in `state`, or a `static` field the callback reads, both of which physically deliver a leasable token — is the enumerated identity channel under [Opaque compatibility descriptions](#opaque-compatibility-descriptions). By itself it costs neither a cache hit nor correct pixels at these three sites: a leased resource must still be declared, and declaration writes its `CacheIdentity` into the output-cache key whichever channel the token travelled through, so a version bump on the declared resource still invalidates the entry. What it costs is that the author now owns the channel's lifetime and its identity, and nothing validates either. A `static` field holds one slot for the whole type, so two recordings in one frame make the first callback lease the second's token. That usually throws — `The render resource was not declared by this operation.` at execution, or `Repeated explicit Borrow registrations must use equal cache keys and matching versions.` at recording — but not always: the authorization check is membership of the leased slot in the operation's declared list, so a mis-lease passes silently whenever both operations happen to declare both resources, or whenever both borrows coalesce onto one slot. A token kept past its own request throws `A released render resource no longer retains its request-scoped slot.`. A holder is per-recording only if the author allocates one per recording, and what any of these shapes then costs in output-cache terms depends on the recording channel, not on the holder — see [Opaque compatibility descriptions](#opaque-compatibility-descriptions). Keeping the index is the third price, and the cheapest of the three at these sites. `DeclaredResourceAddressingContractTests` pins each part: the six rejections across the three factories, the cache hit `CreateRequestLocal` gives up, and the `static` field that leases anyway.
- A painted source runs on the same guarded callback canvas whichever path it takes. When the planner replays it straight onto an existing target instead of materializing it, the engine attaches the guarded `Draw` capability to that target for the callback's duration and detaches it afterwards, unwinding any state the callback left pushed; it changes no transform or clip, so the replay stays byte-identical. Every guard the materializing path enforces — declared-resource authorization, no `SaveLayer`-backed state, no nested render work, snapshots, raw surfaces, or legacy raw callbacks, and no popping below the executor's own state — therefore also holds on the direct path.
- `LoweredBrush`/`LoweredPen` carry the execution lease that resolved them, not just the payload. Every draw overload that accepts one asks that execution whether the payload is still held, so a copy an author retains past the callback is rejected wherever it is used, including on a canvas the author constructed. Retention is a deterministic failure rather than a released native handle.
- Whether a painted source may be replayed directly is derived from its declared `RenderScaleContract`, not authored. A direct replay renders at the destination target's density, which is only what the source declared when the contract declares no supply density of its own — `RenderScaleContract.Vector`, whose resolved supply is `EffectiveScale.Unbounded` and therefore already means "whatever the consumer renders at". A concrete declared density (`Custom`, `MaterializeAtWorkingScale`) materializes at that density and is resampled by its consumer like any other supply. `PreserveInputSupply`/`MapInputSupply` can also resolve to `Unbounded`, but they are valid only for a one-input `OpaqueMap`, never for the source/combine topology a painted source records. A brush the recorder could only keep executable additionally suppresses the direct path in both topologies, because its `BrushConstructor` may start a nested renderer.
- The `ImmediateCanvas` overloads that accept a lowered paint are `DrawEllipse`, `DrawRectangle`, `DrawGeometry`, `DrawText`, `DrawBitmap`, `DrawBitmapScaled`, `DrawImageSource`, and `DrawVideoSource`, so an out-of-tree author can write any of the source node kinds the engine ships. Each verifies its own non-paint resource argument exactly as its `Brush.Resource`/`Pen.Resource` sibling does. `PushOpacityMask`'s lowered overload stays engine-internal: it is `SaveLayer`-backed, and `SaveLayer`-backed state is rejected on every guarded callback canvas, which is the only kind an author reaches.
- `OpaqueCombine` consumes the flattened ordered input streams and publishes the cardinality declared by the description.
- `OpaqueExpand` consumes its flattened inputs and may publish a runtime-dynamic stream within the declared cardinality/bounds contract.
- `RecordSubtree` traverses the supplied node's normal child structure in the current request.
- `RecordNode` invokes one node with explicitly supplied borrowed inputs. Both forms inherit request ownership, options, cache policy, diagnostics, and failure handling.
- Both nested methods reject a node already active in the current request-family traversal before invoking it. Sequential repeated occurrences are legal, but self/ancestor recursion is a deterministic recording failure rather than a stack overflow.
- Neither nested method executes the recorded child.
- Built-in semantic methods such as `Opacity` encode their pixel-affecting scalar arguments in output-cache identity automatically while keeping the operation kind/shape structural.
- No per-node resolved requested region is exposed during `Process`; backward ROI is computed only after the complete graph exists.
- Feature 003's scale helpers remain pure public `RenderScaleUtilities`. They do not allocate or reveal a resolved per-node ROI; planner code applies the same rules after graph analysis.

`Opacity`, `Blend`, `OpacityMask`, `TargetScope`, and `RawTargetScope` may wrap any fragment, including an effect-only target command or `TargetLayerScope`, because they replay it on the same target. `TargetLayerScope` accepts an arbitrary mixed sequence but deliberately keeps it effect-only; `Layer` is the only public primitive that turns such a sequence into one outer materializable value. `ContributeValues`, `Shader`, `Geometry`, `OpaqueMap`, `OpaqueCombine`, `OpaqueExpand`, and `TargetCommand` require inputs whose `CanBeUsedAsValueInput` is true and reject a bare command/effect fragment or shared target-state scope; authors inspect the property and deliberately use finite `Layer` when transforming a mixed painter sequence is semantically intended. The materialized session `Inputs` lists contain values only and never silently omit or auto-materialize effect-only fragments. A target-capture value is valid even though its own contribution flag is false, because its token dependency remains explicit.

Unary value maps and same-target replay scopes—including Opacity, Blend, OpacityMask, Shader, Geometry, OpaqueMap, TargetScope, and RawTargetScope—preserve the primary input's `ContributesValuesToTarget` flag; an OpacityMask's lowered brush/nested value is dependency-only. OpaqueCombine/Expand outputs contribute values iff at least one consumed value input does; all-capture/non-contributing inputs stay non-contributing until `ContributeValues`. OpaqueSource and ordinary MaterializedInput values contribute by default. Layer contributes its value when its local sequence contains any contributing value or pixel-writing target effect; a read/order-only Layer remains non-contributing. `TargetLayerScope`, TargetCapture, TargetCommand, and RawTargetCommand do not contribute values by definition, although the scope/commands may still modify the current target. Query-bounds union and root hit testing ignore non-contributing value anchors but still include target-effect query metadata; dependency and affected-region metadata remain available internally for output-extent/lowering analysis.

### Transaction rules

Each `Process` invocation receives a fresh context checkpoint. On normal return, the engine validates handle ownership and atomically commits fragments, semantic values, target commands/scopes, ordered publications, resource transfers, and cache disablement. On exception, all partial state is discarded, transferred resources are released best-effort, and the primary exception is preserved. The context and every handle created for or exposed by that invocation reject use after either outcome.

Nested recording never exposes a parent handle object directly to a child. `RecordNode` maps every supplied parent handle to a fresh child-owned facade over the same internal fragment ID; ordinary `RecordSubtree` traversal applies the same rule when publishing child outputs as the next node's inputs. Child facades and child-created handles invalidate when the child invocation ends, while the original parent handles remain active. Successful child outputs are then mapped to fresh parent-owned handles before `RecordNode`/`RecordSubtree` returns. This preserves transaction isolation in both directions and never leaks a sealed handle.

The recorder maintains a reference-identity active-node stack shared by same-target and separate-target nested recording in one request family. Encountering any active node rolls back the attempted nested checkpoint and throws an `InvalidOperationException` containing the cycle path; the outer node transaction follows normal rollback/resource cleanup. A node leaves the guard in `finally`, so reuse in a later sibling occurrence or later request remains valid.

## Resource handles

```csharp
namespace Beutl.Graphics.Rendering;

public abstract class RenderResource
{
    internal RenderResource();

    public RenderResourceIdentity CacheIdentity { get; }
}

public sealed class RenderResource<T> : RenderResource
    where T : class
{
    // No public Value property and no public constructor.
}

public readonly record struct RenderResourceIdentity(object Key, long Version);

public readonly record struct RenderRuntimeIdentity(object Key);
```

`Own` requires `T : class, IDisposable` and transfers ownership immediately into the current transaction. The returned token can be declared by Shader, Geometry, materialized, opaque, target-scope, or target-command descriptions. It can be borrowed only through an authorized execution session callback. Rollback disposes it; commit moves it to the request exactly once; request teardown releases it exactly once. Context cloning/nesting shares a reference-counted request slot and never duplicates ownership. `RenderResource<T>` itself requires only a reference type because a borrowed managed resource has no disposal transfer.

`Borrow` instead accepts any reference type and records a request-scoped read-only reference to an externally owned resource without accessing it or transferring disposal. A non-null `cacheKey` must be equality-stable and `version` must change whenever pixel-affecting contents change. A null key gives that registration a fresh request-local cache identity, safely disabling cross-request output-cache reuse without forcing a volatile provider to invent a stable key. The external owner guarantees the resource remains alive, compatible with its device/thread rules, and not concurrently mutated or exclusively leased until every executing request that borrowed it completes. The scoped `UseResource` callback also must not mutate pixel-affecting state. Exclusive mutation or consumption requires `Own`. Metadata-only requests create/release only the managed borrow token and neither touch nor dispose the raw resource. Request teardown invalidates the token but never disposes the borrowed value. This is the normal shape for a repeatable node that exposes an existing materialized target; `Own` remains available for a genuinely one-shot target.

The `(T Resource, int Version)` overload is the shape an `EngineObject`-backed node already holds: it takes the snapshot `Capture()` produced and derives both halves of the identity from it, so the key cannot be paired with a version the node's `Compare`/`Update` never tested. Its key is the backing `EngineObject.Id`, or — for a `Resource` that never went through `ToResource` and therefore has no backing object, which the public `FilterEffectContext.RegisterBrush`/`RegisterPen` entry points accept — a stable synthesized identity held weakly against the resource. Reading `GetOriginal().Id` directly cannot serve that second case.

The request family maintains one raw-resource table keyed by reference identity. A second `Own` of the same raw object, or any `Own`/`Borrow` mixture for that object, is rejected during recording before another transfer occurs. Repeated `Borrow` registrations of the same object with an explicit non-null key coalesce onto one request-family slot only when their cache keys compare equal and versions match; an explicit mismatch is rejected. Each null-key registration receives a distinct request-local slot/identity and never coalesces. The same valid token or coalesced borrowed slot may be declared by multiple descriptions, and each execution access remains callback-scoped.

The internal base constructor prevents out-of-tree subclasses or fabricated tokens; arbitrary author resources are represented only by engine-created `RenderResource<T>` from `Own` or `Borrow`.

`cacheKey` and `version` are runtime output-cache identity, never structural-plan identity. For either `Own` or `Borrow`, a null key creates a unique request slot identity, which is safe but prevents cross-request pixel-cache reuse. Authors increment `version` whenever pixel-affecting contents change under a non-null key. Every description lists the resource tokens it may borrow; the recorder automatically incorporates their identities/versions into output-cache keys and rejects undeclared tokens at execution.

`RenderRuntimeIdentity` is the matching non-resource channel for pixel-affecting scalar/value state captured by opaque, Geometry, scope, and command callbacks. Its `Key` must be non-null, immutable for its equality lifetime, and equality-stable; tuples and immutable records are typical keys. `default(RenderRuntimeIdentity)` and an explicitly null key are invalid and rejected by description factories. Those factories accept a nullable identity: nullable `null` makes the recorder synthesize a fresh request-local identity on every recording, which is always correct but prevents cross-request pixel-cache reuse for that value or command. Shader binders instead use the snapshot-constrained `ShaderBindingCachePolicy` contract below. Runtime identity participates in render-output cache identity only and never in structural plan/program identity.

Every explicit `structuralKey`, `RenderRuntimeIdentity.Key`, and resource `cacheKey` may be retained by a structural/program/render cache beyond the recording request. It must therefore be a lightweight, immutable, equality-stable CPU identity such as a `Type`, string, primitive/value tuple, or immutable record composed of such values. Keys must not be a context/session/handle/facade, `RenderResource`, delegate closure, mutable collection or graph, `IDisposable`, native/target object or handle, or a large payload. Hashes select buckets only; complete key equality decides identity. When a large or native object needs identity, authors supply a small immutable ID/version key instead of the object itself.

## Opaque compatibility descriptions

```csharp
namespace Beutl.Graphics.Rendering;

public enum RenderDeviceGridSensitivity : byte
{
    Insensitive,
    PhaseDependent,
}

public sealed class OpaqueRenderDescription
{
    public OpaqueRenderBoundsContract Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public RenderValueCardinality ValueCardinality { get; }
    public RenderScaleContract Scale { get; }
    public RenderDeviceGridSensitivity DeviceGridSensitivity { get; }
    public IReadOnlyList<RenderInputReadback> InputReadbacks { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static OpaqueRenderDescription Create<TState>(
        TState state,
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.Insensitive,
        object? structuralKey = null,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResource>? resources = null)
        where TState : notnull;

    public static OpaqueRenderDescription CreateRequestLocal(
        Action<OpaqueRenderSession> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.Insensitive,
        object? structuralKey = null,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResource>? resources = null);
}

public readonly struct RenderInputReadback
{
    public static RenderInputReadback None { get; }
    public static RenderInputReadback All { get; }
    public bool ReadsAllValues { get; }
    public IReadOnlyList<int> ValueIndices { get; }

    public static RenderInputReadback Values(IEnumerable<int> valueIndices);
}

public sealed class OpaqueRenderBoundsContract
{
    public static OpaqueRenderBoundsContract Source(Rect outputBounds);
    public static OpaqueRenderBoundsContract Map(RenderBoundsContract bounds);

    public static OpaqueRenderBoundsContract Combine(
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>> getRequiredInputBounds,
        object? structuralKey = null);

    public static OpaqueRenderBoundsContract FullInputs(
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        object? structuralKey = null);
}

public readonly struct RenderHitTestContract
{
    public static RenderHitTestContract None { get; }
    public static RenderHitTestContract OutputBounds { get; }
    public static RenderHitTestContract AnyInput { get; }

    public static RenderHitTestContract Custom(
        Func<RenderHitTestContext, Point, bool> hitTest,
        object? structuralKey = null);
}

public sealed class RenderHitTestContext
{
    public Rect OutputBounds { get; }
    public IReadOnlyList<RenderHitTestInput> Inputs { get; }
}

public readonly struct RenderHitTestInput
{
    public Rect Bounds { get; }
    public bool HitTest(Point point);
}

public readonly struct RenderScaleContract
{
    public static RenderScaleContract Vector { get; }
    public static RenderScaleContract PreserveInputSupply { get; }
    public static RenderScaleContract MaterializeAtWorkingScale { get; }

    public static RenderScaleContract MapInputSupply(
        Func<EffectiveScale, EffectiveScale> map,
        object? structuralKey = null);

    public static RenderScaleContract Custom(
        Func<RenderScaleContext, float> resolve,
        object? structuralKey = null);
}

public readonly record struct RenderScaleContext(
    IReadOnlyList<EffectiveScale> InputSupplies,
    Rect OutputBounds,
    float OutputScale,
    float MaxWorkingScale);

public sealed class RenderCallbackCanvas
{
    public float Density { get; }
    public Rect LogicalBounds { get; }
    public Point LogicalOrigin { get; }
    public PixelRect DeviceBounds { get; }
    public Vector DeviceGridOffset { get; }
    public Rect RasterBounds { get; }

    public void Use(Action<ImmediateCanvas> draw);
}

public sealed class OpaqueRenderSession
{
    public IReadOnlyList<RenderExecutionInput> Inputs { get; }
    public IReadOnlyList<RenderExecutionInputRange> InputRanges { get; }
    public Rect OutputBounds { get; }
    public Rect RequiredRegion { get; }
    public PixelRect DeviceBounds { get; }
    public PixelSize DeviceSize { get; }
    public float OutputScale { get; }
    public float WorkingScale { get; }
    public float MaxWorkingScale { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }

    public OpaqueRenderOutput CreateOutput(Rect logicalBounds, float? density = null);
    public void Publish(OpaqueRenderOutput output);
    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;

    public void UseDeclaredResource<T>(
        int declaredIndex,
        Action<T> use)
        where T : class;
}

public readonly record struct RenderExecutionInputRange(int StartIndex, int Count)
{
    public int EndIndex { get; }
}

public sealed class RenderExecutionInput
{
    public Rect Bounds { get; }
    public EffectiveScale EffectiveScale { get; }
    public PixelRect DeviceBounds { get; }
    public PixelSize DeviceSize { get; }
    public Vector DeviceGridOffset { get; }
    public Rect RasterBounds { get; }
    public Point LogicalOrigin { get; }

    public void Draw(ImmediateCanvas canvas);
    public void DrawDeviceSpace(ImmediateCanvas canvas, Point devicePoint);
    public void UseShader(
        Action<SKShader> use,
        SKShaderTileMode x = SKShaderTileMode.Decal,
        SKShaderTileMode y = SKShaderTileMode.Decal);
    public void UseSnapshot(Action<Bitmap> use);
}

public sealed class OpaqueRenderOutput : IDisposable
{
    public Rect Bounds { get; }
    public EffectiveScale EffectiveScale { get; }
    public RenderCallbackCanvas Canvas { get; }

    public void SetOutputBounds(Rect logicalBounds);
    public void Discard();
}
```

The topology is chosen by the context method, not by an author-supplied semantic flag. Every opaque form is a fusion barrier even when it declares identity bounds. `OpaqueSource` requires `OpaqueRenderBoundsContract.Source`; `OpaqueMap` requires `Map`; combine/expand require `Combine` or `FullInputs`. A custom multi-input backward mapper returns exactly one required region per input; `FullInputs` is the conservative alternative. Invalid counts or rectangles fail planning.

Hit testing is always the CPU-only description contract and is available before execution. `OutputBounds` tests the declared output union, `AnyInput` delegates to input metadata, and `Custom` receives only metadata-safe input views. A custom predicate must be pure, request-lifetime-safe, and must not capture a context, native callback object, or `RenderResource`; pixel-dependent tests use a conservative metadata result instead. Runtime `Publish` cannot replace this predicate.

The callback is invoked only by the executor. `OpaqueSource` invokes it once with no inputs and no input ranges. `OpaqueMap` invokes it once per runtime input element with exactly one session input, one range covering that element, and element-local output bounds, required region, device bounds, and density. `OpaqueCombine` invokes it once with every input stream flattened in authored handle/stream order. `OpaqueExpand` likewise invokes once with all flattened inputs and may return its declared total N-to-M stream. `InputRanges` contains one contiguous `(StartIndex, Count)` range per authored input handle, including zero-count dynamic inputs, so a callback can recover each authored group from `Inputs` without guessing runtime cardinalities. Its session receives materialized borrowed inputs in that order and request-owned methods to acquire, draw, publish, shrink, or discard outputs. A non-empty `InputReadbacks` list has exactly one `RenderInputReadback` declaration per authored input handle, using the same `None`/`All`/local-`Values` semantics as `TargetCommand`; selection remains bound to the authored handle when an earlier dynamic stream changes flattened cardinality. Only selected execution inputs permit `UseSnapshot`, so unrelated inputs do not synchronize or allocate CPU bitmaps. An opaque source has zero authored inputs and therefore cannot declare input readback.

`CreateOutput` acquires from the request owner and returns a transparently initialized output: although pooled contents are undefined at acquire time, the executor clears the allocation inside the already scheduled opaque island before its canvas becomes author-visible. That clear creates neither a separate GPU pass nor a synchronization. The optional finite positive `density` applies only to that output, is capped by `MaxWorkingScale`, and is clamped against that output's own logical bounds and physical allocation limit; null uses the description-wide `WorkingScale`. Dynamic expansion may therefore publish differently sized values at independent effective scales without forcing unrelated outputs to the aggregate-bounds density. Cache payloads retain each published value's actual `EffectiveScale`; the request-level cache identity still keys the declared materialization demand, and publication rechecks `RenderCacheRules` against the sum of the actual output device-pixel areas. Disposing an unpublished output returns it, while `Publish` transfers it back to the request schedule and makes the author lease inert. Runtime output count, bounds, and density are validated against the description. `UseSnapshot` requires selection by the owning input's `RenderInputReadback`. A callback cannot publish a partially constructed output after failure.

`RenderCallbackCanvas` is a non-disposable active-token facade. `LogicalBounds` is the finite semantic allocation/clip region selected from the complete output bounds and resolved requirement. `DeviceGridOffset` is the translation from callback-local logical coordinates to the composition-device grid. `DeviceBounds` is the immutable composition-device footprint of the complete backing target; it contains `PixelRect.FromRect(LogicalBounds.Translate(DeviceGridOffset), Density)` and normally equals it, but may be wider when an existing physical allocation or explicit raster apron is preserved. `RasterBounds == DeviceBounds.ToRect(Density).Translate(-DeviceGridOffset)` and `LogicalOrigin == RasterBounds.Position` identify the complete pixel-aligned callback-local footprint and the logical point represented by backing pixel `(0, 0)`. For a mapped materialization output, the engine pretranslates and clips the supplied `ImmediateCanvas` to `RasterBounds` so author coordinates remain composition-global and antialiasing may write the canonical rounding fringe without changing `LogicalBounds`. A target-attached command/scope retains its declared semantic target clip because it does not remap a standalone backing image, while still reporting its ambient translation-only device grid.

`Use` is one-shot per facade. It invokes the supplied action synchronously with an executor-managed `ImmediateCanvas`, then closes that canvas before returning; a second call or retained use fails. Closing this callback canvas restores state but does not flush, submit, snapshot, or synchronize. Only the request schedule may perform a declared synchronization, including at the containing island boundary. `UseShader` similarly creates a session-owned shader, invokes the action, and disposes it before returning. `UseResource` is a scoped borrow: the callback must not retain or dispose the raw value, and the engine keeps ownership. The facade, session, input, output, and resource token reject use after callback completion.

The supplied `ImmediateCanvas` runs in an engine-only deferred-callback capability mode. The ordinary state stack, allocation-free transform/clip, clear, and drawing calls are allowed only with immutable value arguments, same-session `RenderExecutionInput` views, a resource currently authorized by a nested same-session `UseResource`/`UseShader` scope, or the request-owned `Bitmap` only while its nested same-session `UseSnapshot` action is active. Every resource-bearing canvas entry point validates that capability. `PushLayer`, `PushOpacity`, `PushBlendMode`, `PushOpacityMask`, `PushPaint`, or any other API implemented with `SaveLayer`/a hidden target allocation is rejected; those semantics must be recorded with `TargetLayerScope`/finite value `Layer`, `Opacity`, `Blend`, or `OpacityMask`. Public `Dispose`, `Snapshot`, `DrawNode`, `DrawDrawable`, `DrawBackdrop`, direct target/surface creation or opening, use of an unrelated native/`RenderTarget` object, and any operation that would invoke a legacy raw callback, start a nested renderer, flush, submit, snapshot, or synchronize also throw in this mode. The executor closes the canvas through an internal no-flush path after the action. CPU pixels use the narrowly scoped `UseSnapshot` bitmap capability; nested nodes/subtrees must have been recorded during `Process`; external resources must be declared and borrowed through tokens. A capability violation fails the callback and publishes no output/cache.

`RenderExecutionInput.Draw` accepts only the currently active `ImmediateCanvas` produced by a facade in the same execution session; passing any external, closed, or different-session canvas throws. `Draw` places the complete input image at `RasterBounds`, preserving its physical size and canonical rounding fringe; `Bounds` remains semantic metadata and is never used to stretch the backing image. It resamples only when the effective input supply differs from the callback density. `DrawDeviceSpace` bypasses logical resampling and interprets `devicePoint` in composition-device pixels; the backing-local placement is `devicePoint - Canvas.DeviceBounds.Position`. This makes shifted/cropped output origins explicit without permitting callbacks to draw into an unrelated destination.

Each materialized input exposes immutable composition-device `DeviceBounds`, `DeviceSize == DeviceBounds.Size`, `DeviceGridOffset`, `RasterBounds == DeviceBounds.ToRect(EffectiveScale.Value).Translate(-DeviceGridOffset)`, and `LogicalOrigin == RasterBounds.Position`; executor inputs always have a concrete scale. `DeviceBounds` and `DeviceGridOffset` are propagated with the physical value across materialization and cache capture/hit instead of being recomputed from semantic `Bounds`. `UseShader` supplies a borrowed shader whose local matrix maps composition-global logical coordinates to input-local device coordinates: logical point `p` samples local input point `(p - LogicalOrigin) * EffectiveScale.Value`, with the declared tile modes outside `RasterBounds`. Used on the active same-session callback canvas, this samples the same physical content as `Draw`, including shifted/cropped origins, the antialiasing fringe, and differing input/output densities. The shader must not be retained, disposed, or used with another canvas/session.

`UseSnapshot` is one-shot per input. It invokes the action synchronously with a request-owned `Bitmap` after the declared readback synchronization, then disposes the bitmap before returning. The author must not dispose or retain it; retained use observes an already-disposed object. Callback failure still releases it and preserves the callback exception as primary.

`RenderScaleContract.MaterializeAtWorkingScale` uses feature 003's supply-driven formula, applying the `OutputScale` floor before the authoritative `MaxWorkingScale` ceiling; a lower positive ceiling therefore overrides that floor. `Vector` remains unbounded until a later materialization. `PreserveInputSupply` is a topology contract, not a request to choose one density from an arbitrary list: it is valid only for an element-wise one-input map (`OpaqueMap`, including zero-or-one discard) or a per-fragment replay scope such as `TargetScope`/`RawTargetScope`, where every surviving output has exactly one corresponding input whose supply is copied. `MapInputSupply` has the same one-corresponding-input restriction but applies a pure `EffectiveScale -> EffectiveScale` mapping after that input supply is known. It is the required contract for transform-like density changes that must be recomputed after an `OwningTargetDomain` dependency resolves; returning `Unbounded` preserves deferred rasterization, while a concrete result is capped by `MaxWorkingScale` and the per-buffer dimension limit. Both one-input contracts are rejected for zero-input sources/captures, multi-input combine, and arbitrary expansion; those shapes use `Vector`, `MaterializeAtWorkingScale`, or a `Custom` contract as allowed by their description. `TargetLayerScope` has no author-supplied scale contract and publishes `EffectiveScale.Unbounded`. Validation happens when a description is used by a context method, so one reusable description cannot acquire a different topology meaning accidentally.

`Custom` is the public replacement for a custom render node's former eager working-scale decision. Its pure CPU resolver uses the available input supplies and complete conservative `OutputBounds`; it cannot observe the later `RequiredRegion`. A fragment whose recording metadata is already concrete resolves once while recording. A fragment with an `OwningTargetDomain` dependency does not expose the provisional result through `RenderFragmentHandle` and may re-evaluate the pure resolver during graph-wide metadata resolution after its final input supplies and output bounds are known. The resolver must return a finite value greater than zero. A throw, NaN, infinity, zero, or negative result fails the current recording or graph-finalization transaction and leaves no published output/cache entry; it is never sanitized to `OutputScale` or another fallback. A valid result is capped by `MaxWorkingScale` and clamped against the complete output bounds by the per-buffer 16,384-axis rule before becoming concrete fragment metadata. Later ROI analysis crops the allocation region but never raises or changes that density. The resolver method/key is structural; its returned density is runtime data.

`default(RenderScaleContract)` and `default(RenderHitTestContract)` are uninitialized and rejected. Authors select an explicit named or custom contract.

For `OpaqueMap`, `RenderValueCardinality.Single` means one output per invocation/input and `ZeroOrOne` permits per-input discard; other cardinalities are rejected. `OpaqueCombine` is limited to at most one total output. `OpaqueSource` and `OpaqueExpand` interpret the description cardinality as the total single-invocation result, and only `OpaqueExpand` may declare an arbitrary N-to-M range. Every case preserves authored output order.

Every public description factory takes its pixel-affecting values as one `state` argument and a non-capturing `Action<TSession, TState>`. A capturing `execute` is rejected synchronously with `ArgumentException`. The engine-owned factories named later in this section — `OpaqueRenderDescription.CreateEngineSource`, `CreateBackendBoundary`, and `TargetScopeDescription.CreateValueReplayMap` — are the exception: they still take a capturing callback under a hand-declared identity, and their outputs are cache-reusable. What that buys is the **accidental** channel: the ordinary capturing lambda, which is what an author writes by mistake, can no longer reach the callback at all, and the runtime identity is now the default path rather than a separate `runtimeIdentity:` assertion the author had to remember to keep in step with the closure. That is worth having on its own — the previous shape let a caller omit `runtimeIdentity:` entirely and silently receive request-local caching, and let a declared identity drift out of step with a closure nothing checked against it. It is **not** a proof that every per-frame value is in the cache key, and it cannot be one: `TState : notnull` admits a mutable reference type whose identity is its reference, and the callback body is ordinary code that can read whatever is in scope.

Six channels therefore stay open. An author who uses one owns the completeness of the resulting cache identity:

1. **A mutable object referenced by `state`.** `state` holds the reference and a field behind it changes between frames; the identity compares equal because the reference did not change.
2. **A `static` field read by the `static` callback.** No description member mentions it, and no recording-time rule can see it.
3. **A capturing delegate one level down.** Placed directly in a state tuple this is rejected (see the validation depth below); placed inside a holder object that `state` references it is not.
4. **A borrowed resource whose content changes behind a pinned `cacheKey`/`version`.** A token is deliberately not part of the identity, so the identity cannot observe the change.
5. **A capturing delegate borrowed as a `RenderResource` and read back through `UseDeclaredResource`.** This is a special case of (4) and is the route this document recommends for a callback that needs a request-scoped value. `FilterEffectInputBinding.PublishDeferredPreviews` takes it: its capturing `replace` delegate did not stop capturing, it moved out of the callback closure into a declared resource under an author-declared identity, and that identity is only as complete as its author made it.
6. **A state type whose own equality is coarser than the pixels it feeds.** `state` is compared with `EqualityComparer<TState>.Default`, so a type that overrides `Equals`/`GetHashCode` or implements `IEquatable<T>` to ignore a pixel-affecting member reports two different frames as the same identity. This holds for a reference-typed and a struct-typed state alike, and no recording-time rule can see it: the API cannot know which members of an author's type reach the canvas.

Phase 0's `RenderCacheVerification` is the empirical backstop for exactly these cases. While it is enabled, every selected cache hit also re-executes its producer and compares the two outputs, so an identity that omits a value fails loudly with a `RenderCacheOutputMismatchException` naming the producing node and the first differing device pixel instead of silently serving stale pixels. It is the tool to reach for whenever one of these channels is unavoidable. `RenderCacheIdentityChannelTests` drives them as two-frame renders, pinning the stale result for each and the verification failure for one.

`state` is validated against the lightweight-immutable-key rules to a defined depth. The walk descends through `ValueTuple`/`Tuple` elements whose declared type is itself a tuple, to any nesting depth — a state carrying more than one value has to be a tuple, so that is the aggregate the API itself encourages — and stops at every other aggregate: a custom type's field set is not part of this API's vocabulary, so such a state is validated as one value. A sealed holder object carrying a mutable field or a capturing delegate is therefore accepted, which is channel (3) above. So is a tuple boxed into an `object` element, because the walk dispatches on the declared element type rather than the runtime one. Everything decidable from the closed state type is decided once per closed type and costs nothing per call; an element whose declared type is `object`, an interface, an unsealed class, or a delegate is read by value, because a delegate's capture is a property of its instance rather than of its type.

A callback that needs a request-scoped resource token reaches it through `UseDeclaredResource<T>(declaredIndex, use)` by its position in `resources`, because a token can never be part of a persistent identity. An out-of-range index throws `ArgumentOutOfRangeException` for `declaredIndex` and a mismatched `T` throws `InvalidOperationException` naming both the requested and the declared type. Position is the only address and `T` is the only check on it, so two declared resources of the same type make index 0 and index 1 indistinguishable: a later edit that prepends or reorders `resources` swaps them with the runtime type check still passing. This is documented rather than made verifiable because no available addressing scheme fixes it at an acceptable price — addressing by token is exactly what the non-capturing rule forbids, and addressing by name or by the resource's own `cacheKey` would change the `resources:` parameter shape on all six description families for a hazard that is narrow, local to one `Process` method, and already covered whenever the two resources differ in type. `CreateRequestLocal` is the named opt-out for state that cannot be a key at all; `RawTargetScopeDescription` and `RawTargetCommandDescription` have only that form, because an unguarded external callback is never reusable.

Which of the two addressing modes a session exposes is decided by the channels that reach it cheaply, not by convenience. A callback normally holds a token only by capturing it, and capture is permitted exactly on the channels whose runtime identity does not come from the callback's state: `CreateRequestLocal`, and the declared-identity form where the author supplies a `RenderRuntimeIdentity` separately from what the callback captures. So `OpaqueRenderSession`, `GeometrySession`, `TargetScopeSession`, and `TargetCommandSession` expose both `UseResource(token, use)` and `UseDeclaredResource(index, use)`; `RawTargetScopeSession` and `RawTargetCommandSession` expose only the token form; and `PaintedRenderSession` exposes only the positional form, because `PaintedSource` is state-passing only — its `draw` may not capture, and its `state` is the produced value's runtime identity, which rejects a `RenderResource` element. The one resource a painted source draws does not go through that positional form at all: the primary-resource overload has the recorder declare and lease it and hand it to the callback as a parameter, so the index remains only for the second and later resources. That rejection is not absolute, and the asymmetry does not rest on claiming it is. Channel (3) above — a sealed non-tuple state object the state walk stops at — physically carries a token into any state-passing callback, and the state-passing sessions that do expose `UseResource` accept it there. What that channel is, though, is an enumerated identity channel rather than a way to address resources: the state is the produced value's output-cache runtime identity, so the author who uses it owns the identity contract by hand, and every way of holding the holder costs something. A holder allocated per recording is a fresh reference every frame, so the identity never matches and output-cache reuse is lost outright. Value equality over something stable across recordings is necessary to get any reuse back, but it is not sufficient on its own, and beyond that the outcome is not decided by the holder alone. Which object reaches the output-cache key depends on the channel that recorded the description. Every public `Create<TState>(state, callback, …)` goes through `RenderExecutionChannel.FromState`, which stores a freshly allocated `StateRenderExecutionBinding` per recording; its `Equals` forwards to `EqualityComparer<TState>.Default`, so the author's equality is consulted and a holder reused and mutated in place can be a correct identity if that equality observes the mutation. `PaintedSource` instead publishes `new RenderRuntimeIdentity(state)`, so the runtime component is the holder object itself; both sides of `RenderFragmentOutputIdentity.Equals` are then the same reference, `object.Equals(object, object)` returns on that before reaching the override, and a node that keeps one holder and mutates it in place draws its first frame once and then serves those pixels for every later frame. One holder therefore behaves differently under different sessions. Putting the pixel-affecting state in a declared resource whose version bumps restores correctness in every shape, because the resource's `CacheIdentity` is a component of the key in its own right — but it does not restore reuse to a holder that is a fresh non-value-equal reference every recording, which misses regardless of where the pixel state lives. That spread is the reason this is an escape hatch and not an addressing mode: what it costs is a property of the engine's channel plumbing, not of the author's code. And a token from an earlier, finished request throws when leased — `InvalidOperationException`, "A released render resource no longer retains its request-scoped slot." — so refreshing the token per `Process` is mandatory, which is exactly the write that a reused holder's identity does not observe. That failure is reached only when the callback actually runs, so under an enabled render cache a stale token in an unchanged holder stays latent until the first execution that leases it. Position is the address by design, not by impossibility. `DeclaredResourceAddressingContractTests` pins the two rejections, the open sealed-holder channel, the reuse a per-recording holder loses, the reuse a reused holder keeps, the stale pixels that reuse then serves, and the leftover token's failure and latency, plus the per-session surface. Its holder-shaped states lease the token they carry, so those pins are about the token channel rather than about the mutable-state-holder channel `RenderCacheIdentityChannelTests` already covers. When `structuralKey` is null, the description uses the execution callback's method identity plus operation kind. `RenderScaleContract.Custom`, custom bounds, and custom hit-test contracts likewise default to their delegate method identities. A captured choice that changes operation/binding/topology shape belongs in an explicit equality-stable structural key. Pixel-affecting data is not declared separately: the channel the API provides for it is `state`, which the description publishes as its `RuntimeIdentity`, subject to the six open channels above.

An explicit key is never mandatory on a public factory, and whether the default beats `typeof(TheNode)` depends on where the callback is written. When the callback is a lambda or method declared in the node itself, the method identity names both the node and which callback within it, so `typeof(TheNode)` replaces a finer default with a coarser one and merges every callback that node records. When the callback is built inside a shared helper — a `_ => bounds` closure the helper itself creates, or a delegate the helper receives and forwards — every caller of that helper shares one method identity, so the default is coarser than the node label and carries none of the values the closure captured. That is why the factories whose `structuralKey` parameter is non-optional are exactly the ones that build or forward such a callback: `OpaqueRenderDescription.CreateEngineSource`/`CreateBackendBoundary`, `TargetScopeDescription.CreateValueReplayMap`, `RenderHitTestContract.FromResource`, `BrushRecorder.CreateSourceBounds`, `BrushRecorder.CreatePaintedSource`/`CreatePrimaryPaintedSource`, and the public `RenderNodeContext.PaintedSource` overloads built on them. On those, the key must state whatever the shared callback closes over — for `CreateSourceBounds` the captured source rectangle and dependency count, not only the recording node's type.

The context methods are deliberately named `Opaque*`: an arbitrary callback is never treated as a semantic/fusible map based on author assertion.

## Materialized input description

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class MaterializedInputDescription
{
    public Rect Bounds { get; }
    public EffectiveScale EffectiveScale { get; }
    public PixelRect DeviceBounds { get; }
    public Vector DeviceGridOffset { get; }
    public Rect RasterBounds { get; }

    public static MaterializedInputDescription FromRenderTarget(
        RenderResource<RenderTarget> target,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds,
        Vector deviceGridOffset,
        RenderHitTestContract hitTest);
}
```

A materialized input is already a fusion/cache island boundary. Its `RenderTarget` must be represented by an explicit token: `Borrow` for a repeatable externally owned target or `Own` for a one-shot transfer. Authors cannot wrap a raw target with ambiguous lifetime. `effectiveScale` must be finite, positive, and concrete; `deviceBounds` and `deviceGridOffset` carry the source target's original physical footprint and composition-device phase rather than deriving a new grid from semantic bounds. `RasterBounds == deviceBounds.ToRect(effectiveScale.Value).Translate(-deviceGridOffset)` must contain `bounds`, and the target's device size must equal `deviceBounds.Size`; its format/backend/device/context must be compatible with the request's linear premultiplied RGBA16F pipeline. Backing pixel `(0, 0)` represents `deviceBounds.Position`, so translated, fractional-grid, and apron-bearing targets retain their physical placement across execution and cache reuse. Mismatched targets are rejected rather than silently stretched, cropped, or sampled out of phase. Hit testing uses the same mandatory CPU-only `RenderHitTestContract` as other descriptions. For a source with no logical inputs, authors normally choose `OutputBounds`, `None`, or a pure `Custom`; `AnyInput` is rejected. A custom predicate cannot capture/read the target, a resource token, native state, or an execution/context facade. Internal overloads may represent render-cache, 3D, and decoder sources without widening this public contract.

## Target capture description

```csharp
namespace Beutl.Graphics.Rendering;

public readonly struct TargetCaptureScaleContract
{
    public static TargetCaptureScaleContract MaterializeAtWorkingScale { get; }
    public static TargetCaptureScaleContract PreserveTargetSupply { get; }

    public static TargetCaptureScaleContract Custom(
        Func<RenderScaleContext, float> resolve,
        object? structuralKey = null);
}

public sealed class TargetCaptureDescription
{
    public TargetRegion SourceRegion { get; }
    public Rect Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public TargetCaptureScaleContract Scale { get; }

    public static TargetCaptureDescription Create(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        TargetCaptureScaleContract scale);
}
```

`TargetCapture` is the public typed target-token-to-value edge. It reads the preceding token in the current external root, resolved `TargetLayerScope`, or finite value Layer scope, produces a request-owned immutable value with `RenderValueCardinality.Single`, and advances the order token without invoking an author callback. Its returned fragment has `ContributesValuesToTarget == false`: publishing it anchors the read in painter order but never composites the captured pixels back into the source target. Authors call `ContributeValues(capture)`—or call `ContributeValues` on a downstream transform—at the later point where those pixels should be drawn. This avoids the incorrect assumption that drawing a semitransparent destination over itself with `SrcOver` is identity.

`Bounds` is mandatory, finite, non-empty conservative content metadata even when `SourceRegion` is `Full`; after scope resolution it must be contained by both the source region and current target domain. `SourceRegion.Empty`, `HitTest.AnyInput`, and mismatched bounds are rejected. A globally empty required region may still skip an otherwise valid capture at planning time.

`TargetCaptureScaleContract.MaterializeAtWorkingScale` and `Custom` declare a concrete output-derived sampling boundary. With no value inputs, the standard form starts at `OutputScale`, caps by `MaxWorkingScale`, and applies the 16,384-axis clamp to `Bounds`; `Custom` receives an empty `InputSupplies` list and may use only `OutputBounds`, `OutputScale`, and `MaxWorkingScale` from its `RenderScaleContext` before following the same validation/cap/clamp. Neither form receives enclosing-scope density supply.

`TargetCaptureScaleContract.PreserveTargetSupply` is the explicit lossless alternative for backdrop-style operations. It remains late-bound during recording and exposes `EffectiveScale.Unbounded` on the transaction-scoped handle; during execution it materializes at the linear magnitude of the active target transform — surface pixels per unit of the local logical space the authored `Bounds` are recorded in. A denser enclosing scope therefore remains dense before a later Shader, replay, or other value consumer, and so does a scope made denser by a local scaling transform, which the owning target's own density does not account for.

A single density cannot describe both axes of an anisotropic target transform, and the authored `Bounds` span the local space of both. The resolved density is therefore the geometric mean of the two axis scales, which holds the capture at the target's own pixel count under any transform; taking the finer axis instead would inflate the allocation by the unbounded anisotropy ratio. Axis scales that agree — the common case, including a target with no local transform — reduce to that shared scale exactly, so an isotropic scope still captures at its full supply.

A capture under a singular target transform is empty, not an error: the whole surface collapses onto a point, so no local region has a preimage of non-zero area and there is nothing to sample. Singular here means `Matrix.TryInvert` rejects the transform, which it does whenever the determinant falls under an absolute `10 * MathUtilities.FloatEpsilon` — for a uniform scale, roughly 0.0011 and below, zero included. An element whose scale animates through zero with a backdrop under it therefore renders normally.

The finite authored `Bounds` still provide public bounds and hit-test metadata, and the normal maximum-dimension invariant has already been enforced by the owning target allocation. The built-in backdrop uses this same public density contract plus an internal request-local identity binding; no engine-only density mode is required.

Authors choose the mode deliberately: output-derived modes may downsample a denser scope and require density-sensitive parity coverage, while `PreserveTargetSupply` propagates the active target supply without letting a callback observe or override it.

The captured value may feed Shader, Geometry, opaque, scope, or target-command inputs. Its target-token dependency is threaded into those consumers, materialized once, and may fan out to multiple pure consumers. The capture is a target-read/fusion and whole-subtree-cache boundary and is scheduled/lifetime-counted like every other value; CPU readback occurs only when a downstream declaration actually requires CPU pixels.

The engine's `GraphicsContext2D.Snapshot()` uses the same non-contributing capture anchor and adds only an internal request-local identity binding so a later built-in backdrop node can find its value across sibling transactions. The binding is not a general public side channel.

## Target scope description

```csharp
namespace Beutl.Graphics.Rendering;

public enum RenderDeviceGridMapping : byte
{
    Remapped,
    Preserved,
}

public sealed class TargetScopeDescription
{
    public RenderBoundsContract Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public RenderScaleContract Scale { get; }
    public RenderDeviceGridMapping DeviceGridMapping { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static TargetScopeDescription Create<TState>(
        TState state,
        Action<TargetScopeSession, TState> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
        where TState : notnull;

    public static TargetScopeDescription CreateRequestLocal(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridMapping deviceGridMapping = RenderDeviceGridMapping.Remapped,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null);
}

public sealed class TargetScopeSession
{
    public Rect OutputBounds { get; }
    public Rect RequiredRegion { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
    public RenderCallbackCanvas Canvas { get; }

    public void ReplayInput();

    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;

    public void UseDeclaredResource<T>(
        int declaredIndex,
        Action<T> use)
        where T : class;
}
```

The callback is invoked once per runtime input fragment against the current scoped target, which retains all preceding pixels and is never auto-cleared. It must call `ReplayInput` exactly once while `Canvas.Use` has its managed canvas active; the method replays that fragment on the same target. Missing, duplicate, retained, or out-of-scope replay is a deterministic execution failure. This session uses a narrower capability mode than opaque/Geometry drawing: only save/restore, transform, and clip operations that are mechanically known not to allocate a layer or emit pixels may surround `ReplayInput`; a resource-bearing clip must use a declared borrow. `Clear`, every independent draw, snapshot/readback, `PushLayer`, opacity/blend/paint/mask APIs that internally use `SaveLayer`, any hidden allocation, flush/submit, nested work, and unrelated resource use are rejected. Group isolation uses the typed `TargetLayerScope`; Opacity uses the typed `Opacity` recorder; engine blend/paint/mask nodes use planner-visible typed scope descriptors, and an arbitrary raw layered callback is `LegacyRawCanvas` opaque-external work. Additional pixel emission belongs in `TargetCommand` or an opaque value description. `Bounds`, `HitTest`, and `Scale` map each input's pure metadata; `PreserveInputSupply` keeps its density, while `MapInputSupply` publishes a transform-like density change after the corresponding input supply is known. `DeviceGridSensitivity` and `DeviceGridMapping` are declared planner facts, never inferred from a structural or runtime identity key. They are independent of value-input eligibility and of each other. `PhaseDependent` states that the description's pixels are a function of the device-grid phase — analytic anti-aliased coverage such as glyph or SDF rasterization, and equally screen-space dithering, ordered noise, or a pixel-grid overlay — so the render-output cache neither reuses it across a device-grid phase change nor under a `Remapped` scope ancestor; `Insensitive` is the default. `DeviceGridMapping` states only where the scope replays its input: `Remapped` is the conservative default because a scope callback's whole permitted vocabulary is save/restore, transform, and clip, and `Preserved` is an explicit promise that the callback leaves the target transform alone. A materializing scope may and must still declare `Remapped` when its callback transforms; declaring it never affects eligibility, and neither value contributes to structural plan identity — engine-owned value-replay-map eligibility does, while the mapping is a per-request planning fact. Public `TargetScope` is an opaque fusion boundary even if its bounds look like identity. Engine-proven typed scopes use the same internal fragment shape but may participate in equivalence rewrites.

Finite value `Layer` flattens all supplied streams in authored order into one fragment with exactly one materializable composited value and always publishes `EffectiveScale.Unbounded`. Demand resolution selects its materialization density from every child supply, `OutputScale`, `MaxWorkingScale`, and downstream demand, so a denser downstream consumer can raise the Layer density without changing the Layer's public supply contract (`RenderNodeContext.Layer`, `RenderScaleUtilities.ResolveWorkingScale`). `TargetLayerScope` also flattens a mixed stream but exposes no independent outer value: it publishes `EffectiveScale.Unbounded`, preserves the input streams' aggregate `RenderValueCardinality` for dependency accounting, keeps its initialized `Full`, finite `Region`, or `Empty` target access in the fragment IR, and remains value-ineligible until explicitly localized by finite `Layer`. `TargetCommand` has no independent reusable pixel supply, publishes `EffectiveScale.Unbounded`, and has `RenderValueCardinality.None`; its effectful fragment plus `QueryBounds`/hit-test metadata remain observable. Public target capture has `Single`; output-derived capture modes publish concrete supply while `PreserveTargetSupply` remains `Unbounded` until execution against its active target. Materialized sources, WholeSource Shader, Geometry, and opaque materializations publish concrete supply according to their own contracts.

## Raw target compatibility callbacks

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class RawTargetScopeDescription
{
    public RenderBoundsContract Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public RenderScaleContract Scale { get; }
    public object StructuralKey { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static RawTargetScopeDescription CreateRequestLocal(
        Action<RawTargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null);
}

public sealed class RawTargetScopeSession
{
    public ImmediateCanvas Canvas { get; }
    public Rect OutputBounds { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }

    public void ReplayInput();

    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;
}

public sealed class RawTargetCommandDescription
{
    public Rect QueryBounds { get; }
    public RenderHitTestContract HitTest { get; }
    public object StructuralKey { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static RawTargetCommandDescription CreateRequestLocal(
        Action<RawTargetCommandSession> execute,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null);
}

public sealed class RawTargetCommandSession
{
    public ImmediateCanvas Canvas { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }

    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;
}
```

Both `RawTargetCommandDescription.Create` and `TargetCommandDescription.Create` validate `queryBounds` when the description is created. The accepted domain is every finite `Rect` with non-negative width and height. `Rect.Empty` is the conventional no-query value, although another finite zero-area rectangle is also accepted and preserves its authored origin. A non-finite coordinate/dimension or a negative width/height is rejected synchronously with `ArgumentException` for `queryBounds`.

`queryBounds` and `hitTest` are validated as one query contribution rather than independently. `queryBounds` becomes the recorded fragment's bounds verbatim, so it is the whole region the command reports to Measure and ROI, and a hit outside it is a hit no consumer sized itself for. A zero-area region reports nothing, yet every hit-testing kind still answers true somewhere: `OutputBounds` because `Rect.Contains` is edge-inclusive and an empty rectangle still holds its own origin, `AnyInput` because it delegates to input regions the command never declared, and `Custom` because the callback answers for any point at all. Only `None` is confined to an empty region, so a zero-area `queryBounds` requires it and any other contract is rejected synchronously with `ArgumentException` for `hitTest`. The order-only command idiom — a zero-area `queryBounds` with `RenderHitTestContract.None` — is unaffected. `RawTargetCommandDescription.Create` additionally rejects `RenderHitTestContract.AnyInput`, matching `TargetCaptureDescription.Create` and `MaterializedInputDescription.FromRenderTarget`: a raw command has no logical value inputs, so input hit testing can never report a hit.

A recorder whose bounds are computed at runtime therefore has to derive its contract from those bounds rather than state a constant. `DrawBackdropRenderNode` takes its bounds from the recording `GraphicsContext2D`'s canvas size, which is optional and defaults to `Size.Empty`, so it declares `OutputBounds` only over a positive-area canvas and `None` otherwise.

`RawTargetScope` is invoked once per input fragment and must call `ReplayInput` exactly once. It receives a raw current-target canvas specifically to migrate an existing custom decorator that cannot be expressed through Opacity, Blend, OpacityMask, typed `TargetLayerScope`, finite value `Layer`, or guarded transform/clip TargetScope. Both raw forms conservatively consume/produce the scope's `TargetRegion.Full` token with read/write access because an unguarded callback may draw, clear, snapshot, or touch pixels before/after replay and cannot be mechanically confined. A raw scope's Bounds/HitTest/Scale and a raw command's QueryBounds/HitTest describe only value/query metadata, never a trusted access limit. `RawTargetCommand` is invoked once with no value input and has value cardinality `None`, `EffectiveScale.Unbounded`, and `ContributesValuesToTarget == false`; wrap it in finite `Layer` when its painter result must become a value.

Neither raw callback may dispose or retain the canvas/session/resource, but internal saves, layers, draws, snapshots, flushes, or nested raw hooks cannot be inspected or counted by the planner. Each fragment is therefore a `LegacyRawCanvas`/opaque-external boundary, sets `HasOpaqueExternalWork`, increments `OpaqueExternalExecutions`, disables whole-subtree caching/fusion through itself, and is excluded from exact internal pass/synchronization claims. Raw descriptions deliberately have no runtime cache identity: callback payload binds per request and whole-subtree output caching always bypasses. New code uses the typed vocabulary; the raw forms exist for behavioral completeness, not as optimization assertions. The migration census must classify every old `CreateLambda`/raw-canvas call site as guarded `Opaque*`, typed TargetCommand/capture/scope, RawTargetScope, or RawTargetCommand; no unclassified escape remains.

## Target command description

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class TargetCommandDescription
{
    public TargetRegion AffectedRegion { get; }
    public Rect QueryBounds { get; }
    public RenderHitTestContract HitTest { get; }
    public TargetAccess Access { get; }
    public IReadOnlyList<RenderInputReadback> InputReadbacks { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static TargetCommandDescription Create<TState>(
        TState state,
        Action<TargetCommandSession, TState> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null)
        where TState : notnull;

    public static TargetCommandDescription CreateRequestLocal(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access = TargetAccess.ReadWrite,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        object? structuralKey = null,
        IEnumerable<RenderResource>? resources = null);
}

public readonly struct TargetRegion
{
    public static TargetRegion Full { get; }
    public static TargetRegion Empty { get; }
    public static TargetRegion Region(Rect region);
}

public enum TargetAccess
{
    ReadWrite,
    Readback,
}

public sealed class TargetCommandSession
{
    public IReadOnlyList<RenderExecutionInput> Inputs { get; }
    public IReadOnlyList<RenderExecutionInputRange> InputRanges { get; }
    public Rect AffectedBounds { get; }
    public Rect RequiredRegion { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
    public RenderCallbackCanvas Canvas { get; }

    public void ReplaceAffectedRegion(Color color);
    public void UseSnapshot(Action<Bitmap> use);

    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;

    public void UseDeclaredResource<T>(
        int declaredIndex,
        Action<T> use)
        where T : class;
}
```

`default(TargetRegion)` is uninitialized and rejected. `Region` rejects invalid/non-finite rectangles and normalizes a finite zero-area rectangle to `Empty`; `Full` is resolved only after the current external root, `TargetLayerScope`, or finite value Layer target has a finite domain. `TargetCommandDescription.Create` rejects `TargetAccess.Readback` with `TargetRegion.Empty`, because its required one-shot snapshot cannot produce a non-null zero-area bitmap; an order-only command uses `ReadWrite` plus `Empty`.

Target callbacks execute later against the currently scoped target session and borrowed materialized inputs. Each supplied handle must have `CanBeUsedAsValueInput == true`; recording rejects command-bearing/effect-only fragments or shared-target-scope inputs, which remain ordered through `TargetScope`/`TargetLayerScope` or become a value only through finite `Layer`. A non-contributing public `TargetCapture` is valid because it does expose a value: its prior-token dependency is scheduled before materialization and threaded into the command. Valid inputs are flattened in authored handle/stream order. `InputRanges` contains one contiguous `(StartIndex, Count)` range per authored handle, including zero-count dynamic inputs, so the callback can recover authored groups without guessing their runtime cardinality. A non-empty `InputReadbacks` list has exactly one declaration per authored input handle. `None` schedules no CPU bitmap, `All` selects every runtime value from that handle even when its cardinality is dynamic, and `Values` selects sorted unique local indices within that handle. A null or empty declaration means `None` for every authored input. This per-handle binding keeps a later selection stable when an earlier handle produces a runtime-variable number of values. Only selected flattened execution inputs enable `UseSnapshot`; an unselected input throws without synchronizing or allocating a bitmap. A local index that is impossible for the handle's declared cardinality, or is guaranteed by its minimum cardinality but missing at runtime, fails execution before the command callback. An absent optional value does not materialize a declared readback. This declaration is independent of `TargetAccess.Readback`, which describes the preceding target token.

Every public callback is conservatively target-dependent: `ReadWrite` and `Readback` both consume the prior target token, and `Readback` additionally schedules CPU access. There is no public author-asserted write-only access because ordinary `SrcOver`, inherited opacity/blend/mask state, and most canvas draws read the prior destination. Engine-proven clear/source-replace commands may use an internal write-only classification under an enforceable capability. `TargetRegion.Full` means the complete finite domain of the current root, finite value Layer, or resolved `TargetLayerScope` target; `Empty` is an order-only/no-pixel access, and `Region(Rect)` is a validated finite composition-logical subregion. A built-in clear uses internal write-only `Full`; a destination snapshot uses `Full` readback; a finite backdrop draw uses `ReadWrite` and its bounds. Commands are preserved even when the affected region is `Empty`.

`QueryBounds` and the mandatory CPU-only `HitTest` contract describe this command's visible/query contribution independently of the region it reads or writes; snapshot/readback/clear commands normally use empty query bounds plus `None`, while a backdrop draw over a positive-area canvas uses its declared bounds plus `OutputBounds`. They never authorize command reordering or elimination. Resources must be declared and are borrowed through the same scoped rules as opaque work. A null structural key defaults to the execution callback's method identity alone, matching every other factory: `Access` is already its own component of both the structural plan key and the output-cache identity, so folding it into the default key would only cost an allocation on a path that runs once per node per frame. Shape-changing choices require an explicit key. Pixel-affecting scalar/value data travels as `state`, which becomes the output-cache identity; `CreateRequestLocal` opts out into a fresh request-local cache identity.

For `TargetAccess.Readback`, the executor resolves the finite `AffectedRegion` as the command's `RequiredRegion`, snapshots that subset of the immutable preceding target token, and creates the callback canvas over the same region before invoking the command. `UseSnapshot` must then be called exactly once and supplies that pre-command bitmap synchronously; writes performed by the callback are not reflected in it. The bitmap's local pixel `(0, 0)` represents `Canvas.LogicalOrigin`, and its pixel dimensions match `Canvas.DeviceBounds.Size` (the canvas `RasterBounds` footprint), not the full backing target. The request disposes it before return, retained/disposed-by-author use is invalid, and failure preserves the callback exception while still releasing the bitmap. A callback that needs pixels after an intermediate write must split that work into a target command followed by `TargetCapture`/another command, making the synchronization visible. `ReadWrite` permits GPU-side target access through `Canvas` but does not imply CPU readback.

The command canvas clips ordinary drawing to the resolved affected region and rejects every pixel operation when it is `Empty`. Because the native clear primitive ignores clip state, `Clear` is accepted only for `TargetRegion.Full`. Public authors erase or replace a finite region through `TargetCommandSession.ReplaceAffectedRegion(Color)`, which performs clipped `Src` replacement without exposing unrestricted blend state; an ordinary clipped `SrcOver` draw remains available when destination alpha must be preserved. Every access outside the declaration is a capability violation and fails before cache publication.

Unlike planner-owned opaque/Geometry outputs, `TargetCommandSession.Canvas` is never automatically cleared: it represents the prior target token in the current external root, resolved `TargetLayerScope`, or finite value Layer and must preserve all preceding target content and state. Its close follows the same no-flush rule as every callback canvas.

### Built-in backdrop binding

`SnapshotBackdropRenderNode` records the same non-contributing target-capture fragment through an internal factory that may late-bind the actual scoped target density. It registers a request-local reference-identity binding from its returned built-in `IBackdrop` object to that captured value. A later `DrawBackdropRenderNode` in the same recorded graph consumes the value directly, preserving sequences such as `Snapshot -> Clear -> scoped/filter draw of snapshot`. The planner may realize the capture as a same-backend snapshot/copy or an explicit readback, but it always participates in target-token order, ROI, resource scheduling, and diagnostics; it is never an untracked callback bitmap.

On successful request completion, the existing persistent snapshot behavior may atomically replace the node-owned fallback payload for later-context use; failure publishes neither the request value nor a replacement, and node disposal releases the last committed fallback. No request facade or lease is retained in the node, and there is no second target-read IR kind.

## Bounds contract

```csharp
namespace Beutl.Graphics.Rendering;

public readonly struct RenderBoundsContract
{
    public static RenderBoundsContract Identity { get; }
    public static RenderBoundsContract FullInput { get; }

    public static RenderBoundsContract Create(
        Func<Rect, Rect> transformBounds,
        Func<Rect, Rect> getRequiredInputBounds,
        object? structuralKey = null);

    public static RenderBoundsContract CreateFullInput(
        Func<Rect, Rect> transformBounds,
        object? structuralKey = null);

    public Rect TransformBounds(Rect inputBounds);
    public Rect GetRequiredInputBounds(Rect requestedOutputBounds);
    public bool RequiresFullInput { get; }
}
```

`default(RenderBoundsContract)` is invalid. `FullInput` has identity forward bounds and requests the complete input for every non-empty downstream requirement. `CreateFullInput` combines an author-supplied forward map with that same conservative backward behavior, covering operations that expand or transform output bounds but cannot prove a tight inverse ROI. A custom structural key defaults to the relevant delegate method identity or identities; captured parameter values affect runtime bounds but not structural identity.

Every custom forward, backward, multi-input bounds, scale, and hit-test delegate is deterministic, side-effect-free CPU work. Its captured state must be an immutable request-lifetime snapshot because forward metadata may run during recording while backward ROI and query evaluation run after the complete graph exists. Such delegates cannot capture or consult a recording/execution context, session/handle/facade, `RenderResource` or raw resource, native/media state, clock, random source, or mutable service. Identical inputs and the same captured snapshot must produce the same result; invalid rectangles or non-finite scale results fail validation rather than falling back silently.

## Shader contract

```csharp
namespace Beutl.Graphics.Effects;

public sealed class ShaderDescription
{
    public ShaderDescriptionKind Kind { get; }
    public SkslSource Source { get; }
    public RenderBoundsContract Bounds { get; }
    public IReadOnlyList<ShaderUniformBinding> Uniforms { get; }
    public IReadOnlyList<ShaderResourceBinding> Resources { get; }
    public SKShaderTileMode SourceTileMode { get; }

    public static ShaderDescription CurrentPixel(
        string source,
        Action<ShaderBindingBuilder>? bindings = null);

    public static ShaderDescription WholeSource(
        string source,
        RenderBoundsContract bounds,
        Action<ShaderBindingBuilder>? bindings = null,
        SKShaderTileMode sourceTileMode = SKShaderTileMode.Decal);
}

public enum ShaderDescriptionKind
{
    CurrentPixel,
    WholeSource,
}

public sealed class SkslSource
{
    public string Text { get; }
    public string IdentityHash { get; }
    public ShaderDescriptionKind Kind { get; }
}

public sealed class ShaderUniformBinding
{
    public string Name { get; }
    public object StructuralKey { get; }
    public ShaderBindingCachePolicy CachePolicy { get; }
}

public sealed class ShaderResourceBinding
{
    public string Name { get; }
    public ShaderResourceCoordinateSpace CoordinateSpace { get; }
    public RenderResource Resource { get; }
    public object StructuralKey { get; }
    public ShaderBindingCachePolicy CachePolicy { get; }
}

public enum ShaderBindingCachePolicy
{
    RequestUnique,
    ReuseFromSnapshot,
}

public enum ShaderResourceCoordinateSpace
{
    Value,
    OutputDevice,
}

public sealed class ShaderBindingBuilder
{
    public void Uniform<T>(string name, T value)
        where T : unmanaged;

    public void Uniform(string name, ReadOnlySpan<float> values);

    public void Uniform<T>(
        string name,
        T value,
        Action<ShaderUniformWriter, T, ShaderExecutionContext> bind,
        object? structuralKey = null,
        ShaderBindingCachePolicy cachePolicy = ShaderBindingCachePolicy.RequestUnique)
        where T : unmanaged;

    public void Resource<T>(
        string name,
        RenderResource<T> resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        Action<ShaderResourceWriter, T, ShaderExecutionContext> bind,
        object? structuralKey = null,
        ShaderBindingCachePolicy cachePolicy = ShaderBindingCachePolicy.RequestUnique)
        where T : class;
}

public sealed class ShaderUniformWriter
{
    public void Set<T>(T value) where T : unmanaged;
    public void Set(ReadOnlySpan<float> values);
}

public sealed class ShaderResourceWriter
{
    public void Set(SKShader shader);
}

public sealed class ShaderExecutionContext
{
    public Rect InputBounds { get; }
    public Rect OutputBounds { get; }
    public Rect RequiredRegion { get; }
    public PixelRect DeviceBounds { get; }
    public PixelSize DeviceSize { get; }
    public Point LogicalOrigin { get; }
    public Vector DeviceGridOffset { get; }
    public EffectiveScale InputEffectiveScale { get; }
    public float OutputScale { get; }
    public float WorkingScale { get; }
    public float MaxWorkingScale { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
}
```

`SkslSource`, `ShaderUniformBinding`, `ShaderResourceBinding`, both writers, and `ShaderExecutionContext` have internal constructors. Authors create descriptions through `CurrentPixel`/`WholeSource` and bindings through `ShaderBindingBuilder`; the engine creates writers and execution contexts only for the scoped bind call.

`CurrentPixel` source defines exactly one `half4 apply(half4 color)` entry point. Its input and result are premultiplied linear-light RGBA16F values. It has no output-position coordinate. The validator rejects whole-source entry points, screen/device coordinate built-ins, implicit source sampling, unsupported global declarations, multi-declarator forms that escape renaming, duplicate/reserved bindings, and any construct the merger cannot rename safely. A `CurrentPixel` resource must declare `Value` coordinates and may be sampled only through the restricted value-sampler grammar, whose arguments are proven to derive from the current color and uniform/value data rather than destination position.

Coordinate independence alone does not prove equivalence across analytic or antialiased source coverage. An arbitrary public `CurrentPixel` stage may be nonlinear, so applying it before coverage can differ from applying it to the coverage-resolved premultiplied pixel. A public stage therefore cannot fold across a vector, text, geometry, or other analytic/AA coverage-producing source boundary. The planner first materializes the coverage-resolved source, then may fuse adjacent eligible stages that consume those materialized pixels. Only an engine-owned stage whose premultiplied-coverage homogeneity is mechanically proven for every coverage value may cross such a boundary. There is no public homogeneity assertion, trust flag, or author opt-in escape.

`WholeSource` defines a complete coordinate shader with an implicit upstream `src` child and mandatory bounds contract. It always executes unfused in this feature. Its `main(float2 coord)` receives local output-device pixel coordinates: `(0.5, 0.5)` is the center of the first pixel in `DeviceBounds`, and `LogicalOrigin + coord / WorkingScale` is the corresponding logical point. `LogicalOrigin == DeviceBounds.ToRect(WorkingScale).Translate(-DeviceGridOffset).Position`; `DeviceGridOffset` records the translation used for canonical composition-device rounding. The engine binds `src` so `src.eval(coord)` samples the same logical point from the input, applying the input bounds origin and concrete input density; outside-input sampling uses `sourceTileMode`. A vector input is rasterized at the resolved working density before binding. There is no `WholeSourceInvariant`, `IsCoordinateInvariant`, or author opt-in flag.

`CurrentPixel` is a deferred semantic transform whose description declares no independent scale change. A bare `RenderNodeContext.Shader(input, description)` preserves the input `EffectiveScale`; when the same description is the first surviving `FilterEffectContext.Shader`, the enclosing `FilterEffectRenderNode` may fold its standard or custom working-scale contract into that fragment and choose another density. A chain over a materialized input may stay deferred and fuse into its eventual draw. An unbounded analytic source may stay deferred only across engine-owned stages whose coverage homogeneity is mechanically proven; an arbitrary public `CurrentPixel` stage starts after coverage has been resolved into a materialized input. Each stage's forward metadata is resolved before planning. A concrete predecessor fuses with its successor only when their effective scales are equal; a concrete density change is an explicit `ScaleTransition` boundary. An `Unbounded` predecessor may fuse and adopt the concrete successor/run density. A merged run allocates once at its final runtime-clamped adopted density. Every binder receives stage-local input/output bounds and required region together with that actual run density and its canonical device footprint. The first stage's input effective scale comes from the materialized run input; every later stage receives the run density because that is the concrete output produced by its fused predecessor. Thus fusion and disabled execution expose equivalent binder metadata even when an earlier policy-bearing stage changes density or the allocation limit lowers the planned density. `WholeSource` is itself an unfused materialization boundary and publishes its concrete resolved density immediately from its mapped complete bounds. Later ROI cropping never changes a valid resolved density.

An extra resource binding declares how coordinates passed to its `.eval` are interpreted. `Value` passes author-defined value coordinates unchanged and is the only form accepted by CurrentPixel. `OutputDevice` means the same local device-pixel units as WholeSource `coord`. There is no logical-composition-unit coordinate space; a binder converts to logical units itself through `ShaderExecutionContext`. The resource binder uses `ShaderExecutionContext` to create any required local matrix and density conversion; its declaration, source use, and returned shader are validated as one binding.

Uniform/resource names, parsed source types, order, coordinate spaces, binder method/explicit structural keys, source, kind, tile mode, and bounds behavior are structural. Uniform values, resource identities/versions/contents, final logical/device bounds, required region, output/working/input density, and frame data are runtime. The direct unmanaged `Uniform(name, value)` overload writes the value without an author callback and automatically encodes its validated canonical representation in render-output cache identity. `unmanaged` is only the compile-time ceiling: runtime validation accepts the explicit canonical CPU scalar/vector/matrix allowlist compatible with the parsed SkSL type and rejects pointer-containing/padding-dependent structs, opaque byte blobs, `IntPtr`/`UIntPtr`/`nint`/`nuint`, function/native handles, and any other process-address identity. Canonical identity is derived from validated components, never raw struct memory. The span overload copies its floats into immutable description-owned storage during recording and keys the copied bit pattern; no caller array/memory is retained.

The custom uniform binder overload copies its passed `value` into the immutable description and includes that canonical snapshot automatically in runtime cache identity. `ShaderBindingCachePolicy.RequestUnique` is the default and creates a fresh identity for each recorded custom binding, so a callback that legitimately observes other request-local state cannot publish a cross-request cache entry. `ReuseFromSnapshot` is accepted only for a non-capturing callback and derives reusable binding identity solely from the copied uniform value, or from the resource token's immutable key/version snapshot for a resource binder. Per cache candidate, the containing Shader subtree additionally keys request `OutputScale` and `MaxWorkingScale` plus the resolved complete bounds and required region of every reusable stage and every fragment in its upstream input closure. Combined with the subtree's density and device-grid identity, these values cover every `ShaderExecutionContext` property, including fan-out changes to shared materialized bounds that pass through transparent value wrappers, while an external reusable sibling leaves a binder-free candidate reusable. A static lambda provides the intended compile-time guarantee. Such a callback must read only its callback arguments and `ShaderExecutionContext`; instance and closure state are mechanically rejected, while mutable globals, services, clocks, or other unsnapshotted state are contract violations. Additional pixel-affecting state must be copied into a direct/custom uniform value or represented by a versioned resource instead of being paired with an author-asserted key. A null structural key defaults to the binder method identity; structural keys identify shape only and never substitute for runtime snapshots. `ShaderExecutionContext`, writers, and resource tokens are active only during binding.

`ShaderUniformWriter` validates exactly one value compatible with the parsed uniform type. `ShaderResourceWriter.Set` transfers one newly created native shader to the engine; the engine disposes it after binding/program execution or on failure. A missing/duplicate/incompatible write is an explicit binding failure. `ShaderDescription` intentionally has reference equality; an internal structural key/comparer, not object/record equality, implements plan/program reuse.

Program/source hashes select buckets only. Full source text, description kind, binding signature, backend capability, color/alpha contract, and relevant limits are compared before reuse.

## Geometry contract

```csharp
namespace Beutl.Graphics.Effects;

public sealed class GeometryDescription
{
    public RenderBoundsContract Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
    public bool RequiresReadback { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static GeometryDescription Create<TState>(
        TState state,
        Action<GeometrySession, TState> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        object? structuralKey = null,
        bool requiresReadback = false,
        IEnumerable<RenderResource>? resources = null)
        where TState : notnull;

    public static GeometryDescription CreateRequestLocal(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        object? structuralKey = null,
        bool requiresReadback = false,
        IEnumerable<RenderResource>? resources = null);
}

public sealed class GeometrySession
{
    public RenderExecutionInput Input { get; }
    public Rect OutputBounds { get; }
    public Rect RequiredRegion { get; }
    public PixelRect DeviceBounds { get; }
    public PixelSize DeviceSize { get; }
    public float OutputScale { get; }
    public float WorkingScale { get; }
    public float MaxWorkingScale { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
    public RenderCallbackCanvas Canvas { get; }

    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;

    public void UseDeclaredResource<T>(
        int declaredIndex,
        Action<T> use)
        where T : class;
    public void SetOutputBounds(Rect logicalBounds);
    public void DiscardOutput();
}
```

Geometry applies element-wise to one input stream and is never fused in this feature. Each input produces zero or one output, so it is an order-preserving `N -> 0..N` map; `DiscardOutput` is the only cardinality reduction and Geometry never expands. Its CPU-only hit-test contract is resolved from conservative metadata and cannot depend on executing readback. Each element uses the standard supply-driven `MaterializeAtWorkingScale` rule: start at `OutputScale`, raise to the concrete input supply when denser, cap by `MaxWorkingScale`, and apply the 16,384-axis clamp against that element's complete mapped output bounds. The resulting concrete density is published in the Geometry fragment metadata and supplied as `WorkingScale`/canvas density; later ROI cropping never changes it.

Before each Geometry callback begins, the executor transparently clears its planner-owned output inside the already scheduled Geometry island; undefined pooled pixels are never author-visible, and the clear adds no separate pass or synchronization. The session, shared `RenderExecutionInput`, canvas facade, declared resource tokens, shader-use callbacks, and snapshot bitmap are borrowed for callback duration and follow the same scoped rules as opaque work. `UseSnapshot` throws unless readback was declared and scheduled, and the request disposes its bitmap before the method returns. `SetOutputBounds` accepts only a contained shrink of `OutputBounds`; `DiscardOutput` wins over shrink. A null structural key defaults to the render callback's method identity plus Geometry kind; shape-changing choices require an explicit equality-stable key. Pixel-affecting scalar/value data travels as `state`, which becomes the output-cache identity; `CreateRequestLocal` opts out into a fresh request-local identity and disables cross-request output-cache reuse for the recorded Geometry value. `GeometryDescription` uses reference equality and an internal structural comparer/key.

## FilterEffectContext additions

```csharp
namespace Beutl.Graphics.Effects;

public sealed class FilterEffectContext : IDisposable
{
    // All existing current-main operation members remain available.

    public bool TryGetWorkingScale(out float workingScale);

    public void Shader(ShaderDescription description);
    public void Geometry(GeometryDescription description);

    public RenderResource<T> Own<T>(
        T resource,
        object? cacheKey = null,
        long version = 0)
        where T : class, IDisposable;

    public RenderResource<T> Borrow<T>(
        T resource,
        object? cacheKey = null,
        long version = 0)
        where T : class;
}
```

The existing operation-call surface and authored order remain compatible, but provisional author-time metadata does not. The legacy public `Bounds` property is removed (the engine keeps an internal recording tracker), so authors cannot read the current bounds during `ApplyTo`; symbolic or branch-dependent input makes `WorkingScale` unavailable, so authors must use `TryGetWorkingScale`. The getter delegates to the safe probe and throws when that probe reports no single concrete density (`FilterEffectContext.WorkingScale` delegating to `FilterEffectContext.TryGetWorkingScale`). An operation whose parameters depend on unavailable bounds records a deferred pure bounds mapping and an execution factory/callback that bind from the resolved target bounds. The engine invokes `ApplyTo` once and never replays authoring after metadata resolution. This stricter metadata availability is an intentional compatibility break.

Both new methods append to the same authored item order as existing Skia/color/custom operations. They do not draw, allocate, access a GPU, compile, flush, synchronize, snapshot, or read back. Like the existing bounds-transforming methods, they update the engine-internal recording bounds before returning so a later operation in the same `ApplyTo` call observes the complete conservative result of every preceding item. `ShaderDescription.CurrentPixel` preserves the recording bounds; `ShaderDescription.WholeSource` and `GeometryDescription` apply their `RenderBoundsContract.TransformBounds` to the current value. Runtime Geometry shrink/discard does not reduce this recording-time conservative bound. If a retained preceding legacy custom item already made the recording bounds invalid, the new item joins that existing render-time sequence and the bounds remain invalid; its mapping is applied to the actual runtime target bounds inside the same marked opaque island rather than being split into a planner-visible typed fragment. This compatibility case does not allow a new Shader/Geometry mapping invoked with valid bounds to return Invalid.

Each `Shader`/`Geometry` append is atomic. With valid current bounds, the context validates the description and ownership, invokes the pure forward bounds mapping, and validates the result before committing either the item or the new recording bounds. If validation or that mapping throws, returns an invalid/non-finite rectangle, or otherwise fails, the method leaves its previous item order and recording bounds unchanged. There is no identity fallback. The surrounding engine invocation of `FilterEffect.ApplyTo` is a nested transaction checkpoint over authored items, recording bounds, owned-resource transfers, and borrow registrations: an exception rolls all of them back to the state before that invocation, disposes newly owned resources best-effort, preserves the primary exception, and publishes no partial operation. This applies recursively to effect groups/presenters rather than leaving earlier child items visible after a later child fails.

The existing `CustomEffect` recording method and its `CustomFilterEffectContext`/materialized `EffectTarget` callback surface remain available and execute later, but lower as an explicit legacy opaque-external island. Its physical-footprint contract is explicit:

```csharp
public class CustomFilterEffectContext
{
    public EffectTargets Targets { get; }
    public float OutputScale { get; }
    public float WorkingScale { get; }
    public float MaxWorkingScale { get; }
    public static PixelRect DeviceBufferBounds(Rect bounds, float w);
    public static (int Width, int Height) DeviceBufferSize(Rect bounds, float w);
    public Vector DeviceGridOffset { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
    public void ForEach(Action<int, EffectTarget> action);
    public void ForEach(Func<int, EffectTarget, EffectTarget> action);
    public void ForEach(Func<int, EffectTarget, EffectTargets> action);
    public float ResolveTargetDensity(Rect bounds);
    public EffectTarget CreateTarget(Rect bounds);
    public ImmediateCanvas Open(EffectTarget target);
}

public sealed class EffectTarget : IDisposable
{
    public EffectTarget();
    public EffectTarget(
        RenderTarget renderTarget,
        Rect originalBounds,
        EffectiveScale scale = default);
    public Rect OriginalBounds { get; set; }
    public Rect Bounds { get; set; }
    public EffectiveScale Scale { get; init; }
    public PixelRect DeviceBounds { get; }
    public Vector DeviceGridOffset { get; }
    public Rect RasterBounds { get; }
    public RenderTarget? RenderTarget { get; }
    public bool IsEmpty { get; }
    public EffectTarget Clone();
    public void Draw(ImmediateCanvas canvas);
}

public sealed class SKSLShader : IDisposable
{
    public static SKSLShader Create(string sksl);
    public static bool TryCreate(
        string sksl,
        out SKSLShader? shader,
        out string? errorText);

    public SKRuntimeEffect Effect { get; }
    public SKRuntimeShaderBuilder CreateBuilder();

    public EffectTarget ApplyToNewTarget(
        CustomFilterEffectContext context,
        SKRuntimeShaderBuilder builder,
        Rect bounds);

    public void Dispose();
}
```

`EffectTarget()` and the materialized `EffectTarget(RenderTarget, Rect, EffectiveScale)` constructor remain public for source-less and caller-materialized legacy effects. Only the operation-backed constructor is removed. The materialized constructor takes a shallow copy and derives the canonical zero-offset footprint. `Clone()` preserves `OriginalBounds`, current `Bounds`, `Scale`, `DeviceBounds`, and `DeviceGridOffset`; `EffectTargets.Clone()` uses that path for every retained target.

Direct legacy activation takes its allocation-failure classification from an explicit `RenderIntent` argument that defaults to `RenderIntent.Preview`; its request purpose remains auxiliary. `RenderIntent.Delivery` fails fast — an intermediate buffer that cannot be allocated throws `InvalidOperationException` rather than letting the layer vanish from a delivered frame — while `RenderIntent.Preview` logs the failed footprint and drops that target. `MaxWorkingScale` does not participate in this classification: it bounds the working scale only, so a preview with an infinite ceiling still degrades and a delivery with a finite ceiling still fails fast. Engine-owned execution paths supply their explicit request classification internally:

```csharp
public sealed class FilterEffectActivator : IDisposable
{
    public FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        RenderIntent intent = RenderIntent.Preview);

    public SKImageFilterBuilder Builder { get; }
    public EffectTargets CurrentTargets { get; }
    public float OutputScale { get; }
    public float WorkingScale { get; private set; }
    public float MaxWorkingScale { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }

    public void Flush(bool force = true);
    public void Apply(FilterEffectContext context);
    public SKImageFilter? Activate(FilterEffectContext context);
    public void Dispose();
}
```

The overload that accepts `transformBounds` must provide a conservative finite mapping and keeps that mapping
available to later authored items. The two-argument overload intentionally leaves bounds unknown; it never means
identity. Unknown bounds remain symbolic through recording and resolve during scope-domain lowering to the
complete finite local domain of their owning target, after enclosing transforms, clips, and target scopes are
known. That owner may be the real destination, an explicit root `TargetDomain`, or an enclosing finite target
scope. A target-less root request without such an owner fails before the custom callback is entered. All later
legacy Skia/custom/Shader/Geometry items execute from actual runtime target bounds in the same opaque island.
Only the island's final semantic outputs are cropped to the resolved owning domain; internal opaque allocations
are neither planner-visible nor bounded by that crop. Built-in effects use a finite conservative mapping whenever
one can be derived; only genuinely dynamic effects whose callback may run an arbitrary child effect retain
unknown bounds.

The public render-target `EffectTarget` constructor is a legacy compatibility constructor. It anchors the
backing at `originalBounds.Position`, exposes the backing size through `RasterBounds`, and preserves local
point-placement semantics when `Bounds` moves. Canonical device-cover allocation is reserved for typed
Shader/Geometry execution.

`DeviceBufferBounds(bounds, w) == PixelRect.FromRect(bounds, w)` describes a canonical composition-device cover. Legacy `CreateTarget(bounds)` deliberately keeps its prior local-buffer allocation instead: `DeviceBufferSize` depends only on the logical dimensions (`(int)` at `w == 1`, otherwise `ceil(dimension * w)`), so changing a fractional origin does not change the buffer size. `ResolveTargetDensity(bounds)` likewise applies the legacy dimension-only per-buffer clamp. `DeviceGridOffset`, `DeviceBounds`, and `RasterBounds` record how that local storage is placed on the composition grid without changing what the callback sees through `Open`.

Immediately before each legacy Custom callback, the engine force-materializes surviving inputs to remove renderer-owned aprons. A target that the callback creates, retains, or repositions keeps its local raster placement through execution; scale-one replay uses the historical direct point composite, and no canonical normalization pass is inserted. If a legacy effect translates `Bounds` without reallocating, the backing translates by the same logical delta while preserving its physical size. Semantic `Bounds`, measurement, hit testing, and ROI publication remain separate from that backing footprint. The typed `Shader` and `Geometry` paths use canonical device covers and guarded callback canvases independently of this compatibility behavior.

That legacy callback is not handed the new capability-guarded `RenderCallbackCanvas`; its internal raw target/canvas passes, snapshots, or flushes are intentionally uninspectable. Nothing may fuse through it, and diagnostics set `HasOpaqueExternalWork` rather than pretending its internal physical pass/synchronization count is known. New custom work should use Shader, Geometry, or the explicit opaque render-node descriptions for fully planned ownership and diagnostics.

The same rule applies to every retained public/protected raw-`ImmediateCanvas` author hook found by the migration census, including arbitrary `IBackdrop.Draw(ImmediateCanvas)` implementations and `AudioVisualizerDrawable.Resource.RenderForeground(ImmediateCanvas, Rect)` plus their shape callbacks. Their source APIs remain callable without the new capability restrictions, so execution is a marked `LegacyRawCanvas` opaque-external fragment and no fusion crosses it. The built-in request-local backdrop path above is typed and does not invoke `IBackdrop.Draw`; an unrelated/custom backdrop remains external. New deferred opaque/Geometry callbacks receive the guarded canvas and cannot call `ImmediateCanvas.DrawBackdrop` to smuggle a legacy callback into a planned island.

`Clone` and `CreateChildContext` preserve the synchronously updated bounds/order semantics and share request-owned resource slots safely. A clone starts with the source's current `Bounds` and ordered items; a child starts from that current `Bounds` as its `OriginalBounds`, matching existing behavior. Disposing a context that was never transferred releases its resources; successful transfer moves ownership once into the renderer request.

The existing abstract entry point remains:

```csharp
public abstract void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource);
```

`FilterEffect.Resource.CreateRenderNode()` also remains. A node that only customizes the filter working scale
overrides the protected hook while retaining the base `Process` lowering:

```csharp
protected virtual RenderScaleContract? GetWorkingScaleContract();
```

Returning `null` uses the standard `MaterializeAtWorkingScale` policy, including its `s_out` floor before the
authoritative `MaxWorkingScale` ceiling; a lower positive ceiling therefore overrides that floor. The protected
hook may instead return any valid `RenderScaleContract`; an explicit `Custom` result may intentionally be below
`s_out` and is not raised to the standard floor. After the base implementation has identified the finite or
owner-relative isolation needed for mixed/value-ineligible inputs, it folds the standard or custom policy into the
first surviving Shader, Geometry, or legacy operation. The callback is invoked per surviving branch with exactly
one `InputSupplies` item and that branch's isolated effect-input bounds as `OutputBounds`; those are not the later
first-operation output bounds. Legacy multi-input lowering aggregates the densest concrete branch result and falls
back to `OutputScale` only if every branch remains `Unbounded`. Allocation footprints are independent of callback
cardinality: before an opaque Custom callback they retain each branch's local-origin transforms and intermediate
materializations, so empty space in a sparse union is not backing storage. The forced compatibility materialization
immediately before callback entry removes renderer-owned aprons. Callback-created targets retain the legacy
dimension-only local allocation and authored raster placement through final replay. Because a Custom callback may
combine or split targets without declaring topology, the first such callback unions the transformed branch results
and collapses later analysis to that aggregate domain.
The pure contract is reevaluated after a symbolic `TargetLayerScope(Full)` resolves against its actual owner.

The base records no identity fragment or extra opaque/pass boundary. If `ApplyTo` records no items, the node
publishes its original inputs, commits no provisional isolation, and rolls back untransferred owned resources. The
hook and resolver remain lazy for an unprobed no-op; `ApplyTo` can still evaluate them by explicitly reading
`WorkingScale`/`TryGetWorkingScale`. `TryGetWorkingScale(out float)` returns `false` (and `WorkingScale` throws)
while the nominal effect-input density is symbolic or branch-dependent. Operation-specific device math belongs in
the execution context because a later expanding output may apply its own buffer clamp. If opaque runtime behavior
still produces a larger physical `RasterBounds.Union(Bounds)` than its pure bounds contract declared, normalization
reapplies the exact 16,384-axis clamp, resamples at the reduced density, and publishes that actual
`EffectiveScale`/`DeviceBounds`; it never merely retags the pixels. Custom nodes that replace
effect topology or lowering for another reason still implement the new `void Process` contract directly.

## Public authoring examples

### Pass-through

```csharp
public override void Process(RenderNodeContext context)
{
    context.PassThrough();
}
```

### Current-pixel Shader node

```csharp
private static readonly ShaderDescription s_invert = ShaderDescription.CurrentPixel(
    "half4 apply(half4 color) { return half4(color.a - color.rgb, color.a); }");

public override void Process(RenderNodeContext context)
{
    foreach (RenderFragmentHandle input in context.Inputs)
    {
        context.Publish(context.Shader(input, s_invert));
    }
}
```

### Scope-relative isolation versus a reusable Layer value

```csharp
public override void Process(RenderNodeContext context)
{
    // Full remains relative to the eventual current target. This fragment is
    // ordered and effectful, but cannot be passed directly to Shader/Geometry.
    RenderFragmentHandle isolated = context.TargetLayerScope(
        context.Inputs,
        TargetRegion.Full);

    // A value consumer must choose a finite logical domain explicitly.
    RenderFragmentHandle value = context.Layer([isolated], _finiteDomain);
    context.Publish(context.Shader(value, s_invert));
}
```

Publishing `isolated` directly preserves `PushLayer(default)`-style current-target group isolation. Wrapping it in finite `Layer` deliberately changes the public topology by producing exactly one reusable value.

### Existing FilterEffect lifecycle with Shader

```csharp
public override void ApplyTo(FilterEffectContext context, Resource resource)
{
    float amount = resource.Amount;
    context.Shader(ShaderDescription.CurrentPixel(
        "uniform float amount; half4 apply(half4 color) { "
        + "return mix(color, half4(color.a - color.rgb, color.a), amount); }",
        bindings => bindings.Uniform("amount", amount)));
}
```

These examples record descriptions only. They create no native shader and perform no execution during author callbacks.
