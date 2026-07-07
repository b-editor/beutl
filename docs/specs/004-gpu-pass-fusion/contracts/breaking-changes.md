# Contract: Breaking Changes & Migration Map

**Feature**: `004-gpu-pass-fusion` | **Status: removal shipped** (rollout step 6, `refactor!(engine): remove the imperative filter-effect pipeline`). This document is the `BREAKING CHANGE:` documentation for effect authors.

All changes landed as `refactor!:` / `feat!:` Conventional Commits with a `BREAKING CHANGE:` footer naming `Beutl.Engine` (`Beutl.Graphics.Effects`). No `[Obsolete]` shims, no v2 duplicates; in-tree call sites migrated in the same change (AGENTS.md design priorities). The sole surviving `[Obsolete]` member is `CSharpScriptEffectGlobals.Context` (retyped `object` after `FilterEffectContext`'s deletion), kept deliberately as the FR-013 compile-time script diagnostic pointing at this guide.

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
| `CSharpScriptEffect` script globals typed on `CustomFilterEffectContext` | globals typed on `GeometrySession` | **breaks user scripts** (maintainer-approved): legacy scripts fail at script compile time with a diagnostic referencing this guide — never silently wrong output; before/after sample below |

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

After (bounded `Session` over the pass output; the canvas is pre-filled with the input):

```csharp
var canvas = Session.OpenCanvas();
using (canvas.PushOpacity(0.8f))
using (canvas.PushDeviceSpace())
    Session.Inputs[0].Draw(canvas, default);          // re-composite the input
canvas.Canvas.DrawCircle(40, 40, 20 * Session.WorkingScale, new SkiaSharp.SKPaint());
```

**Capability note**: a C# script can no longer apply raw Skia *image filters* (blur, drop shadow, …) through the session — `GeometrySession` deliberately exposes no image-filter surface, so resource lifetimes and pass scheduling stay executor-owned. Compose dedicated effects (`Blur`, `DropShadow`, …) in the effect chain next to the script instead; the script draws, the chain filters.

## Unchanged (explicit non-breaks)

- `FilterEffect` subclassing as the plugin model; `Resource` capture; `Resource.CreateRenderNode()` / custom `FilterEffectRenderNode` overrides (003 seam).
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
> C# script effects: scripts that used `Context` fail at script compile time with a diagnostic naming the migration guide. Draw through the new `Session` global (`GeometrySession`: `OpenCanvas()`, `Inputs`, `WorkingScale`); apply blur/shadow-style filtering with dedicated effects in the chain rather than inside the script.
