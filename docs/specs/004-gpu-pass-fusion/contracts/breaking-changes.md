# Contract: Breaking Changes & Migration Map

**Feature**: `004-gpu-pass-fusion` | **Status: removal shipped** (rollout step 6, `refactor(engine)!: remove the imperative filter-effect pipeline`). This document is the `BREAKING CHANGE:` documentation for effect authors.

Breaking changes use `refactor(engine)!:` / `feat(engine)!:` Conventional Commits with a `BREAKING CHANGE:` footer naming `Beutl.Engine` (`Beutl.Graphics.Effects`). No `[Obsolete]` shims or v2 duplicates are retained. Legacy C# script identifiers such as `Context` and `Session` receive targeted Roslyn validation errors naming `Builder` and this guide without retaining public members on `CSharpScriptEffectGlobals`.

## Removed / changed → replacement

| Removed or changed public surface | Replacement | Migration note |
|---|---|---|
| `FilterEffect.ApplyTo(FilterEffectContext, Resource)` | `FilterEffect.Describe(EffectGraphBuilder, Resource)` | same append idiom; convenience methods keep their names, including `InnerShadow`, `InnerShadowOnly`, `ColorMatrix<T>`, and brush-backed `BlendMode` |
| Concrete `FilterEffectRenderNode` with an inherited default `Process(RenderNodeContext)` implementation | Abstract `FilterEffectRenderNode`; normal descriptor-authored effects use `PlanFilterEffectRenderNode` | Override only `ResolveWorkingScale` in a plan-node subclass for narrow policy. A fully opaque custom node implements the complete `Process` operation construction itself. |
| A custom-node effect deriving directly from `FilterEffect`, with a `FilterEffect.Resource` that could inherit the default plan factory | Derive the effect from `CustomRenderNodeFilterEffect` and its resource from `CustomRenderNodeFilterEffect.Resource` | The dedicated resource makes `RenderNodeFactory` an abstract override, so omitting it is a compile-time error instead of recursive plan execution. |
| Public `EffectGraphBuilder.Dispose()` / `IDisposable` | No author-owned lifetime API | The engine owns the builder, transfers tracked resources at internal `Build()`, and internally aborts/cleans an unbuilt builder after failure. Effect code only appends descriptors. |
| Public `RenderTargetPool`, `RenderNodeContext.Pool`, the `RenderNodeProcessor(..., RenderTargetPool? pool, ...)` constructor parameter, and `RenderTargetPool.Acquire*` | `RenderNodeContext.CreateChildProcessor(root, useRenderCache, requestedBounds?)` for nested pulls; no public raw-pool replacement | The complete pool type and every lease/teardown path are executor internals. A plugin that hosts a child render tree asks its current context to create the child processor; scale, diagnostics, intent, purpose, requested bounds, and the opaque pool are inherited without transferring ownership. Public cache-helper overloads likewise no longer accept a pool. |
| Public compiler implementation records (`CompiledPlan`, `CompiledPass` and its pass/stage/resource-plan hierarchy, `StructuralKey`, `PassBackend`) | No public replacement; author graphs through `EffectGraphBuilder` and observe execution through `PipelineDiagnostics` | These types are internal scheduling/cache representation, not an extensibility contract. Plugins declare descriptors and never construct or execute plans directly. |
| External inheritance from `EffectNodeDescriptor` | Compose the public sealed descriptor factories accepted by `EffectGraphBuilder` | The descriptor vocabulary is a closed union. There was no public generic append/registration seam and the compiler rejected unknown derived types, so the base now carries an engine-only abstract discriminator that prevents out-of-tree concrete subclasses. |
| `FilterEffectContext` (recording surface, `AppendSkiaFilter`, `AppendSKColorFilter`, `CustomEffect<T>`) | `EffectGraphBuilder` (`SkiaFilter`, `ColorFilter`, `Shader`, `Compute`, `Geometry`, `Split`, `Composite` + conveniences) | factories become descriptors; `CustomEffect` callbacks become `GeometryNode` sessions or `ShaderNode`s |
| `CustomFilterEffectContext` (`CreateTarget`, `Open`, `ForEach`, target mutation) | `GeometrySession` (`OpenCanvas`, `Inputs`, scales) | target creation/flushing is executor-owned; multi-target flows become multiple nodes |
| `FilterEffectActivator` | `PlanExecutor` (internal) | no public replacement — execution is engine-owned |
| `EffectTarget` / `EffectTargets` (mutable) | `EffectInput` (read-only) for authors; `ResourcePlan` internally | |
| `SKImageFilterBuilder` (public) | internal compiler detail of `SkiaFilterPass` | |
| `SKSLShader.ApplyToNewTarget(CustomFilterEffectContext, …)` | `ShaderNodeDescriptor` (whole-source or snippet) | **final disposition**: `SKSLShader` survives as the compile/holder type (`Create`/`TryCreate`/`CreateBuilder`/`Effect`); only the activator entry point was deleted |
| `GLSLShader.Apply/ApplyMultiPass(CustomFilterEffectContext, …)` | `ComputeNodeDescriptor` | **final disposition**: `GLSLShader` survives as the compile/holder type with internal single-pass execution used by the compute executor; pipeline creation/ping-pong is executor-owned |
| `ComputeNodeDescriptor.Create(..., bool requiresDepth, ...)` and independently nullable fallback fields | `Create(dispatch, passCount, BoundsContract, ComputeFallbackPolicy, ...)` | Declare local versus full-frame bounds explicitly. Choose `Identity`, `Skip`, or `Cpu(callback, requiresReadback)` as one valid policy value; contradictory callback/readback states cannot be represented. |
| `ComputeNodeDescriptor.RequiresDepth` and the interim `DepthScratchCount` | No replacement | Remove the depth declaration. It represented an unused fixed-function attachment rather than an author-visible compute capability. `ColorScratchCount` remains structural and runtime-enforced. |
| `IComputeContext.AcquireDepthScratch()` | No replacement | Remove the acquire and its descriptor count. Compute GLSL stages execute through color-only fullscreen render passes. |
| `IComputeContext.Run(..., destination, depth, pushConstants)` | `Run(..., destination, pushConstants)` | Remove the depth argument from both the single-texture and dual-texture overloads. Color scratch acquisition and dispatch-count enforcement are unchanged. |
| `IComputeContext` exposing only device textures, destination `Width`/`Height`, and one `WorkingScale` | `SourceBounds`, `TargetBounds`, and `SourceScale` plus the existing destination `WorkingScale` | Local/coordinate-changing kernels must translate from source logical coordinates and density into the destination logical coordinates and density explicitly. CPU fallbacks read the corresponding values from `GeometrySession.Inputs[0]` and the session output members. |
| `IGraphicsContext.CreateRenderPass3D(..., TextureFormat depthFormat = Depth32Float, ...)` | `CreateRenderPass3D(..., TextureFormat? depthFormat, ...)` | Backend implementers and callers must recompile and explicitly pass a depth format or `null` for a color-only pass. A non-null value must be a depth format. This intentionally avoids silently changing omitted arguments from depth-enabled to color-only. |
| `IGraphicsContext.CreateFramebuffer3D(..., ITexture2D depthTexture)` / non-null `IFramebuffer3D.DepthTexture` | `CreateFramebuffer3D(..., ITexture2D? depthTexture)` / nullable `DepthTexture` | Pass `null` exactly when the render pass is color-only. A non-null texture must match the render pass depth format and framebuffer dimensions. Color-only pipelines must disable depth testing and depth writes. |
| `GeometryNodeDescriptor.Create(callback, ..., structuralToken)` | `GeometryNodeDescriptor.Create(callback, ..., structuralToken, requiresReadback)` | This is a binary/source signature change: recompile plugins, and pass `requiresReadback: true` when the callback calls `EffectInput.Snapshot()`. Draw-only callbacks leave it false. |
| `SplitNodeDescriptor.Static(Action<ISplitEmitter> render, int branchCount, object? structuralToken = null)` / `Dynamic(Action<ISplitEmitter> render, object? structuralToken = null)` | The same factory with a final `bool requiresReadback = false` argument | This is a binary/source signature change: recompile plugins, and pass `requiresReadback: true` for callbacks that snapshot an input. |
| Implicit callback readback synchronization | `requiresReadback: true` on `GeometryNodeDescriptor` / `SplitNodeDescriptor`, or `ComputeFallbackPolicy.Cpu(callback, requiresReadback: true)` | `EffectInput.Snapshot()` rejects undeclared readback. The executor owns and counts the declared synchronization; shader sampling and draw-only callbacks leave it false. |
| `CSharpScriptEffect` script globals typed on `CustomFilterEffectContext` | globals expose `Builder` (`EffectGraphBuilder`) + `Progress`/`Duration`/`Time` | **breaks user scripts** (maintainer-approved): a script now authors the declarative graph exactly like a compiled effect (`Builder.Blur(...)`, `Builder.Geometry(...)`, …); legacy `Context`- and interim `Session`-based scripts fail at script compile time with a diagnostic naming `Builder` and this guide — never silently wrong output; before/after sample below |
| One factory surface for both plan customization and opaque execution | `PlanFilterEffectRenderNodeFactory` for standard-plan subclasses; `FilterEffectRenderNodeFactory` only on `CustomRenderNodeFilterEffect.Resource` for opaque nodes | Retain one static factory instance. The exact instance participates in render-tree reuse, so two constructors producing the same node type cannot alias. |
| `EffectGraphBuilder.CustomRenderNode(...)` and containers calling `child.Describe(...)` directly | `EffectGraphBuilder.Effect(FilterEffect.Resource)` | Every container must use `Effect`: it captures `RenderNodeFactory` once and preserves the same normal/custom route regardless of placement. `CustomRenderNodeFilterEffect.Describe` also delegates to `Effect`. |
| Structural tokens keyed by runtime type + `ToString()` snapshot | runtime type + `Equals`/`GetHashCode` | Token types must be immutable with stable equality/hash semantics. Two unequal values that print the same text no longer alias; a mutable token or a token relying only on `ToString()` must be replaced by an immutable value/record. |
| `RenderNodeContext(input, outputScale, maxWorkingScale, RenderIntent? renderIntent = null)` and `RenderNodeProcessor(root, useRenderCache, outputScale, maxWorkingScale, diagnostics, pool, RenderIntent? renderIntent = null)` | `RenderNodeContext(input, RenderIntent renderIntent, float outputScale = 1, float maxWorkingScale = +∞, RenderPullPurpose pullPurpose = Frame)` and `RenderNodeProcessor(root, useRenderCache, RenderIntent renderIntent, float outputScale = 1, float maxWorkingScale = +∞, PipelineDiagnostics? diagnostics = null, RenderPullPurpose pullPurpose = Frame)` | Intent is mandatory; no value is inferred from `MaxWorkingScale`. The public processor constructor no longer exposes a pool. Use `CreateChildProcessor` inside `Process` to inherit all parent execution state. |
| Positional `PassUniformContext(WorkingScale, TargetWidth, TargetHeight, TargetBounds, PipelineDiagnostics? Diagnostics = null, RenderIntent RenderIntent = Delivery)` with generated `init` setters | `PassUniformContext(WorkingScale, TargetWidth, TargetHeight, TargetBounds, RenderIntent, RenderPullPurpose, PipelineDiagnostics? Diagnostics = null)` with get-only properties | Pass intent and purpose explicitly. All properties are immutable after construction, so invalid enum values cannot be introduced through object initializers or `with` expressions. |
| `IRenderer3D.Render(..., float ambientIntensity, Object3D.Resource? gizmoTarget = null, GizmoMode gizmoMode = None)` | `IRenderer3D.Render(..., float ambientIntensity, RenderIntent renderIntent, RenderPullPurpose pullPurpose, Object3D.Resource? gizmoTarget = null, GizmoMode gizmoMode = None)` | 3D renderer implementations must forward both values to material texture resolution. |
| `RenderContext3D(..., CompositionContext compositionContext, float surfaceDensity = 1)` | `RenderContext3D(..., CompositionContext compositionContext, RenderIntent renderIntent, RenderPullPurpose pullPurpose, float surfaceDensity = 1)` | Material implementations read the immutable policy from the context and forward it to texture sources. |
| `TextureSource.Resource.GetTexture(IGraphicsContext, float surfaceDensity = 1)` | `GetTexture(IGraphicsContext, RenderIntent renderIntent, RenderPullPurpose pullPurpose, float surfaceDensity = 1)` | Texture-source implementations must forward policy into nested 2D rendering. Drawable textures keep frame and auxiliary caches separate. |
| Internal mutable `IsAuxiliaryPull` flag | Public `RenderPullPurpose` (`Frame` or `Auxiliary`) fixed at context/processor construction and forwarded through 3D texture rendering | Use `Auxiliary` for measurement, hit testing, thumbnails, and any nested pull that must preserve retained frame renderers/textures. Branch on `PullPurpose`; `IsAuxiliaryPull` is an engine-only convenience, not duplicate public API. |

## Behavioral changes (allowed by spec)

- Rendered output of migrated shader effects may differ within the golden thresholds (SSIM ≥ 0.99 / MAE ≤ 0.02, linear light) — floating-point rounding of fused programs. Byte-identity is not claimed (spec Assumptions).
- Per-effect intermediate targets, per-effect flushes, and full-frame snapshots between custom effects disappear; any out-of-tree code that observed them (timing, memory) sees different numbers.
- **Allocation-failure behavior is normalized** (FR-015): `RenderIntent.Preview` drops the failed pass output and continues; `RenderIntent.Delivery` throws. Intent is independent of the working-scale ceiling. The legacy surface was path-dependent (`Flush` drop-or-throw vs `CreateTarget` returning an empty target whose `Open` threw unconditionally); that divergence is intentionally not reproduced.
- **Structural callback declarations are enforced at runtime**: a compute callback must complete exactly its declared number of successful dispatches (or use the exclusive terminal copy), and a static split callback must call `Emit` exactly `BranchCount` times. Contract violations throw after releasing executor-owned resources and are never converted to preview identity.

> Internal-only removals (`IFEItem`, `FEItem_*`, activator internals — data-model §7) need no plugin migration; this table maps only the public surface.

### Rows that changed shape during implementation

- **`NestedGraphNodeDescriptor` / `EffectGraphBuilder.NestedGraph` (added)**: meta effects whose child chain must be re-described per branch (e.g. `DelayAnimationEffect`'s per-branch delayed clock after a split fan-out) declare a nested-graph node; the executor re-describes and recursively executes the child graph per branch index. This replaced the legacy nested-activator pull.
- **Whole-source `src` tile mode (added)**: `ShaderNodeDescriptor.WholeSource(…, srcTileMode:)` declares the implicit `src` child's out-of-bounds sampling (`Clamp`/`Decal`), reproducing what each legacy custom effect chose when building its own image shader.
- **`RenderNodeContext.DeviceBufferSize` (relocated)**: the logical-bounds/density sizing formula moved from `CustomFilterEffectContext` to `RenderNodeContext` for bounds-oriented callers. Shader authors do not freeze device-px uniforms from that describe-time estimate; they use execution-time `PassUniformContext.TargetWidth` / `TargetHeight` / `WorkingScale` after the per-pass clamp.
- **Pure-generator SKSL scripts**: an `SKSLScriptEffect` script that declares no `src` child (never samples the source) runs as a geometry pass drawing the built shader over the input rect — the legacy behavior, now without the bridge.
- **Render-node customization is split by responsibility.** Descriptor-authored effects override `Resource.PlanRenderNodeFactory` with a static `PlanFilterEffectRenderNodeFactory` whose node derives from `PlanFilterEffectRenderNode`; this preserves compiler, ROI, pooling, and caches while allowing narrow hooks such as `ResolveWorkingScale`. Fully opaque execution derives from `CustomRenderNodeFilterEffect` and supplies a static typed `FilterEffectRenderNodeFactory`. `Resource.Push` and embedded `EffectGraphBuilder.Effect` capture the exact factory instance, so node type alone never aliases different constructors.

## CSharpScriptEffect: before / after

Before (imperative `Context`, removed):

```csharp
// Legacy script — no longer compiles; `Context` is a compile-time error pointing here.
Context.Blur(new Size(10, 10));
Context.CustomEffect(default(Unit), static (_, ctx) =>
{
    for (int i = 0; i < ctx.Targets.Count; i++)
    {
        using var canvas = ctx.Open(ctx.Targets[i]);
        canvas.DrawRectangle(new Rect(0, 0, 50, 50), Brushes.Resource.Red, null);
    }
});
```

After (the script authors the declarative graph through `Builder`, exactly like a compiled effect author):

```csharp
Builder.Blur(new Size(10, 10));                       // convenience filter, was Context.Blur(...)

// Custom canvas drawing — the full-frame bounds default is always correct; the callback's
// canvas is a freshly-cleared pooled output, so re-composite the input first to keep it as a baseline.
Builder.Geometry(session =>
{
    var canvas = session.OpenCanvas();
    using (canvas.PushDeviceSpace())
        session.Inputs[0].Draw(canvas, default);      // re-composite the input
    canvas.DrawEllipse(new Rect(20, 20, 40, 40), Brushes.Resource.Red, null);
});
```

**Capability note (restored)**: a C# script authors the *same declarative vocabulary* a compiled effect does — the full descriptor surface (`Builder.Shader`/`ColorFilter`/`SkiaFilter`/`Geometry`/`Compute`/`Split`/`Composite`/`NestedGraph`) plus the conveniences (`Blur`, `DropShadow`, `Saturate`, `ColorMatrix`, `Transform`, `Erode`, `Dilate`, `BlendMode`, …) and the sampler/child/track helpers. So raw Skia *image filters* (blur, drop shadow, …) are available again, now as fusable declarative nodes: a script color filter between two invariant effects fuses into one GPU pass. The interim `GeometrySession` `Session` global (which exposed only a canvas, never image filters) existed only on this unreleased branch and was replaced by `Builder` on the same branch — it never shipped in a release. Custom canvas drawing stays available through `Builder.Geometry(session => { ... })`. The script runs at describe time every frame; a script may branch on `Progress`/`Time` to emit different structures (the plan recompiles once per crossing), and a runtime exception during describe degrades the effect to identity (pass-through) without crashing the render.

## Unchanged (explicit non-breaks)

- `FilterEffect` subclassing as the plugin model; `Resource` capture; the ability to supply a custom `FilterEffectRenderNode` (the 003 escape hatch — the *shape* of that seam changed, see below).
- All effect *properties*, serialization formats, and project files.
- 003 scale semantics (`OutputScale`, `EffectiveScale`, `ResolveWorkingScale`, `MaxWorkingScale`, 16 384 px clamp).
- `NodeGraphFilterEffect` user-facing behavior (internally re-described).
- The GPL/MIT boundary: no FFmpeg/IPC surface is touched by this feature.


## Release-note draft

> ### Breaking: the imperative filter-effect pipeline is removed
>
> Beutl.Engine's filter effects are now fully declarative. `FilterEffect.ApplyTo(FilterEffectContext, Resource)` and the imperative types `FilterEffectContext`, `CustomFilterEffectContext`, `FilterEffectActivator`, `EffectTarget`, `EffectTargets` and `SKImageFilterBuilder` were removed. Effects override the now-abstract `FilterEffect.Describe(EffectGraphBuilder, Resource)` and append node descriptors (`Shader`, `ColorFilter`, `SkiaFilter`, `Geometry`, `Compute`, `Split`, `Composite`, `NestedGraph`); the engine compiles, caches and executes the graph — adjacent color effects fuse into a single GPU pass, intermediate buffers are pooled, and animated parameters no longer rebuild the pipeline.
>
> `FilterEffectRenderNode` is abstract. Derive from `PlanFilterEffectRenderNode` for standard plan execution with narrow policy hooks; derive directly and use `CustomRenderNodeFilterEffect` only for a complete opaque `Process` implementation.
>
> Plugin authors: see `docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md` for the symbol-by-symbol migration map. Effect *properties*, serialization formats and project files are unchanged.
>
> C# script effects: scripts that used `Context` fail at script compile time with a diagnostic naming the migration guide. Scripts now author the same declarative graph a compiled effect does through the `Builder` global (`EffectGraphBuilder`): `Context.Blur(...)` becomes `Builder.Blur(...)`, blur/shadow/color filtering is available inline again (and fuses), and custom canvas drawing uses `Builder.Geometry(session => { ... })`.
