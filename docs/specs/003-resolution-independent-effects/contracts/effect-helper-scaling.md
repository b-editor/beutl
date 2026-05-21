# Contract: FilterEffectContext Helper Scaling

**Surface**: every length-taking method on `Beutl.Graphics.Effects.FilterEffectContext` (`Blur`, `DropShadow`, `DropShadowOnly`, `InnerShadow`, `InnerShadowOnly`, `Erode`, `Dilate`, plus any other helper whose argument has units of pixels) — and their `*Raw` twins.

**Audience**: built-in effect authors, third-party plugin authors, scripting users (`CSharpScriptEffect`, `GLSLScriptEffect`).

> **Design pivot**: This file replaces the obsolete `parameter-wrappers.md`. Earlier drafts introduced wrapper structs (`PixelLength` / `PixelExtent` / `PixelOffset`) and asked plugins to opt in by changing their `IProperty<…>` declarations. That design has been abandoned — see `research.md` § R2. The current design changes only the semantics of the existing `FilterEffectContext` helpers and adds a `*Raw` twin per helper as an opt-out. **Plugin authors do nothing to benefit; they call the new `*Raw` variant if they need raw-raster behavior.**

## Stability

The semantic change to existing helpers is the contract. The added `*Raw` methods are public API on `Beutl.Engine`. Per the repo's "adopt better designs eagerly" priority, the `*Raw` names are open for one round of PR feedback; after the first release they follow normal semver discipline.

## The contract in one paragraph

Every existing `FilterEffectContext` helper that takes a length-typed argument (`Size`, `Point`, `float`-as-length) interprets that argument as **"pixels measured against the project's export resolution"**. The implementation multiplies the argument by the context's snapshotted `RenderScale` before forwarding to the underlying Skia / `EffectActivator` path. The result is that a project rendered at a different raster size produces visually equivalent output without any change to the caller. A `*Raw` variant of every such helper is provided that passes its arguments through verbatim, for plugins that want raw-raster pixel semantics.

## When the new behavior triggers

Today every `Renderer` is constructed with `RenderScale.Identity`, so the multiplication is a no-op and behavior is byte-identical to before. The behavior change becomes observable only when a future proxy-preview UX (out of scope for this feature — see `research.md` § R1) constructs a renderer with `RenderScale ≠ Identity`. At that point all existing built-in effects and all plugins that use the standard helpers become resolution-independent automatically.

## Surface — scaled helpers

These keep their existing signatures from `Beutl.Engine` pre-feature. **No call site changes.** Only the documented semantic changes.

```csharp
namespace Beutl.Graphics.Effects
{
    public sealed class FilterEffectContext : IDisposable
    {
        public RenderScale RenderScale  { get; } // snapshot at construction
        public PixelSize  ReferenceFrame { get; }

        // Length-taking helpers — now apply RenderScale internally.
        public void Blur(Size sigma);
        public void DropShadow(Point position, Size sigma, Color color);
        public void DropShadowOnly(Point position, Size sigma, Color color);
        public void InnerShadow(Point position, Size sigma, Color color);
        public void InnerShadowOnly(Point position, Size sigma, Color color);
        public void Erode(float radiusX, float radiusY);
        public void Dilate(float radiusX, float radiusY);
        // … plus every other helper whose argument has length units.
    }
}
```

Non-length arguments (`Color`, blend modes, scalar percentages, booleans) are not touched.

## Surface — `*Raw` opt-out variants

These are new on `FilterEffectContext`. They are byte-identical to the pre-feature helpers in semantics (no scaling). Plugins that want raw-raster behavior switch one method-name suffix.

```csharp
namespace Beutl.Graphics.Effects
{
    public sealed partial class FilterEffectContext
    {
        public void BlurRaw(Size sigma);
        public void DropShadowRaw(Point position, Size sigma, Color color);
        public void DropShadowOnlyRaw(Point position, Size sigma, Color color);
        public void InnerShadowRaw(Point position, Size sigma, Color color);
        public void InnerShadowOnlyRaw(Point position, Size sigma, Color color);
        public void ErodeRaw(float radiusX, float radiusY);
        public void DilateRaw(float radiusX, float radiusY);
        // … one Raw twin per scaled helper.
    }
}
```

## How a built-in effect looks under the new contract

Unchanged from before this feature. Example — `Blur.cs`:

```csharp
[Display(Name = nameof(GraphicsStrings.Sigma), ResourceType = typeof(GraphicsStrings))]
public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

public override void ApplyTo(FilterEffectContext context, Resource r)
{
    context.Blur(r.Sigma);   // now resolution-independent — author did nothing
}
```

The `Property.CreateAnimatable(Size.Empty)` line is unchanged. The `context.Blur(r.Sigma)` line is unchanged. The behavior is changed by `FilterEffectContext` itself.

## How a plugin opts out

Imagine a plugin that needs the user-typed value to mean "5 raster pixels regardless of resolution" (e.g. snap-to-pixel-grid effects, screen-relative debug overlays). Change `Blur` to `BlurRaw`:

```diff
- context.Blur(r.Sigma);
+ context.BlurRaw(r.Sigma);
```

That is the entirety of the opt-out path.

## Plugin author migration (authoritative cross-surface table)

This is the single source of truth for the plugin migration contract. If anything in `spec.md` or another contract document disagrees with this table, the table wins and the other document must be reconciled.

| Plugin-code shape | Action required to benefit | Opt-out path | Notes |
|---|---|---|---|
| Calls a `FilterEffectContext` helper (`Blur(Size)`, `DropShadow(Point, Size, Color)`, `Erode(float, float)`, …) | **None** — automatic on rebuild. | Switch the call to its `*Raw` twin (`BlurRaw`, `DropShadowRaw`, `ErodeRaw`, …). One method-name change per call site. | This is the 99% case for filter-effect plugins. |
| Calls a `GraphicsContext2D` direct helper (`DrawRectangle(Rect)`, `DrawEllipse(Rect)`, `PushClip(Rect)`, `PushLayer(Rect)`, `PushOpacityMask(...)`) | **None** — automatic on rebuild. | Switch to the corresponding `*Raw` twin. | Covers most custom `Drawable` subclasses. |
| Calls `GraphicsContext2D.PushTransform(Matrix)` or `PushTransform(Transform.Resource)` | **None** — automatic. Scaling happens at render-node application (`ImmediateCanvas`), not at `PushTransform` call time. | Switch to `PushTransformRaw(Matrix)` / `PushTransformRaw(Transform.Resource)`. The raw node records `IsRaw = true` and skips render-time scaling. | The Transform path is special — see `transform-scaling.md` and `research.md` § R10. |
| Custom `Transform` subclass with a non-trivial `CreateMatrix` override (returning a project-space matrix) | **None** — automatic on rebuild. `CreateMatrix` continues to return project-space matrices; `ImmediateCanvas.PushTransform` scales the result. | Wrap the matrix in a `MatrixTransform` and push via `PushTransformRaw(Transform.Resource)`. | Earlier draft of this contract required custom Transforms to opt in by reading `context.RenderScale` inside `CreateMatrix`. That requirement is **gone** under the render-node-application design. |
| Reads `pen.Thickness` (or `DashOffset` / `Offset`) directly in a rendering path | **Manual** — switch the read to `PenHelper.GetScaledThickness(pen, renderScale)` (or `GetScaledDashOffset` / `GetScaledOffset`). | Keep the raw `pen.Thickness` read — that **is** the opt-out for raw-raster behavior. | Pen is the one surface where opt-in is manual; see `research.md` § R9 for why. Bounds-computing code that reads `pen.Thickness` for project-space math is fine as-is — see `pen-scaling.md` § "Project-space vs render-space bounds". |
| Reads `Transform.Resource.Matrix` directly (for bounding-box / animation / non-rendering math) | **None** — `Matrix` is authoring-space, which is what these consumers want. | n/a — they read authoring-space by definition. | If you need a render-space matrix, multiply the translation column by the surrounding `RenderScale` yourself. |
| Calls a `Geometry` / `TextBlock` / `Brush`-rectangle API | **Deferred** — out of scope this PR. | n/a (no scaling, no opt-out). | These surfaces become resolution-independent in follow-up PRs. See `data-model.md` § "Deferred follow-ups". |

For scripting users:

- `CSharpScriptEffect`: rows 1–4 of the table above apply. Existing scripts pick up the new scaling on the next `Beutl.Engine` upgrade.
- `GLSLScriptEffect` / `SKSLScriptEffect`: shader uniforms whose units are pixels and that are bound by the host should pass through the same `RenderScale` multiplication. Documented in the shader-binding section of the script-effect guide (separate work, tracked as a Phase 6 task).

## Sub-pixel / zero handling (FR-009)

Inside each scaled helper, after multiplying by `RenderScale`:

- **Zero**: `0 * scale == 0` exact for finite scales — zero values pass through.
- **Sub-pixel positive**: `0 < scaled < 1` — let the rasterizer (Skia) handle it; do not clamp to zero, since the user authored a non-zero value.
- **Negative inputs**: rejected at the helper boundary with `ArgumentOutOfRangeException` for helpers where negative length is nonsensical (sigma, radius). Helpers where negative is meaningful (positional offsets like `DropShadow.Position`) pass through.
- **NaN inputs**: rejected at the helper boundary with `ArgumentException`.

`*Raw` helpers apply the same NaN guard but not the rasterizer-minimum clamp — Raw is "you know what you're doing".

## Backward compatibility guarantees

- **Today (`RenderScale == Identity` everywhere)**: behavior is byte-identical to before this feature. No plugin can observe the change.
- **After proxy-preview UX ships**: any plugin that calls standard helpers gets correct resolution-independent behavior automatically. The only plugins that break are ones that intentionally depended on raw-raster pixels — those switch to `*Raw` (one method-name change per call site).
- **No project file format change.** `Size` is still serialized as `{ width, height }`; `Point` as `{ x, y }`; `float` as a number. Legacy projects open and render at export resolution byte-identically to the previous build.
- **No new property type.** `IProperty<Size>` / `IProperty<Point>` / `IProperty<float>` declarations on every effect stay verbatim. No new animator. No new property editor.
- **No `[Obsolete]` markers** — there is no API renamed or replaced.
