# Contract: Breaking Changes & Migration Map

**Feature**: `004-gpu-pass-fusion` | **Status: removal shipped** (rollout step 6, `refactor!(engine): remove the imperative filter-effect pipeline`). This document is the `BREAKING CHANGE:` documentation for effect authors.

All changes landed as `refactor!:` / `feat!:` Conventional Commits with a `BREAKING CHANGE:` footer naming `Beutl.Engine` (`Beutl.Graphics.Effects`). No `[Obsolete]` shims, no v2 duplicates; in-tree call sites migrated in the same change (AGENTS.md design priorities). The surviving `[Obsolete]` members are the two `CSharpScriptEffectGlobals` script diagnostics — `Context` (retyped `object` after `FilterEffectContext`'s deletion, the FR-013 diagnostic) and `Session` (retyped `object` after the interim `GeometrySession` global was replaced by `Builder` on this same branch) — kept deliberately as compile-time pointers naming `Builder` and this guide.

## Removed → replacement

| Removed (public today) | Replacement | Migration note |
|---|---|---|
| `FilterEffect.ApplyTo(FilterEffectContext, Resource)` | `FilterEffect.Describe(EffectGraphBuilder, Resource)` | same append idiom; convenience methods keep their names |
| `FilterEffectContext` (recording surface, `AppendSkiaFilter`, `AppendSKColorFilter`, `CustomEffect<T>`) | `EffectGraphBuilder` (`SkiaFilter`, `ColorFilter`, `Shader`, `Compute`, `Geometry`, `Split`, `Composite` + conveniences) | factories become descriptors; `CustomEffect` callbacks become `GeometryNode` sessions or `ShaderNode`s |
| `CustomFilterEffectContext` (`CreateTarget`, `Open`, `ForEach`, target mutation) | `GeometrySession` (`OpenCanvas`, `Inputs`, scales) | target creation/flushing is executor-owned; multi-target flows become multiple nodes |
| `FilterEffectActivator` | `PlanExecutor` (internal) | no public replacement — execution is engine-owned |
| `EffectTarget` / `EffectTargets` (mutable) | `EffectInput` (read-only) for authors; `ResourcePlan` internally | |
| `SKImageFilterBuilder` (public) | internal compiler detail of `SkiaFilterPass` | |
| `SKSLShader.ApplyToNewTarget(CustomFilterEffectContext, …)` | `ShaderNodeDescriptor` (whole-source or snippet) | **final disposition**: `SKSLShader` survives as the compile/holder type (`Create`/`TryCreate`/`CreateBuilder`/`Effect`); only the activator entry point was deleted |
| `GLSLShader.Apply/ApplyMultiPass(CustomFilterEffectContext, …)` | `ComputeNodeDescriptor` | **final disposition**: `GLSLShader` survives as the compile/holder type with internal single-pass execution used by the compute executor; pipeline creation/ping-pong is executor-owned |
| `CSharpScriptEffect` script globals typed on `CustomFilterEffectContext` | globals expose `Builder` (`EffectGraphBuilder`) + `Progress`/`Duration`/`Time` | **breaks user scripts** (maintainer-approved): a script now authors the declarative graph exactly like a compiled effect (`Builder.Blur(...)`, `Builder.Geometry(...)`, …); legacy `Context`- and interim `Session`-based scripts fail at script compile time with a diagnostic naming `Builder` and this guide — never silently wrong output; before/after sample below |

## Behavioral changes (allowed by spec)

- Rendered output of migrated shader effects may differ within the golden thresholds (SSIM ≥ 0.99 / MAE ≤ 0.02, linear light) — floating-point rounding of fused programs. Byte-identity is not claimed (spec Assumptions).
- Per-effect intermediate targets, per-effect flushes, and full-frame snapshots between custom effects disappear; any out-of-tree code that observed them (timing, memory) sees different numbers.
- **Allocation-failure behavior is normalized** (FR-015): preview drops the failed pass output and continues; delivery throws. The legacy surface was path-dependent (`Flush` drop-or-throw vs `CreateTarget` returning an empty target whose `Open` threw unconditionally); that divergence is intentionally not reproduced.

> Internal-only removals (`IFEItem`, `FEItem_*`, activator internals — data-model §7) need no plugin migration; this table maps only the public surface.

### Rows that changed shape during implementation

- **`NestedGraphNodeDescriptor` / `EffectGraphBuilder.NestedGraph` (added)**: meta effects whose child chain must be re-described per branch (e.g. `DelayAnimationEffect`'s per-branch delayed clock after a split fan-out) declare a nested-graph node; the executor re-describes and recursively executes the child graph per branch index. This replaced the legacy nested-activator pull.
- **Whole-source `src` tile mode (added)**: `ShaderNodeDescriptor.WholeSource(…, srcTileMode:)` declares the implicit `src` child's out-of-bounds sampling (`Clamp`/`Decal`), reproducing what each legacy custom effect chose when building its own image shader.
- **`RenderNodeContext.DeviceBufferSize` (relocated)**: the effect-pass buffer-sizing formula moved from `CustomFilterEffectContext` to `RenderNodeContext`; callers computing device-px uniforms use it unchanged.
- **Pure-generator SKSL scripts**: an `SKSLScriptEffect` script that declares no `src` child (never samples the source) runs as a geometry pass drawing the built shader over the input rect — the legacy behavior, now without the bridge.
- **Custom render-node seam reshaped: `Resource.CreateRenderNode()` + `Resource.RenderNodeType` → one `Resource.RenderNodeFactory`.** The 003 escape hatch (override to supply a custom `FilterEffectRenderNode` with a non-supply working scale) was a virtual `FilterEffectRenderNode CreateRenderNode()`; keeping the render-graph diff's reuse check alive across re-renders then required a *separately overridden* `virtual Type RenderNodeType`, and overriding one but not the other silently recompiled the plan every frame. Both are replaced by a single `virtual FilterEffectRenderNodeFactory RenderNodeFactory` — a `FilterEffectRenderNodeFactory` value that captures the node type (`typeof(TNode)`) and its constructor together via `FilterEffectRenderNodeFactory.Of<TNode>(Func<Resource, TNode>)`, so the type can no longer drift from the node created. **Migration:** `public override FilterEffectRenderNode CreateRenderNode() => new MyNode(this);` (with any paired `RenderNodeType`) becomes `public override FilterEffectRenderNodeFactory RenderNodeFactory => FilterEffectRenderNodeFactory.Of(static r => new MyNode(r));`. `Resource.Push` stays overridable; a direct caller of `CreateRenderNode()` uses `resource.RenderNodeFactory.Create(resource)`. This seam is new-on-this-branch relative to a release only in that `RenderNodeType` was added and removed here; `CreateRenderNode()` shipped in 003, so removing it is a real break.

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

// Custom canvas drawing — the render-time bounds default is full-frame and always correct; the callback's
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
> Plugin authors: see `docs/specs/004-gpu-pass-fusion/contracts/breaking-changes.md` for the symbol-by-symbol migration map. Effect *properties*, serialization formats and project files are unchanged.
>
> C# script effects: scripts that used `Context` fail at script compile time with a diagnostic naming the migration guide. Scripts now author the same declarative graph a compiled effect does through the `Builder` global (`EffectGraphBuilder`): `Context.Blur(...)` becomes `Builder.Blur(...)`, blur/shadow/color filtering is available inline again (and fuses), and custom canvas drawing uses `Builder.Geometry(session => { ... })`.
