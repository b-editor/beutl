# Data Model: Resolution-Independent Pixel-Absolute Effects

This document enumerates the new types, modified types, and effect-property migrations introduced by this feature.

> **Design pivot (post-T001-audit)**: The original design introduced three wrapper structs (`PixelLength` / `PixelExtent` / `PixelOffset`) plus matching animators and property editors, with a per-effect property-type migration. **That approach has been replaced.** The current design adds **only `RenderScale`** as a new type; scaling is applied inside the existing `FilterEffectContext` helper methods (`Blur(Size)`, `DropShadow(Point, Size, Color)`, …), with a `*Raw` variant of each helper as an opt-out. Effects keep their existing `IProperty<Size>` / `IProperty<Point>` / `IProperty<float>` declarations verbatim. See `research.md` § R2 for the rationale. The sections below have been updated accordingly; the obsolete wrapper-type definitions are removed.

## New types

### `RenderScale` — `Beutl.Graphics.Rendering.RenderScale`

A small value type carrying the per-axis ratio of the current render raster to the reference frame:

```csharp
public readonly struct RenderScale : IEquatable<RenderScale>
{
    public RenderScale(float scaleX, float scaleY);

    public float ScaleX { get; }
    public float ScaleY { get; }

    public static RenderScale Identity { get; } // ScaleX = ScaleY = 1.0f

    public static RenderScale FromFrames(PixelSize renderTarget, PixelSize referenceFrame);

    public float ApplyX(float referencePixels);   // returns referencePixels * ScaleX
    public float ApplyY(float referencePixels);
    public Size  Apply(Size referenceSize);       // (size.Width * ScaleX, size.Height * ScaleY)
    public Point Apply(Point referencePoint);     // (point.X * ScaleX, point.Y * ScaleY)
    public float ApplyUniform(float referencePixels); // average — for genuinely 1-D things
}
```

- **Identity** is the value every existing `Renderer` produces today, so behavior is unchanged when nothing else opts in.
- **Validation**:
  - Constructor: `ScaleX > 0`, `ScaleY > 0` and both finite. Throws `ArgumentOutOfRangeException` otherwise.
  - `FromFrames(renderTarget, referenceFrame)` requires the per-axis ratios to be uniform within tolerance: `|sx − sy| ≤ 1e-3 * max(sx, sy)`. Non-uniform ratios throw `ArgumentException` with a message naming both ratios. This enforces the spec's "proxy must be a uniform scale of export" rule at the API boundary, so any future proxy-preview UI that forgets to snap is caught immediately rather than producing silently warped output.
- **Equality**: bitwise on the two floats.

### (No further new types)

The earlier draft of this document introduced three wrapper structs and three matching animators. The current design needs none of them — see § "Design pivot" above and `research.md` § R2.

## Modified types

### `IRenderer` / `Renderer` / `SceneRenderer`

Add `ReferenceFrame`:

```csharp
public interface IRenderer
{
    PixelSize FrameSize       { get; } // existing — the raster being drawn into
    PixelSize ReferenceFrame  { get; } // NEW — the reference for Pixel* wrappers
    RenderScale RenderScale   => RenderScale.FromFrames(FrameSize, ReferenceFrame);
}
```

- Default for `Renderer(int width, int height)` → `ReferenceFrame = FrameSize` (i.e. `RenderScale = Identity`). All existing callers behave identically.
- New overload `Renderer(int width, int height, PixelSize referenceFrame)` for future proxy-preview / explicit-scale callers.
- `SceneRenderer` continues to use `Scene.FrameSize` as both `FrameSize` and `ReferenceFrame`. When a future proxy-preview feature wants to render smaller, it constructs `new Renderer(proxyW, proxyH, scene.FrameSize)` (or a `SceneRenderer` overload taking a proxy size).

### `GraphicsContext2D`

Add a push-down stack for `(ReferenceFrame, RenderScale)`:

```csharp
public sealed class GraphicsContext2D : ...
{
    public PixelSize ReferenceFrame { get; }   // top of stack
    public RenderScale RenderScale  { get; }   // derived

    public PushedState PushReferenceFrame(PixelSize referenceFrame);
    // existing PushTransform / PushClip / PushLayer unchanged
}
```

Initial values come from the constructing `Renderer`. `PushReferenceFrame(...)` swaps them; the returned `PushedState.Dispose` restores.

### `FilterEffectContext`

Change the semantics of every existing length-taking helper to apply `RenderScale` internally, and add a `*Raw` opt-out variant of each:

```csharp
public sealed class FilterEffectContext : IDisposable
{
    public RenderScale RenderScale     { get; } // snapshot at construction
    public PixelSize  ReferenceFrame   { get; }

    // CHANGED — every length-typed argument is now interpreted as
    // "pixels at the project's export resolution"; the implementation
    // multiplies by RenderScale before forwarding to the underlying
    // Skia / EffectActivator path.
    public void Blur(Size sigma);
    public void DropShadow(Point position, Size sigma, Color color);
    public void DropShadowOnly(Point position, Size sigma, Color color);
    public void InnerShadow(Point position, Size sigma, Color color);
    public void InnerShadowOnly(Point position, Size sigma, Color color);
    public void Erode(float radiusX, float radiusY);
    public void Dilate(float radiusX, float radiusY);
    // … plus any other existing helpers whose arguments include lengths.

    // NEW — explicit opt-out for the niche case where a plugin wants
    // raw-raster pixel semantics (e.g. snap to physical pixel grid).
    // These forward verbatim without applying RenderScale.
    public void BlurRaw(Size sigma);
    public void DropShadowRaw(Point position, Size sigma, Color color);
    public void DropShadowOnlyRaw(Point position, Size sigma, Color color);
    public void InnerShadowRaw(Point position, Size sigma, Color color);
    public void InnerShadowOnlyRaw(Point position, Size sigma, Color color);
    public void ErodeRaw(float radiusX, float radiusY);
    public void DilateRaw(float radiusX, float radiusY);
    // … pair every newly-scaled helper with a *Raw twin.
}
```

The signatures of the scaled helpers are byte-identical to their pre-feature versions — call sites do not change. The behavior change is invisible while `RenderScale == Identity` (the only state that exists today, since no proxy preview UX has shipped) and silently produces correct output once a future proxy-preview feature constructs a renderer with `RenderScale ≠ Identity`. Plugins that wanted raw-raster behavior on purpose switch one method-name letter group (e.g. `Blur` → `BlurRaw`).

### `SceneDrawable`

`Render(GraphicsContext2D context, Resource resource)` wraps its draw in:

```csharp
using (context.PushReferenceFrame(r.ReferencedScene.FrameSize))
{
    // existing inner draw
}
```

### `LayerEffect`

Mirror the `SceneDrawable` pattern when activating its sub-graph.

## In-scope built-in effects (no source migration; helper contract suffices)

> **Design pivot**: Under the helper-internal-scaling design, "in scope" no longer means "the effect's source has to change" — every column except "Effect file" and "Helpers called" is **purely descriptive**. The effect's `*.cs` file is **not edited**. The table is kept so that test fixtures and reviewers can confirm which helpers must scale correctly for each effect.

| Effect file | Length-typed property | `FilterEffectContext` helper(s) that scale it | Notes |
|---|---|---|---|
| `Blur.cs` | `Sigma: IProperty<Size>` | `context.Blur(Size)` | — |
| `DropShadow.cs` | `Position: IProperty<Point>`, `Sigma: IProperty<Size>` | `context.DropShadow(Point, Size, Color)`, `context.DropShadowOnly(Point, Size, Color)` | — |
| `InnerShadow.cs` | `Position: IProperty<Point>`, `Sigma: IProperty<Size>` | `context.InnerShadow(Point, Size, Color)`, `context.InnerShadowOnly(Point, Size, Color)` | — |
| `StrokeEffect.cs` | `Offset: IProperty<Point>` | scaling helper used for the offset translation | `Pen.Thickness` follow-up — research R6. Stroke draws via `Canvas` not via a `FilterEffectContext` length helper, so thickness stays raw-pixel. |
| `Erode.cs` | `RadiusX / RadiusY: IProperty<float>` | `context.Erode(float, float)` | — |
| `Dilate.cs` | `RadiusX / RadiusY: IProperty<float>` | `context.Dilate(float, float)` | — |
| `FlatShadow.cs` | `Length: IProperty<float>` (Angle stays raw) | the scaled custom-effect helper FlatShadow already uses | Angle, Brush, ShadowOnly stay raw. |
| `ColorShift.cs` | `RedOffset / GreenOffset / BlueOffset / AlphaOffset: IProperty<Beutl.Media.PixelPoint>` (integer) | the scaled custom-effect / SKSL uniform binding | Integer offsets pass through the same scaling rule (multiply by `RenderScale`, then truncate / round at the rasterizer). Project file shape unchanged. |
| `DisplacementMapTransform.cs` | Three subclasses: `Translate.X/Y`, `Scale.CenterX/CenterY`, `Rotation.CenterX/CenterY` (each `IProperty<float>`) | the scaled SKSL-uniform binding for each subclass | `Scale*` (percent) and `Rotation` (degrees) stay raw. |
| `MosaicEffect.cs` | `TileSize: IProperty<Size>` | the scaled custom-effect helper MosaicEffect uses | `Origin: RelativePoint` is normalized 0..1 — stays raw. |
| `ShakeEffect.cs` | `StrengthX: IProperty<float>`, `StrengthY: IProperty<float>` | the scaled custom-effect helper ShakeEffect uses | `Speed: float` is a frequency — stays raw. |
| `SplitEffect.cs` | `HorizontalSpacing / VerticalSpacing: IProperty<float>` | the scaled custom-effect helper SplitEffect uses | `HorizontalDivisions / VerticalDivisions: int` are counts — stay raw. |
| `Clipping.cs` | `Left / Top / Right / Bottom: IProperty<float>` | the scaled custom-effect helper Clipping uses | `AutoCenter / AutoClip: bool` stay raw. |
| `PartsSplitEffect.cs` | — | — | No public pixel-absolute properties — operation is purely contour-driven. |
| `TransformEffect.cs` | — (direct) | — | Pixel translation lives inside the referenced `Transform` (in `Beutl.Graphics.Transformation.*`, out of scope). |
| Out-of-scope (verified by audit) | `Brightness`, `Saturate`, `HueRotate`, `Gamma`, `Invert`, `Threshold`, `Negaposi`, `ColorGrading`, `HighContrast`, `ColorKey`, `ChromaKey`, `LutEffect`, `DelayAnimationEffect`, `PathFollowEffect`, `PixelSortEffect`, `Lighting`, `BlendEffect`, `Curves`, `PerlinNoise`, `DisplacementMapEffect` (wrapper) | n/a | dimensionless / non-pixel / parameter-less — nothing to scale. |

**Effect-count summary**: **13 effects benefit automatically** — `Blur`, `DropShadow`, `InnerShadow`, `StrokeEffect`, `Erode`, `Dilate`, `FlatShadow`, `ColorShift`, `DisplacementMapTransform` (3 subclasses), `MosaicEffect`, `ShakeEffect`, `SplitEffect`, `Clipping`. `PartsSplitEffect` and `TransformEffect` have no length-typed helpers in scope (see T001 audit). **None of these effects' `*.cs` files are edited by this feature** — they automatically become resolution-independent the moment `FilterEffectContext`'s helpers gain the scaling step.

### Property-editor registration

> **Design pivot**: Not applicable. With no new wrapper types, no new editor registrations are needed. The existing `typeof(float)` / `typeof(Point)` / `typeof(Size)` editor entries in `src/Beutl/Services/PropertyEditorService.cs` keep handling the unchanged property types. **No source change** in `src/Beutl/Services/` or `src/Beutl/ViewModels/Editors/`.

### Deferred follow-ups discovered by the audit

- **`Beutl.Graphics.Transformation.*` (e.g. `TranslateTransform`, `RotateTransform`, `ScaleTransform`)**: same helper-internal-scaling treatment is appropriate but the equivalent helpers (e.g. `Canvas.PushTransform`) ship with `Beutl.Graphics`-level Drawables, not just FilterEffects. Bundling here would balloon scope — track as a follow-up feature alongside `Pen.Thickness`.
- **`Pen.Thickness`**: already deferred per research R6.

If a later code change introduces a new pixel-absolute parameter, it MUST be added to this table and given a test.

## Serialization

**Project files do not change shape.** No property type is renamed; `Size` stays `Size`, `Point` stays `Point`, `float` stays `float`. FR-003 ("no project-file migration step") is satisfied automatically because nothing on disk needs to round-trip into a different schema.

The behavioural guarantee that legacy projects render identically at export resolution comes from the fact that `RenderScale.Identity` makes the new helper-internal multiplication a no-op: `sigma * Identity == sigma`.

## Reference-frame contract (summary, full text in `contracts/render-scale.md`)

- `Renderer.ReferenceFrame` is set once at construction.
- `GraphicsContext2D.ReferenceFrame` is initialized from the constructing `Renderer` and overridden by `PushReferenceFrame(...)`.
- `SceneDrawable.Render` MUST push the sub-scene's `FrameSize`.
- `LayerEffect` (and any future container that materializes a separate raster) MUST push appropriately.
- `FilterEffectContext` snapshots `(ReferenceFrame, RenderScale)` at construction; once captured it does not change for the lifetime of that `FilterEffectContext`.

## Validation summary

| Rule | Enforced where |
|---|---|
| `RenderScale.ScaleX > 0`, `ScaleY > 0`, both finite | `RenderScale` constructor |
| `RenderScale.FromFrames` uniform-scale enforcement (`|sx − sy| ≤ 1e-3 * max(sx, sy)`) | `RenderScale.FromFrames` factory — throws `ArgumentException` otherwise |
| `NaN` / negative-length rejection on length-typed arguments to scaled helpers | inside each `FilterEffectContext` scaled helper — argument guard before multiplication |
| Resolved value clamps to rasterizer minimum (FR-009) | inside each scaled helper — `Math.Max(scaled, MinSupported)` where `MinSupported = 0` for zero-preservation or 1 for "must-not-be-invisible" |
| Zero stays zero (FR-009) | scaled-helper multiplication preserves `0` exactly (`0 * scale == 0`) |
| `*Raw` helpers bypass every scaling step and pass through verbatim | `*Raw` helper implementations — single forwarding call to the underlying Skia builder |

## State transitions

None. The wrappers are immutable value types; the scale plumbing is a simple stack. No state machine.
