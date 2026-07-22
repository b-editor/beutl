# Public API Contract

This contract fixes the public authoring shape for feature 004. Names and responsibilities below are normative for implementation; ordinary framework details such as XML documentation and nullable annotations may be completed without changing the model.

## Namespaces

- Render-node/request authoring: `Beutl.Graphics.Rendering`
- Shared Shader/Geometry authoring: `Beutl.Graphics.Effects`
- Existing geometry, color, and media value types remain in their current namespaces.

No `EffectGraphBuilder`, `RenderPlanBuilder`, public executable operation hierarchy, or parallel compatibility namespace is introduced.

## Construction convention

For new or reshaped types in these excerpts, constructor accessibility is normative: a public class is externally constructible only when a public constructor is explicitly shown. `RenderFragmentHandle`, `RenderNodeContext`, `RenderNodeRasterization`, both `RenderResource` classes, all description/binding/source classes created through a displayed factory or builder, and every hit-test/scale/execution/session/input/output/canvas/writer context have internal constructors. In particular, out-of-tree code cannot instantiate or subclass an unvalidated description, fabricate a resource token, or create an execution facade. Public value structs may be constructed as shown, but every contract struct whose `default` is documented invalid is validated before use. `RenderNodeRendererOptions` keeps its public parameterless constructor for object-initializer use. Existing `FilterEffectContext` construction and members retain their current accessibility unless this contract explicitly changes them; its public `(Rect bounds, float outputScale = 1f, float workingScale = 1f)` constructor remains.

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

`CanBeUsedAsValueInput` is conservative transaction-memoized metadata. It is true only when the fragment exposes every possible runtime value as a materializable value stream without replaying a target-state scope or an effect-only fragment. A target capture is true even though its explicit preceding-token dependency remains scheduled, so this property does not promise purity or request-independent execution. The result is fixed at recording by these rules:

| Recorder/result | Input requirement | Result |
|---|---|---|
| `OpaqueSource`, `MaterializedInput`, `TargetCapture`, `Layer` | Their own descriptor validation; `Layer` accepts mixed fragments | `true` |
| `Shader`, `Geometry`, `OpaqueMap` | Primary input MUST already be `true` | `true` |
| `OpaqueCombine`, `OpaqueExpand` | Every input MUST be `true`; an allowed empty input list is vacuously eligible | `true` |
| `ContributeValues` | Input MUST be `true` | preserves `true` |
| `Opacity` | Any fragment may be wrapped | `true` only for a value-input-eligible child; otherwise `false` |
| `OpacityMask` | Any fragment may be wrapped | `true` only when the primary child and every lowered mask dependency are value-input eligible and no `LegacyRawCanvas` fallback is required; otherwise `false` |
| `Blend` | Any fragment may be wrapped | `false`, because the result retains a dependency on the current destination even for a pure child |
| `TargetLayerScope`, `TargetScope`, `RawTargetScope`, `TargetCommand`, `RawTargetCommand` | As declared by their APIs | `false` |
| nested `RecordSubtree`/`RecordNode` result | Child-defined | preserves each returned handle's recorded value |

A mixed command/value fragment is therefore false until an explicit `Layer` localizes and materializes its painter result. It is legal to publish the same handle more than once as explicit fan-out when the fragment is pure; effectful fragment fan-out is rejected except for a target capture, whose one scheduled materialization may feed multiple pure consumers. Non-friend public-contract tests MUST assert every table row, including a `Shader -> Opacity -> Shader` chain whose opacity result remains true and a pure-child `Blend` result that remains false.

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

    public void Render(ImmediateCanvas destination);
    public RenderNodeRasterization Rasterize();
    public RenderNodeMeasurement Measure();
    public bool HitTest(Point point);
    public void Dispose();
}

public sealed class RenderNodeRendererOptions
{
    public RenderIntent Intent { get; init; } = RenderIntent.Preview;
    public Rect? TargetDomain { get; init; }
    public Rect? RequestedRegion { get; init; }
    public float OutputScale { get; init; } = 1f;
    public float MaxWorkingScale { get; init; } = float.PositiveInfinity;
    public bool UseRenderCache { get; init; } = true;
    public IRenderTargetFactory? TargetFactory { get; init; }
}

public interface IRenderTargetFactory
{
    RenderTarget? Create(PixelSize deviceSize);
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

The node-renderer constructor snapshots and sanitizes options. A non-finite or non-positive `OutputScale` becomes `1`; a NaN or non-positive `MaxWorkingScale` becomes positive infinity, while a positive finite value or positive infinity is preserved. `TargetDomain` and `RequestedRegion` are expressed in the root node's composition-logical coordinate space before the destination canvas's active transform. A non-null `TargetDomain` must be finite and non-empty. It supplies the root target only for target-less `Rasterize`, `Measure`, and `HitTest`; `Render(ImmediateCanvas)` and production Frame requests always use the actual destination viewport and ignore this option. A target-less request with null `TargetDomain` remains valid for self-bounded work, but graph finalization rejects every published root `TargetRegion.Full` access whose enclosing target still has no finite domain. Authors set `TargetDomain` or use a finite `TargetRegion.Region`; neither `RequestedRegion` nor query bounds are inferred as a substitute. `RequestedRegion = null` selects the complete conservative `OutputBounds` computed for the root. A non-degenerate non-null rectangle selects its intersection with that output as the final output requirement/commit crop, so a wholly outside selection is a successful empty result rather than a transparent padded bitmap. An explicitly degenerate rectangle is a valid empty request and preserves its authored bounds and shifted origin. An invalid non-null rectangle is rejected. `RequestedRegion` never replaces or shrinks the available `TargetDomain` used by target reads and scope-relative effects.

Standalone `RenderNodeRenderer.Render` and `Rasterize` always create `Auxiliary` execution requests; only the production `Renderer` creates `Frame` requests through an internal entry point, and cache warm-up is likewise internal. `Measure` and `HitTest` create metadata-only `Bounds`/`HitTest` requests and emit internal diagnostic snapshots. `OutputScale` is the density for target-less rasterize/metadata calls. `Render(ImmediateCanvas)` instead uses the destination's active `Density` as the request output scale and the lesser of the option and destination maximum-working-scale ceilings; it never silently resamples through the option scale. `TargetFactory` replaces `RenderNodeProcessor.CreateRenderTarget` extensibility and is called only when the renderer-owned pool cannot satisfy a materialization. A null factory selects the engine's standard current-backend RGBA16F `RenderTarget.Create(deviceSize.Width, deviceSize.Height)` factory; a selected factory returning null follows the characterized `Intent` failure policy. Targets created by the factory are owned by that pool and are reused or evicted there.

`Render` executes against the borrowed destination exactly as if the root fragments were drawn at the call site: it honors the canvas's active logical transform, clip, opacity, blend mode, coordinate-space density, and prior destination pixels. The finite root target domain is the canvas logical viewport mapped conservatively back through the active transform; `RequestedRegion` is a separate final-output requirement/commit clip, not a shrink of the available target. `TargetRegion.Full` resolves during scope-token lowering to the complete finite domain of its current external root, resolved `TargetLayerScope`, or finite value Layer, so backward ROI may expand target reads for blur/filter aprons up to that domain. The active clip remains an additional exact execution constraint. The renderer snapshots destination state for the synchronous request, restores it before returning, and never implicitly clears, closes, disposes, flushes, submits, or snapshots the caller's canvas. A direct-to-root optimization is legal only when it is observably equivalent to that state; otherwise the planner materializes and performs the final composite.

`Measure.OutputBounds` is the root's complete conservative pixel-output extent before `RequestedRegion`: it unions contributing value bounds with the resolved affected regions of every potentially pixel-writing root target effect, applies enclosing scope mappings/clips, and clips to the finite root target domain when one exists. Effects localized inside a finite value `Layer` contribute through that Layer's value bounds instead of being counted again at the root. Engine-proven read-only captures and order-only effects do not enlarge it; public target callbacks and raw forms are conservatively potentially writing. A Full write therefore contributes the resolved root domain even when its query metadata is empty. `Measure.QueryBounds` separately unions contributing value query bounds and target-command/scope query metadata for bounds queries and hit testing; non-contributing capture/read anchors do not enlarge it. Both are independent of `RequestedRegion`.

`ValueCardinality` counts all materializable values, including non-contributing captures, so a command-only root reports `None`. `HasFragments` distinguishes that command from a node that published nothing; `HasTargetEffects` is true for any command, scope, capture, or other target-token/read dependency and distinguishes those from a pure value stream; `HasContributingValues` reports whether automatic value compositing is present. Publication, effect, value, contribution, output extent, and query extent remain distinct facts. Effective scale is the densest declared value supply and is `EffectiveScale.Unbounded` when no materializable value declares a finite supply. `HitTest` examines contributing values and target-command/scope query metadata in reverse painter order but returns false for a point outside a non-null `RequestedRegion`. Neither metadata call executes dynamic shrink/discard/expansion or pixel callbacks.

`Rasterize` creates a transparent private target with identity transform, `SrcOver`, opacity one, and `Options.OutputScale`, then executes the complete fragment stream including target commands and internal target-read values. `RenderNodeRasterization.Bounds` is the clipped final commit bounds for a non-degenerate `RequestedRegion`, the authored zero-area bounds for an explicitly degenerate request, or `Measure.OutputBounds` when no region was requested. A non-empty domain returns a non-null owned `Bitmap`, and that bitmap's local `(0, 0)` represents `Bounds.Position`; `OutputScale` is the sanitized density actually used for that raster target. The planner may allocate a larger internal target/read apron than the returned crop when backward ROI requires neighboring destination pixels; those pixels are never exposed in the result.

A finite zero-area selected domain is a successful empty result: `IsEmpty == true`, `Bitmap == null`, and `Bounds` preserves that logical empty domain, including its origin; no target is allocated and no pixel callback executes. For a non-empty selected domain, `IsEmpty == false` and `Bitmap != null`, even when every returned pixel is transparent. `RenderNodeRasterization` exclusively owns its bitmap; `Dispose` is idempotent and disposes it, while disposing the renderer does not dispose already returned rasterizations. Callers dispose the result rather than retaining or independently disposing its `Bitmap`. Allocation failures for non-empty results continue to follow `Intent` and are never reported as a successful empty result. There is no list-returning compatibility rasterizer: a fragment stream has one painter-ordered result, and callers that need individual semantic values model them as separate roots/requests.

Each non-null `IRenderTargetFactory.Create` result transfers exclusive ownership to the renderer immediately. It must be a fresh, unleased target of exactly the requested device size, compatible with the current device/context and the pipeline's linear premultiplied RGBA16F format; returning an external, shared, cached elsewhere, already-leased, or previously returned live target is invalid. The renderer validates observable compatibility before use. It disposes an invalid non-null return under the transferred-ownership rule and then follows the request's allocation-failure policy; a factory exception remains the primary failure. The factory is invoked only on the owning render lifetime/thread and must not retain a lease to its return value.

`RenderNodeRenderer` owns its persistent structural-plan/program caches, target pool, and every factory-created target while it remains in that pool or a request lease. Successful render-cache publication transfers the captured payload into the existing `RenderNodeCache` ownership/invalidation lifecycle; it is no longer a pool lease. `Dispose` is idempotent, rejects every later public call, and releases renderer-owned resources best-effort while preserving the first disposal failure. It does not dispose `Root`, `Root.Cache`, `TargetFactory`, a borrowed render destination, or an already returned `RenderNodeRasterization`. Concurrent calls on one instance are unsupported. Distinct instances may execute concurrently only when their node/cache graphs, destinations, and externally borrowed mutable resources are disjoint; callers must serialize instances that share any of them.

## RenderNodeContext

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class RenderNodeContext
{
    public IReadOnlyList<RenderFragmentHandle> Inputs { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
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
- Public `Layer(inputs, domain)` composes every supplied fragment stream into one target-local ordered sequence bounded by a finite, non-empty `Rect`; invalid or empty domains are rejected because Layer promises exactly one materializable value. It is a normal bottom-up value constructor over already-recorded handles, so it never accepts `TargetRegion.Full`: the enclosing scope is not yet known, and freezing a root-sized guess would make metadata unsound under a later parent Layer/transform/clip. A nested command consumes this layer's local target token, not the external root token. When every input has concrete metadata, the Layer value's content bounds are the union of contributing child-value bounds and every potentially pixel-writing child target effect's affected region after scope maps, clipped to `domain`, and hit testing uses explicit child query metadata. If any input is symbolic, the explicit finite Layer resolves its public metadata conservatively to the complete `domain` for bounds and domain containment for hit testing. Public `TargetCommand` and both raw target forms are conservatively pixel-writing whenever their resolved region is non-empty; engine-proven captures/read-only effects do not add their access region.

`FilterEffectRenderNode` has an engine-only isolation path for an ineligible target-effect stream; it does not broaden the public `Layer` API. If every contributing value and target write has an owner-independent finite affected region, the engine records the ordinary finite Layer over that union. If a Full/raw write still needs its parent scope, it records an internal owning-domain Layer whose public metadata remains symbolic, threads it through parent transforms/clips normally, and resolves its allocation domain during graph-wide scope lowering. In both cases the filter remains in authored order and no root-sized recording placeholder becomes authoritative.
- `TargetLayerScope(inputs, region)` is the typed current-target counterpart for group isolation without exposing an outer value. It accepts an authored mixed fragment sequence and any initialized `TargetRegion`. It records bottom-up like every other context method, keeps Full symbolic while later parents add transform/clip/Layer scopes, and resolves the region only during final scope-token lowering against the actual current external root, parent `TargetLayerScope`, or finite value Layer domain; `Empty` is a valid order-only/no-pixel scope. Its handle preserves the supplied streams' ordered aggregate `RenderValueCardinality`, because those dependency values still exist inside the scoped fragment, but has `ContributesValuesToTarget == false` and `CanBeUsedAsValueInput == false`: replaying a target-dependent scope is required to reach them. Its internal recorded hints contain child query metadata only and never pretend that an unresolved affected region is authoritative reusable metadata; public Try queries return false when that dependency is symbolic. It remains an ordered potentially-writing target effect when its resolved region and child writes are non-empty, so root `OutputBounds` includes that affected region even when query metadata is empty.
- At execution, a non-empty `TargetLayerScope` replays the complete mixed stream once into a transparently initialized planner-owned local target and composites that target once into the preceding current target; an Empty scope preserves ordering while scheduling no pixel work. The isolation target and composite are retained unless the planner proves their removal observationally equivalent; direct replay is not a default optimization because overlapping translucent children and destination interaction can change pixels. A Full scope is target-dependent and cannot be cached independently of its preceding target token. Authors that need pixels for Shader, Geometry, another value consumer, or a reusable cache value deliberately wrap the effect fragment in finite `Layer(inputs, domain)`.
- Existing `GraphicsContext2D.PushLayer(default)`/`LayerRenderNode(default)` records `TargetLayerScope(context.Inputs, TargetRegion.Full)` from its ordinary bottom-up `Process`; there is no pre-order traversal exception or early domain resolution. A non-default finite legacy limit records the finite value `Layer`. Target-less finalization of a published root Full scope still requires `RenderNodeRendererOptions.TargetDomain`. Engine-owned semantic consumers may carry an ineligible `TargetLayerScope` through typed effect lowering until its target domain is known, but they do not fabricate a public value-eligible handle; public value consumers use finite `Layer` explicitly.
- `TargetScope` is an order/cardinality-preserving per-fragment map for allocation-free transform/clip state. It replays each input exactly once on the same target. `Opacity`, `Blend`, and `OpacityMask` are planner-visible typed layer scopes. `OpacityMask` declaratively snapshots the `Brush.Resource` during recording; `brushBounds` remains the brush coordinate/mapping frame used by the existing `PushOpacityMask`, not a clip or transparent-outside region. The recorder copies scalar brush state synchronously, includes brush version, mapping bounds, invert, and nested resource identities in output-cache identity, converts every retained image/drawable/native payload to request-owned internal borrow slots before `Process` returns, lowers solid/gradient/perlin/image masks to internal shader/resource dependencies, and records DrawableBrush content as inherited nested fragments. It never retains an undeclared raw brush, invokes `BrushConstructor`, or starts a renderer in the execution callback. Unknown retained custom-brush behavior lowers to `LegacyRawCanvas` rather than being mislabeled exact. A scope is distinct from `Layer`: it does not independently define a mixed child sequence. The planner may canonicalize a typed scope into a pure value edge only after proving target-state equivalence.
- `RawTargetScope` is the explicit migration escape hatch for an old custom decorator whose raw canvas behavior cannot be expressed by the typed vocabulary. It is always opaque-external, cannot fuse or make exact whole-request pass/synchronization claims, and is not the default for new code.
- `RawTargetCommand` is the zero-input counterpart for an existing raw callback that directly reads or mutates the current painter target. It is never used as a guarded value-source API; new independent sources use `OpaqueSource` or `MaterializedInput`.
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

The request family maintains one raw-resource table keyed by reference identity. A second `Own` of the same raw object, or any `Own`/`Borrow` mixture for that object, is rejected during recording before another transfer occurs. Repeated `Borrow` registrations of the same object with an explicit non-null key coalesce onto one request-family slot only when their cache keys compare equal and versions match; an explicit mismatch is rejected. Each null-key registration receives a distinct request-local slot/identity and never coalesces. The same valid token or coalesced borrowed slot may be declared by multiple descriptions, and each execution access remains callback-scoped.

The internal base constructor prevents out-of-tree subclasses or fabricated tokens; arbitrary author resources are represented only by engine-created `RenderResource<T>` from `Own` or `Borrow`.

`cacheKey` and `version` are runtime output-cache identity, never structural-plan identity. For either `Own` or `Borrow`, a null key creates a unique request slot identity, which is safe but prevents cross-request pixel-cache reuse. Authors increment `version` whenever pixel-affecting contents change under a non-null key. Every description lists the resource tokens it may borrow; the recorder automatically incorporates their identities/versions into output-cache keys and rejects undeclared tokens at execution.

`RenderRuntimeIdentity` is the matching non-resource channel for pixel-affecting scalar/value state captured by a deferred callback. Its `Key` must be non-null, immutable for its equality lifetime, and equality-stable; tuples and immutable records are typical keys. `default(RenderRuntimeIdentity)` and an explicitly null key are invalid and rejected by description/binding factories. Those factories accept a nullable identity: nullable `null` makes the recorder synthesize a fresh request-local identity on every recording, which is always correct but prevents cross-request pixel-cache reuse for that value or command. Runtime identity participates in render-output cache identity only and never in structural plan/program identity.

Every explicit `structuralKey`, `RenderRuntimeIdentity.Key`, and resource `cacheKey` may be retained by a structural/program/render cache beyond the recording request. It must therefore be a lightweight, immutable, equality-stable CPU identity such as a `Type`, string, primitive/value tuple, or immutable record composed of such values. Keys must not be a context/session/handle/facade, `RenderResource`, delegate closure, mutable collection or graph, `IDisposable`, native/target object or handle, or a large payload. Hashes select buckets only; complete key equality decides identity. When a large or native object needs identity, authors supply a small immutable ID/version key instead of the object itself.

## Opaque compatibility descriptions

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderDescription
{
    public RenderOperationBoundsContract Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public RenderValueCardinality ValueCardinality { get; }
    public RenderScaleContract Scale { get; }
    public bool RequiresReadback { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static OpaqueRenderDescription Create(
        Action<OpaqueRenderSession> execute,
        RenderOperationBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
        bool requiresReadback = false,
        IEnumerable<RenderResource>? resources = null);
}

public sealed class RenderOperationBoundsContract
{
    public static RenderOperationBoundsContract Source(Rect outputBounds);
    public static RenderOperationBoundsContract Map(RenderBoundsContract bounds);

    public static RenderOperationBoundsContract Combine(
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>> getRequiredInputBounds,
        object? structuralKey = null);

    public static RenderOperationBoundsContract FullInputs(
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
    public Rect RasterBounds { get; }

    public void Use(Action<ImmediateCanvas> draw);
}

public sealed class OpaqueRenderSession
{
    public IReadOnlyList<RenderExecutionInput> Inputs { get; }
    public Rect OutputBounds { get; }
    public Rect RequiredRegion { get; }
    public PixelRect DeviceBounds { get; }
    public PixelSize DeviceSize { get; }
    public float OutputScale { get; }
    public float WorkingScale { get; }
    public float MaxWorkingScale { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }

    public OpaqueRenderOutput CreateOutput(Rect logicalBounds);
    public void Publish(OpaqueRenderOutput output);
    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;
}

public sealed class RenderExecutionInput
{
    public Rect Bounds { get; }
    public EffectiveScale EffectiveScale { get; }
    public PixelRect DeviceBounds { get; }
    public PixelSize DeviceSize { get; }
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

The topology is chosen by the context method, not by an author-supplied semantic flag. Every opaque form is a fusion barrier even when it declares identity bounds. `OpaqueSource` requires `RenderOperationBoundsContract.Source`; `OpaqueMap` requires `Map`; combine/expand require `Combine` or `FullInputs`. A custom multi-input backward mapper returns exactly one required region per input; `FullInputs` is the conservative alternative. Invalid counts or rectangles fail planning.

Hit testing is always the CPU-only description contract and is available before execution. `OutputBounds` tests the declared output union, `AnyInput` delegates to input metadata, and `Custom` receives only metadata-safe input views. A custom predicate must be pure, request-lifetime-safe, and must not capture a context, native callback object, or `RenderResource`; pixel-dependent tests use a conservative metadata result instead. Runtime `Publish` cannot replace this predicate.

The callback is invoked only by the executor. `OpaqueSource` invokes it once with no inputs. `OpaqueMap` invokes it once per runtime input element with exactly one session input and element-local output bounds, required region, device bounds, and density. `OpaqueCombine` invokes it once with every input stream flattened in authored handle/stream order. `OpaqueExpand` likewise invokes once with all flattened inputs and may return its declared total N-to-M stream. Its session receives materialized borrowed inputs in that order and request-owned methods to acquire, draw, publish, shrink, or discard outputs.

`CreateOutput` acquires from the request owner and returns a transparently initialized output: although pooled contents are undefined at acquire time, the executor clears the allocation inside the already scheduled opaque island before its canvas becomes author-visible. That clear creates neither a separate GPU pass nor a synchronization. Disposing an unpublished output returns it, while `Publish` transfers it back to the request schedule and makes the author lease inert. Runtime output count, bounds, and density are validated against the description. `UseSnapshot` requires declared readback. A callback cannot publish a partially constructed output after failure.

`RenderCallbackCanvas` is a non-disposable active-token facade. `LogicalBounds` is the finite semantic allocation/clip region selected from the complete output bounds and resolved requirement. `DeviceBounds` is the immutable composition-device footprint of the complete backing target; it contains `PixelRect.FromRect(LogicalBounds, Density)` and normally equals it, but may be wider when an existing physical allocation or explicit raster apron is preserved. `RasterBounds == DeviceBounds.ToRect(Density)` and `LogicalOrigin == RasterBounds.Position` identify the complete pixel-aligned logical footprint and the logical point represented by backing pixel `(0, 0)`. For a mapped materialization output, the engine pretranslates and clips the supplied `ImmediateCanvas` to `RasterBounds` so author coordinates remain composition-global and antialiasing may write the canonical rounding fringe without changing `LogicalBounds`. A target-attached command/scope retains its declared semantic target clip because it does not remap a standalone backing image.

`Use` is one-shot per facade. It invokes the supplied action synchronously with an executor-managed `ImmediateCanvas`, then closes that canvas before returning; a second call or retained use fails. Closing this callback canvas restores state but does not flush, submit, snapshot, or synchronize. Only the request schedule may perform a declared synchronization, including at the containing island boundary. `UseShader` similarly creates a session-owned shader, invokes the action, and disposes it before returning. `UseResource` is a scoped borrow: the callback must not retain or dispose the raw value, and the engine keeps ownership. The facade, session, input, output, and resource token reject use after callback completion.

The supplied `ImmediateCanvas` runs in an engine-only deferred-callback capability mode. The ordinary state stack, allocation-free transform/clip, clear, and drawing calls are allowed only with immutable value arguments, same-session `RenderExecutionInput` views, a resource currently authorized by a nested same-session `UseResource`/`UseShader` scope, or the request-owned `Bitmap` only while its nested same-session `UseSnapshot` action is active. Every resource-bearing canvas entry point validates that capability. `PushLayer`, `PushOpacity`, `PushBlendMode`, `PushOpacityMask`, `PushPaint`, or any other API implemented with `SaveLayer`/a hidden target allocation is rejected; those semantics must be recorded with `TargetLayerScope`/finite value `Layer`, `Opacity`, `Blend`, or `OpacityMask`. Public `Dispose`, `Snapshot`, `DrawNode`, `DrawDrawable`, `DrawBackdrop`, direct target/surface creation or opening, use of an unrelated native/`RenderTarget` object, and any operation that would invoke a legacy raw callback, start a nested renderer, flush, submit, snapshot, or synchronize also throw in this mode. The executor closes the canvas through an internal no-flush path after the action. CPU pixels use the narrowly scoped `UseSnapshot` bitmap capability; nested nodes/subtrees must have been recorded during `Process`; external resources must be declared and borrowed through tokens. A capability violation fails the callback and publishes no output/cache.

`RenderExecutionInput.Draw` accepts only the currently active `ImmediateCanvas` produced by a facade in the same execution session; passing any external, closed, or different-session canvas throws. `Draw` places the complete input image at `RasterBounds`, preserving its physical size and canonical rounding fringe; `Bounds` remains semantic metadata and is never used to stretch the backing image. It resamples only when the effective input supply differs from the callback density. `DrawDeviceSpace` bypasses logical resampling and interprets `devicePoint` in composition-device pixels; the backing-local placement is `devicePoint - Canvas.DeviceBounds.Position`. This makes shifted/cropped output origins explicit without permitting callbacks to draw into an unrelated destination.

Each materialized input exposes immutable composition-device `DeviceBounds`, `DeviceSize == DeviceBounds.Size`, `RasterBounds == DeviceBounds.ToRect(EffectiveScale.Value)`, and `LogicalOrigin == RasterBounds.Position`; executor inputs always have a concrete scale. `DeviceBounds` is propagated with the physical value across materialization and cache capture/hit instead of being recomputed from semantic `Bounds`. `UseShader` supplies a borrowed shader whose local matrix maps composition-global logical coordinates to input-local device coordinates: logical point `p` samples local input point `(p - LogicalOrigin) * EffectiveScale.Value`, with the declared tile modes outside `RasterBounds`. Used on the active same-session callback canvas, this samples the same physical content as `Draw`, including shifted/cropped origins, the antialiasing fringe, and differing input/output densities. The shader must not be retained, disposed, or used with another canvas/session.

`UseSnapshot` is one-shot per input. It invokes the action synchronously with a request-owned `Bitmap` after the declared readback synchronization, then disposes the bitmap before returning. The author must not dispose or retain it; retained use observes an already-disposed object. Callback failure still releases it and preserves the callback exception as primary.

`RenderScaleContract.MaterializeAtWorkingScale` uses feature 003's supply-driven formula, and `Vector` remains unbounded until a later materialization. `PreserveInputSupply` is a topology contract, not a request to choose one density from an arbitrary list: it is valid only for an element-wise one-input map (`OpaqueMap`, including zero-or-one discard) or a per-fragment replay scope such as `TargetScope`/`RawTargetScope`, where every surviving output has exactly one corresponding input whose supply is copied. `MapInputSupply` has the same one-corresponding-input restriction but applies a pure `EffectiveScale -> EffectiveScale` mapping after that input supply is known. It is the required contract for transform-like density changes that must be recomputed after an `OwningTargetDomain` dependency resolves; returning `Unbounded` preserves deferred rasterization, while a concrete result is capped by `MaxWorkingScale` and the per-buffer dimension limit. Both one-input contracts are rejected for zero-input sources/captures, multi-input combine, and arbitrary expansion; those shapes use `Vector`, `MaterializeAtWorkingScale`, or a `Custom` contract as allowed by their description. `TargetLayerScope` has no author-supplied scale contract and publishes `EffectiveScale.Unbounded`. Validation happens when a description is used by a context method, so one reusable description cannot acquire a different topology meaning accidentally.

`Custom` is the public replacement for a custom render node's former eager working-scale decision. Its pure CPU resolver uses the available input supplies and complete conservative `OutputBounds`; it cannot observe the later `RequiredRegion`. A fragment whose recording metadata is already concrete resolves once while recording. A fragment with an `OwningTargetDomain` dependency does not expose the provisional result through `RenderFragmentHandle` and may re-evaluate the pure resolver during graph-wide metadata resolution after its final input supplies and output bounds are known. The resolver must return a finite value greater than zero. A throw, NaN, infinity, zero, or negative result fails the current recording or graph-finalization transaction and leaves no published output/cache entry; it is never sanitized to `OutputScale` or another fallback. A valid result is capped by `MaxWorkingScale` and clamped against the complete output bounds by the per-buffer 16,384-axis rule before becoming concrete fragment metadata. Later ROI analysis crops the allocation region but never raises or changes that density. The resolver method/key is structural; its returned density is runtime data.

`default(RenderScaleContract)` and `default(RenderHitTestContract)` are uninitialized and rejected. Authors select an explicit named or custom contract.

For `OpaqueMap`, `RenderValueCardinality.Single` means one output per invocation/input and `ZeroOrOne` permits per-input discard; other cardinalities are rejected. `OpaqueCombine` is limited to at most one total output. `OpaqueSource` and `OpaqueExpand` interpret the description cardinality as the total single-invocation result, and only `OpaqueExpand` may declare an arbitrary N-to-M range. Every case preserves authored output order.

When `structuralKey` is null, the description uses the execution callback's method identity plus operation kind. `RenderScaleContract.Custom`, custom bounds, and custom hit-test contracts likewise default to their delegate method identities. A captured choice that changes operation/binding/topology shape belongs in an explicit equality-stable structural key. Pixel-affecting captured scalar/value data belongs in `runtimeIdentity`; leaving it null safely disables cross-request output-cache reuse for this recorded value.

The context methods are deliberately named `Opaque*`: an arbitrary callback is never treated as a semantic/fusible map based on author assertion.

## Materialized input description

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class MaterializedInputDescription
{
    public Rect Bounds { get; }
    public EffectiveScale EffectiveScale { get; }

    public static MaterializedInputDescription FromRenderTarget(
        RenderResource<RenderTarget> target,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderHitTestContract hitTest);
}
```

A materialized input is already a fusion/cache island boundary. Its `RenderTarget` must be represented by an explicit token: `Borrow` for a repeatable externally owned target or `Own` for a one-shot transfer. Authors cannot wrap a raw target with ambiguous lifetime. `effectiveScale` must be finite, positive, and concrete. Let `deviceBounds = PixelRect.FromRect(bounds, effectiveScale.Value)`; the target's device size must equal `deviceBounds.Size`, and its format/backend/device/context must be compatible with the request's linear premultiplied RGBA16F pipeline. Backing pixel `(0, 0)` represents `deviceBounds.Position`, so mismatched targets are rejected rather than silently stretched, cropped, or sampled out of range. Hit testing uses the same mandatory CPU-only `RenderHitTestContract` as other descriptions. For a source with no logical inputs, authors normally choose `OutputBounds`, `None`, or a pure `Custom`; `AnyInput` is rejected. A custom predicate cannot capture/read the target, a resource token, native state, or an execution/context facade. Internal overloads may represent render-cache, 3D, and decoder sources without widening this public contract.

## Target capture description

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class TargetCaptureDescription
{
    public TargetRegion SourceRegion { get; }
    public Rect Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public RenderScaleContract Scale { get; }

    public static TargetCaptureDescription Create(
        TargetRegion sourceRegion,
        Rect bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale);
}
```

`TargetCapture` is the public typed target-token-to-value edge. It reads the preceding token in the current external root, resolved `TargetLayerScope`, or finite value Layer scope, produces a request-owned immutable value with `RenderValueCardinality.Single`, and advances the order token without invoking an author callback. Its returned fragment has `ContributesValuesToTarget == false`: publishing it anchors the read in painter order but never composites the captured pixels back into the source target. Authors call `ContributeValues(capture)`—or call `ContributeValues` on a downstream transform—at the later point where those pixels should be drawn. This avoids the incorrect assumption that drawing a semitransparent destination over itself with `SrcOver` is identity.

`Bounds` is mandatory, finite, non-empty conservative content metadata even when `SourceRegion` is `Full`; after scope resolution it must be contained by both the source region and current target domain. `SourceRegion.Empty`, `HitTest.AnyInput`, and mismatched bounds are rejected. A globally empty required region may still skip an otherwise valid capture at planning time.

The public capture accepts only `RenderScaleContract.MaterializeAtWorkingScale` or `Custom`. With no value inputs, the standard form starts at `OutputScale`, caps by `MaxWorkingScale`, and applies the 16,384-axis clamp to `Bounds`; `Custom` receives an empty `InputSupplies` list and may use only `OutputBounds`, `OutputScale`, and `MaxWorkingScale` from its `RenderScaleContext` before following the same validation/cap/clamp. It receives no enclosing-scope density supply. `Vector` and `PreserveInputSupply` are rejected because the current target's eventual scoped density is not available while a child is recording.

Public `TargetCapture` is therefore a deliberate sampling/materialization boundary, not a lossless or scope-density-preserving snapshot. The executor samples the scoped target into the declared concrete capture density, so capture inside a denser finite `Layer` or `TargetLayerScope` may intentionally downsample before later consumers run. This resampling is part of the public operation's declared semantics and must be covered by density-sensitive parity tests. Engine-internal backdrop capture may instead late-bind the actual scoped target density; a nested handle for that capture reports no concrete public metadata until an explicit finite Layer supplies the owning domain.

The captured value may feed Shader, Geometry, opaque, scope, or target-command inputs. Its target-token dependency is threaded into those consumers, materialized once, and may fan out to multiple pure consumers. The capture is a target-read/fusion and whole-subtree-cache boundary and is scheduled/lifetime-counted like every other value; CPU readback occurs only when a downstream declaration actually requires CPU pixels.

The engine's `GraphicsContext2D.Snapshot()` uses the same non-contributing capture anchor and adds only an internal request-local identity binding so a later built-in backdrop node can find its value across sibling transactions. The binding is not a general public side channel.

## Target scope description

```csharp
namespace Beutl.Graphics.Rendering;

public sealed class TargetScopeDescription
{
    public RenderBoundsContract Bounds { get; }
    public RenderHitTestContract HitTest { get; }
    public RenderScaleContract Scale { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static TargetScopeDescription Create(
        Action<TargetScopeSession> execute,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
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
}
```

The callback is invoked once per runtime input fragment against the current scoped target, which retains all preceding pixels and is never auto-cleared. It must call `ReplayInput` exactly once while `Canvas.Use` has its managed canvas active; the method replays that fragment on the same target. Missing, duplicate, retained, or out-of-scope replay is a deterministic execution failure. This session uses a narrower capability mode than opaque/Geometry drawing: only save/restore, transform, and clip operations that are mechanically known not to allocate a layer or emit pixels may surround `ReplayInput`; a resource-bearing clip must use a declared borrow. `Clear`, every independent draw, snapshot/readback, `PushLayer`, opacity/blend/paint/mask APIs that internally use `SaveLayer`, any hidden allocation, flush/submit, nested work, and unrelated resource use are rejected. Group isolation uses the typed `TargetLayerScope`; Opacity uses the typed `Opacity` recorder; engine blend/paint/mask nodes use planner-visible typed scope descriptors, and an arbitrary raw layered callback is `LegacyRawCanvas` opaque-external work. Additional pixel emission belongs in `TargetCommand` or an opaque value description. `Bounds`, `HitTest`, and `Scale` map each input's pure metadata; `PreserveInputSupply` keeps its density, while `MapInputSupply` publishes a transform-like density change after the corresponding input supply is known. Public `TargetScope` is an opaque fusion boundary even if its bounds look like identity. Engine-proven typed scopes use the same internal fragment shape but may participate in equivalence rewrites.

Finite value `Layer` flattens all supplied streams in authored order into one fragment with exactly one materializable composited value and publishes `EffectiveScale.Unbounded` because it can replay at the eventual target density. `TargetLayerScope` also flattens a mixed stream but exposes no independent outer value: it publishes `EffectiveScale.Unbounded`, preserves the input streams' aggregate `RenderValueCardinality` for dependency accounting, keeps its initialized `Full`, finite `Region`, or `Empty` target access in the fragment IR, and remains value-ineligible until explicitly localized by finite `Layer`. `TargetCommand` has no independent reusable pixel supply, publishes `EffectiveScale.Unbounded`, and has `RenderValueCardinality.None`; its effectful fragment plus `QueryBounds`/hit-test metadata remain observable. Public target capture has `Single`; materialized sources, WholeSource Shader, Geometry, and opaque materializations publish concrete supply according to their own contracts.

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

    public static RawTargetScopeDescription Create(
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

    public static RawTargetCommandDescription Create(
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
    public bool RequiresInputReadback { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
    public IReadOnlyList<RenderResource> Resources { get; }

    public static TargetCommandDescription Create(
        Action<TargetCommandSession> execute,
        TargetRegion affectedRegion,
        Rect queryBounds,
        RenderHitTestContract hitTest,
        TargetAccess access,
        bool requiresInputReadback = false,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
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
    public Rect AffectedBounds { get; }
    public Rect RequiredRegion { get; }
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
    public RenderCallbackCanvas Canvas { get; }

    public void UseSnapshot(Action<Bitmap> use);

    public void UseResource<T>(
        RenderResource<T> resource,
        Action<T> use)
        where T : class;
}
```

`default(TargetRegion)` is uninitialized and rejected. `Region` rejects invalid/non-finite rectangles and normalizes a finite zero-area rectangle to `Empty`; `Full` is resolved only after the current external root, `TargetLayerScope`, or finite value Layer target has a finite domain. `TargetCommandDescription.Create` rejects `TargetAccess.Readback` with `TargetRegion.Empty`, because its required one-shot snapshot cannot produce a non-null zero-area bitmap; an order-only command uses `ReadWrite` plus `Empty`.

Target callbacks execute later against the currently scoped target session and borrowed materialized inputs. Each supplied handle must have `CanBeUsedAsValueInput == true`; recording rejects command-bearing/effect-only fragments or shared-target-scope inputs, which remain ordered through `TargetScope`/`TargetLayerScope` or become a value only through finite `Layer`. A non-contributing public `TargetCapture` is valid because it does expose a value: its prior-token dependency is scheduled before materialization and threaded into the command. Valid inputs are flattened in authored handle/stream order. `RequiresInputReadback` explicitly schedules CPU-readable snapshots for all materialized inputs and enables each input's `UseSnapshot`; when false, input `UseSnapshot` throws without synchronizing. This declaration is independent of `TargetAccess.Readback`, which describes the preceding target token.

Every public callback is conservatively target-dependent: `ReadWrite` and `Readback` both consume the prior target token, and `Readback` additionally schedules CPU access. There is no public author-asserted write-only access because ordinary `SrcOver`, inherited opacity/blend/mask state, and most canvas draws read the prior destination. Engine-proven clear/source-replace commands may use an internal write-only classification under an enforceable capability. `TargetRegion.Full` means the complete finite domain of the current root, finite value Layer, or resolved `TargetLayerScope` target; `Empty` is an order-only/no-pixel access, and `Region(Rect)` is a validated finite composition-logical subregion. A built-in clear uses internal write-only `Full`; a destination snapshot uses `Full` readback; a finite backdrop draw uses `ReadWrite` and its bounds. Commands are preserved even when the affected region is `Empty`.

`QueryBounds` and the mandatory CPU-only `HitTest` contract describe this command's visible/query contribution independently of the region it reads or writes; snapshot/readback/clear commands normally use empty query bounds plus `None`, while a backdrop draw uses its declared bounds plus `OutputBounds`. They never authorize command reordering or elimination. Resources must be declared and are borrowed through the same scoped rules as opaque work. A null structural key defaults to the execution callback's method identity plus access kind; shape-changing captured choices require an explicit key. Pixel-affecting captured scalar/value data uses `runtimeIdentity`; null creates a fresh request-local cache identity.

For `TargetAccess.Readback`, the executor snapshots the immutable preceding target token over the resolved finite affected region before invoking the command callback. `UseSnapshot` must then be called exactly once and supplies that pre-command bitmap synchronously; writes performed by the callback are not reflected in it. The bitmap's local pixel `(0, 0)` represents `Canvas.LogicalOrigin` and its size is `Canvas.DeviceBounds.Size`. The request disposes it before return, retained/disposed-by-author use is invalid, and failure preserves the callback exception while still releasing the bitmap. A callback that needs pixels after an intermediate write must split that work into a target command followed by `TargetCapture`/another command, making the synchronization visible. `ReadWrite` permits GPU-side target access through `Canvas` but does not imply CPU readback.

The command canvas clips ordinary drawing to the resolved affected region and rejects every pixel operation when it is `Empty`. Because the native clear primitive ignores clip state, `Clear` is accepted only for `TargetRegion.Full`; a finite-region clear must use the engine's clipped source-replace operation or an ordinary clipped draw. Every access outside the declaration is a capability violation and fails before cache publication.

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
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
}

public sealed class ShaderResourceBinding
{
    public string Name { get; }
    public ShaderResourceCoordinateSpace CoordinateSpace { get; }
    public RenderResource Resource { get; }
    public object StructuralKey { get; }
    public RenderRuntimeIdentity? RuntimeIdentity { get; }
}

public enum ShaderResourceCoordinateSpace
{
    Value,
    OutputLogical,
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
        RenderRuntimeIdentity? runtimeIdentity = null)
        where T : unmanaged;

    public void Resource<T>(
        string name,
        RenderResource<T> resource,
        ShaderResourceCoordinateSpace coordinateSpace,
        Action<ShaderResourceWriter, T, ShaderExecutionContext> bind,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null)
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

`WholeSource` defines a complete coordinate shader with an implicit upstream `src` child and mandatory bounds contract. It always executes unfused in this feature. Its `main(float2 coord)` receives local output-device pixel coordinates: `(0.5, 0.5)` is the center of the first pixel in `DeviceBounds`, and `LogicalOrigin + coord / WorkingScale` is the corresponding logical point. `LogicalOrigin` is the logical position represented by local device `(0, 0)` after canonical `PixelRect.FromRect` rounding. The engine binds `src` so `src.eval(coord)` samples the same logical point from the input, applying the input bounds origin and concrete input density; outside-input sampling uses `sourceTileMode`. A vector input is rasterized at the resolved working density before binding. There is no `WholeSourceInvariant`, `IsCoordinateInvariant`, or author opt-in flag.

`CurrentPixel` is a deferred semantic transform whose description declares no independent scale change. A bare `RenderNodeContext.Shader(input, description)` preserves the input `EffectiveScale`; when the same description is the first surviving `FilterEffectContext.Shader`, the enclosing `FilterEffectRenderNode` may fold its standard or custom working-scale contract into that fragment and choose another density. A chain over a materialized input may stay deferred and fuse into its eventual draw. An unbounded analytic source may stay deferred only across engine-owned stages whose coverage homogeneity is mechanically proven; an arbitrary public `CurrentPixel` stage starts after coverage has been resolved into a materialized input. Each stage's forward metadata is resolved before planning. A concrete predecessor fuses with its successor only when their effective scales are equal; a concrete density change is an explicit `ScaleTransition` boundary. An `Unbounded` predecessor may fuse and adopt the concrete successor/run density. A merged run allocates once at its final runtime-clamped adopted density. Every binder receives stage-local input/output bounds and required region together with that actual run density and its canonical device footprint. The first stage's input effective scale comes from the materialized run input; every later stage receives the run density because that is the concrete output produced by its fused predecessor. Thus fusion and disabled execution expose equivalent binder metadata even when an earlier policy-bearing stage changes density or the allocation limit lowers the planned density. `WholeSource` is itself an unfused materialization boundary and publishes its concrete resolved density immediately from its mapped complete bounds. Later ROI cropping never changes a valid resolved density.

An extra resource binding declares how coordinates passed to its `.eval` are interpreted. `Value` passes author-defined value coordinates unchanged and is the only form accepted by CurrentPixel. `OutputLogical` means logical composition units. `OutputDevice` means the same local device-pixel units as WholeSource `coord`. The resource binder uses `ShaderExecutionContext` to create any required local matrix and density conversion; its declaration, source use, and returned shader are validated as one binding.

Uniform/resource names, parsed source types, order, coordinate spaces, binder method/explicit structural keys, source, kind, tile mode, and bounds behavior are structural. Uniform values, resource identities/versions/contents, final logical/device bounds, required region, output/working/input density, and frame data are runtime. The direct unmanaged `Uniform(name, value)` overload writes the value without an author callback and automatically encodes its validated canonical representation in render-output cache identity. `unmanaged` is only the compile-time ceiling: runtime validation accepts the explicit canonical CPU scalar/vector/matrix allowlist compatible with the parsed SkSL type and rejects pointer-containing/padding-dependent structs, opaque byte blobs, `IntPtr`/`UIntPtr`/`nint`/`nuint`, function/native handles, and any other process-address identity. Canonical identity is derived from validated components, never raw struct memory. The span overload copies its floats into immutable description-owned storage during recording and keys the copied bit pattern; no caller array/memory is retained.

The custom uniform binder overload also includes its passed `value` automatically. A null structural key defaults to the binder method identity; a shape-changing captured choice requires an explicit equality-stable structural key. Any additional pixel-affecting state read by the binder—including captured fields, globals, clocks, or services—must be represented by `runtimeIdentity`. Null is deliberately request-unique for every custom binder invocation, so the safe default cannot reuse a stale pixel cache; authors opt into cross-request output-cache reuse by supplying a complete equality-stable runtime key. Resource binders follow the same runtime rule. `ShaderExecutionContext`, writers, and resource tokens are active only during binding.

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

    public static GeometryDescription Create(
        Action<GeometrySession> render,
        RenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
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
    public void SetOutputBounds(Rect logicalBounds);
    public void DiscardOutput();
}
```

Geometry applies element-wise to one input stream and is never fused in this feature. Each input produces zero or one output, so it is an order-preserving `N -> 0..N` map; `DiscardOutput` is the only cardinality reduction and Geometry never expands. Its CPU-only hit-test contract is resolved from conservative metadata and cannot depend on executing readback. Each element uses the standard supply-driven `MaterializeAtWorkingScale` rule: start at `OutputScale`, raise to the concrete input supply when denser, cap by `MaxWorkingScale`, and apply the 16,384-axis clamp against that element's complete mapped output bounds. The resulting concrete density is published in the Geometry fragment metadata and supplied as `WorkingScale`/canvas density; later ROI cropping never changes it.

Before each Geometry callback begins, the executor transparently clears its planner-owned output inside the already scheduled Geometry island; undefined pooled pixels are never author-visible, and the clear adds no separate pass or synchronization. The session, shared `RenderExecutionInput`, canvas facade, declared resource tokens, shader-use callbacks, and snapshot bitmap are borrowed for callback duration and follow the same scoped rules as opaque work. `UseSnapshot` throws unless readback was declared and scheduled, and the request disposes its bitmap before the method returns. `SetOutputBounds` accepts only a contained shrink of `OutputBounds`; `DiscardOutput` wins over shrink. A null structural key defaults to the render callback's method identity plus Geometry kind; shape-changing captured choices require an explicit equality-stable key. Pixel-affecting captured scalar/value data uses `runtimeIdentity`; null creates a fresh request-local identity and disables cross-request output-cache reuse for the recorded Geometry value. `GeometryDescription` uses reference equality and an internal structural comparer/key.

## FilterEffectContext additions

```csharp
namespace Beutl.Graphics.Effects;

public sealed class FilterEffectContext : IDisposable
{
    // All existing current-main operation members remain available.

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

The existing operation-call surface and authored order remain compatible, but provisional author-time metadata does not. A symbolic owning-domain input intentionally starts with `Bounds == Rect.Invalid`; symbolic or branch-dependent input makes `WorkingScale` unavailable, so authors must use `TryGetWorkingScale`. An operation whose parameters depend on unavailable bounds records a deferred pure bounds mapping and an execution factory/callback that bind from the resolved target bounds. The engine invokes `ApplyTo` once and never replays authoring after metadata resolution. This stricter metadata availability is an intentional compatibility break.

Both new methods append to the same authored item order as existing Skia/color/custom operations. They do not draw, allocate, access a GPU, compile, flush, synchronize, snapshot, or read back. Like the existing bounds-transforming methods, they update the public `Bounds` synchronously before returning so a later operation in the same `ApplyTo` call observes the complete conservative result of every preceding item. `ShaderDescription.CurrentPixel` preserves `Bounds`; `ShaderDescription.WholeSource` and `GeometryDescription` apply their `RenderBoundsContract.TransformBounds` to the current value. Runtime Geometry shrink/discard does not reduce this recording-time conservative bound. If a retained preceding legacy custom item already made `Bounds == Rect.Invalid`, the new item joins that existing render-time sequence and `Bounds` remains Invalid; its mapping is applied to the actual runtime target bounds inside the same marked opaque island rather than being split into a planner-visible typed fragment. This compatibility case does not allow a new Shader/Geometry mapping invoked with valid bounds to return Invalid.

Each `Shader`/`Geometry` append is atomic. With valid current bounds, the context validates the description and ownership, invokes the pure forward bounds mapping, and validates the result before committing either the item or new `Bounds`. If validation or that mapping throws, returns an invalid/non-finite rectangle, or otherwise fails, the method leaves its previous item order and `Bounds` unchanged. There is no identity fallback. The surrounding engine invocation of `FilterEffect.ApplyTo` is a nested transaction checkpoint over authored items, `Bounds`, owned-resource transfers, and borrow registrations: an exception rolls all of them back to the state before that invocation, disposes newly owned resources best-effort, preserves the primary exception, and publishes no partial operation. This applies recursively to effect groups/presenters rather than leaving earlier child items visible after a later child fails.

The existing `CustomEffect` recording method and its `CustomFilterEffectContext`/materialized `EffectTarget` callback surface remain available and execute later, but lower as an explicit legacy opaque-external island. Its physical-footprint contract is explicit:

```csharp
public class CustomFilterEffectContext
{
    public static PixelRect DeviceBufferBounds(Rect bounds, float w);
    public static (int Width, int Height) DeviceBufferSize(Rect bounds, float w);
    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
}

public sealed class EffectTarget : IDisposable
{
    public Rect OriginalBounds { get; set; }
    public Rect Bounds { get; set; }
    public EffectiveScale Scale { get; init; }
    public PixelRect DeviceBounds { get; }
    public Rect RasterBounds { get; }
    public RenderTarget? RenderTarget { get; }
}
```

Direct legacy activation remains available, but request classification is explicit and independent from scale:

```csharp
public sealed class FilterEffectActivator : IDisposable
{
    public FilterEffectActivator(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity);

    public RenderIntent Intent { get; }
    public RenderRequestPurpose Purpose { get; }
}
```

There is no compatibility inference from `MaxWorkingScale`: finite-density delivery and unlimited-density preview
are both valid and must retain their authored allocation-failure policy.

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

`DeviceBufferBounds(bounds, w) == PixelRect.FromRect(bounds, w)` is the allocation source of truth. `DeviceBufferSize` returns that footprint's size rather than independently rounding width/height; consequently a fractional origin can add a device pixel even when `ceil(bounds.Width * w)` alone would not. `EffectTarget.DeviceBounds` is the immutable allocation footprint and `RasterBounds` is its pixel-aligned logical footprint. If a legacy effect translates `Bounds` without reallocating (for example Shake), `RasterBounds` translates by exactly the same logical delta while preserving its physical size. `Draw`, `Open`, and final activation use this physical footprint and never stretch the backing image to semantic `Bounds`; `OriginalBounds`, `Bounds`, measurement, hit testing, and ROI semantics remain unchanged.

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

Returning `null` uses the standard `MaterializeAtWorkingScale` policy, including its `s_out` floor. The protected
hook may instead return any valid `RenderScaleContract`; an explicit `Custom` result may intentionally be below
`s_out` and is not raised to the standard floor. After the base implementation has identified the finite or
owner-relative isolation needed for mixed/value-ineligible inputs, it folds the standard or custom policy into the
first surviving Shader, Geometry, or legacy operation. The callback is invoked per surviving branch with exactly
one `InputSupplies` item and that branch's isolated effect-input bounds as `OutputBounds`; those are not the later
first-operation output bounds. Legacy multi-input lowering aggregates the densest concrete branch result and falls
back to `OutputScale` only if every branch remains `Unbounded`. Allocation footprints are independent of callback
cardinality: before an opaque Custom callback they retain each branch's local-origin transforms and every
intermediate/forced materialization, so empty space in a sparse union is not backing storage. Because a Custom
callback may combine or split targets without declaring topology, the first such callback unions the transformed
branch results and collapses later analysis to that aggregate domain. The clamp also carries the largest known
pre-callback backing footprint forward at the transformed semantic position because a callback may retain that
backing while moving or shrinking only `EffectTarget.Bounds`; this is deliberately conservative if the
implementation replaces every target.
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
