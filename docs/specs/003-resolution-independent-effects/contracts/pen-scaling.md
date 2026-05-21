# Contract: Pen Length Scaling

**Surface**: `Beutl.Engine/Media/Pen.cs` (no signature change) + new helpers in `Beutl.Engine/Graphics/Rendering/PenHelper.cs` + consumption-site updates in `ImmediateCanvas`, `Shape`, `PenHelper`, `StrokeEffect`.

**Audience**: anyone who reads `Pen.Resource.Thickness` (or `DashOffset` / `Offset`) for rendering purposes.

> Design rationale: see `research.md` § R9 — option (c), shared helper.

## The contract in one paragraph

`Pen.Resource.Thickness`, `DashOffset`, and `Offset` continue to hold the **user-authored, raw** value as before. **Reading them directly is fine for non-rendering purposes** (bounding-box computation at the project level, animation evaluation, serialization). For rendering, callers go through `PenHelper.GetScaledThickness(pen, scale)` / `GetScaledDashOffset(...)` / `GetScaledOffset(...)`, which multiplies by `RenderScale.ApplyUniform(1)` (stroke widths are scalar — single uniform multiplier, not per-axis). A caller that wants raw-raster thickness in a rendering path still reads `pen.Thickness` directly (the helper is opt-in).

## Surface — new helpers on `PenHelper`

```csharp
namespace Beutl.Graphics.Rendering
{
    public static partial class PenHelper
    {
        // Returns pen.Thickness * scale.ApplyUniform(1).
        public static float GetScaledThickness(Pen.Resource pen, RenderScale scale);
        // Returns pen.DashOffset * scale.ApplyUniform(1).
        public static float GetScaledDashOffset(Pen.Resource pen, RenderScale scale);
        // Returns pen.Offset * scale.ApplyUniform(1).
        public static float GetScaledOffset(Pen.Resource pen, RenderScale scale);

        // Convenience overload — common pattern in the call sites: combine the
        // existing GetRealThickness (Inside/Center/Outside alignment) with scaling.
        public static float GetScaledRealThickness(StrokeAlignment alignment, Pen.Resource pen, RenderScale scale);
    }
}
```

Why uniform `ApplyUniform(1)` instead of axis-specific: stroke thickness is a scalar quantity that has no axis. If the surrounding `RenderScale` is non-uniform (which is forbidden by FR-005, but defensively handled), the helper uses the geometric mean. Practically, `RenderScale.FromFrames` already enforces uniformity, so `ScaleX == ScaleY` and uniform == per-axis.

## Properties that stay raw

- `Pen.MiterLimit` — multiplier of thickness (already self-scales since thickness scales).
- `Pen.TrimStart`, `Pen.TrimEnd`, `Pen.TrimOffset` — percentages 0..100.

## Consumption-site updates

The audit identified ~7 sites that read `pen.Thickness` directly for rendering:

| Site | Update |
|---|---|
| `src/Beutl.Engine/Graphics/ImmediateCanvas.cs` (5 reads) | replace `pen.Thickness` with `PenHelper.GetScaledThickness(pen, this.RenderScale)` for the rendering paths (where the canvas is actively materializing to Skia). |
| `src/Beutl.Engine/Graphics/Shapes/Shape.cs` (`GetRealThickness`) | switch to `PenHelper.GetScaledRealThickness(pen.StrokeAlignment, pen, renderScale)`. The `Shape` already has access to the rendering `GraphicsContext2D` and thus to `RenderScale`. |
| `src/Beutl.Engine/Graphics/Rendering/PenHelper.cs` (existing internal helpers) | extend with the new scaled helpers above. Existing `GetRealThickness` stays for non-rendering callers. |
| `src/Beutl.Engine/Graphics/FilterEffects/StrokeEffect.cs` | switch the thickness read used to compute Skia stroke width to the scaled helper. (The effect already runs inside a `FilterEffectContext` and has access to `RenderScale`.) |

Bounding-box computation paths (`PenHelper.GetBounds(rect, pen)`) intentionally keep using `pen.Thickness` directly — bounds are project-space and should not include rendering-time scale.

### Project-space vs render-space bounds (audit obligation)

The design review surfaced a risk: stroke-thickness scaling at render time can diverge from raw-thickness bounds, and that divergence can quietly break invalidation, clipping, hit-testing, or effect-extent calculations if any of those paths uses `PenHelper.GetBounds(...)` (project-space) but the actual rendering uses a scaled stroke. To keep the divergence safe, every consumer of `PenHelper.GetBounds(...)` (and any other API that returns "the rectangle a stroke covers") MUST be classified as one of:

- **Project-space consumer** (raw thickness is correct) — bounding-box for project-level math, animation evaluation, hit-testing against authored geometry. These read `pen.Thickness` (raw) directly via the existing `GetBounds`.
- **Render-space consumer** (scaled thickness is correct) — invalidation regions submitted to the raster compositor, clip rectangles enforced at render time, effect-extent calculations that feed into raster filters. These MUST switch to a new `PenHelper.GetScaledBounds(rect, pen, renderScale)` that applies the same `RenderScale` multiplication as `GetScaledThickness` before computing the bounds.

The audit is part of Phase 2 Block D (`tasks.md` T014) — enumerate every call site of `PenHelper.GetBounds(...)`, classify it, and either keep raw or migrate to `GetScaledBounds`. The PR description records the classification result; reviewers can sanity-check it against this contract.

### `PenHelper.GetScaledBounds` (new helper)

```csharp
public static partial class PenHelper
{
    // Project-space (existing — unchanged):
    public static Rect GetBounds(Rect rect, Pen.Resource? pen);

    // Render-space (new — applies RenderScale before computing stroke expansion):
    public static Rect GetScaledBounds(Rect rect, Pen.Resource? pen, RenderScale scale);
}
```

`GetScaledBounds` internally uses `GetScaledThickness(pen, scale)` so the expansion of `rect` matches what the rasterizer will actually draw.

## Opt-out for raw thickness in a rendering path

A custom effect or drawable that explicitly wants raw-raster thickness reads `pen.Thickness` directly and does not call `PenHelper.GetScaledThickness`. There is no separate `*Raw` API method on `Pen` itself — the raw read *is* the opt-out.

## Sub-pixel / zero handling

- Zero thickness → unchanged (no stroke drawn anyway).
- Sub-pixel positive scaled thickness (e.g. `4 * 0.25 = 1`) passes through to Skia, which has its own minimum-width handling.
- `NaN` thickness on the resource is unexpected — `Pen.Resource.Update` does not currently guard, and adding a guard is out of scope for this PR.

## Backward compatibility

- Today: `RenderScale.Identity` → scaled helpers return the same value as a raw read → no observable change.
- After proxy preview ships: stroked geometry scales correctly. Plugins / custom Drawables that reach into `pen.Thickness` for rendering and want to opt out simply do not call the helper.
