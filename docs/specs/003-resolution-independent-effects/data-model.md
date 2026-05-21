# Data Model: Resolution-Independent Pixel-Absolute Effects

This document enumerates the new types, modified types, and effect-property migrations introduced by this feature.

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

### `PixelLength` — `Beutl.Graphics.PixelLength`

Single-axis length measured in **reference-frame pixels** (i.e. pixels at the project's export resolution).

```csharp
public readonly struct PixelLength : IEquatable<PixelLength>, IFormattable
{
    public PixelLength(float referencePixels);
    public float ReferencePixels { get; }

    public static PixelLength Zero { get; }
    public static implicit operator PixelLength(float referencePixels); // ergonomic
    public float ResolveX(RenderScale scale);    // = ReferencePixels * scale.ScaleX
    public float ResolveY(RenderScale scale);
    public float ResolveUniform(RenderScale scale);
}
```

### `PixelExtent` — `Beutl.Graphics.PixelExtent`

2-D anisotropic extent (`Width` × `Height`) in reference-frame pixels. Used for symmetric "spread"-style parameters such as blur sigma or mosaic tile size. Named with the geometric noun **Extent** so it doesn't collide with the existing integer `Beutl.Media.PixelSize` (raster dimensions). See `research.md` § R2.

```csharp
public readonly struct PixelExtent : IEquatable<PixelExtent>, IFormattable
{
    public PixelExtent(float width, float height);
    public PixelExtent(Size referencePixels);

    public float Width  { get; }
    public float Height { get; }
    public Size ToSize();

    public static PixelExtent Empty { get; }
    public Size Resolve(RenderScale scale); // (Width * scale.ScaleX, Height * scale.ScaleY)
}
```

### `PixelOffset` — `Beutl.Graphics.PixelOffset`

2-D directional offset (`X`, `Y`) in reference-frame pixels. Used for positional translations such as a drop-shadow's offset from origin. Named with the geometric noun **Offset** so it doesn't collide with the existing integer `Beutl.Media.PixelPoint` (raster coordinates).

```csharp
public readonly struct PixelOffset : IEquatable<PixelOffset>, IFormattable
{
    public PixelOffset(float xReferencePixels, float yReferencePixels);
    public PixelOffset(Point referencePixels);

    public float X { get; }
    public float Y { get; }
    public Point ToPoint();

    public static PixelOffset Zero { get; }
    public Point Resolve(RenderScale scale); // (X * scale.ScaleX, Y * scale.ScaleY)
}
```

### Animators

For each wrapper, an `Animator<T>` is registered in `AnimatorRegistry`:

- `PixelLengthAnimator : Animator<PixelLength>` — linear interpolation on `ReferencePixels`.
- `PixelExtentAnimator : Animator<PixelExtent>` — linear per component.
- `PixelOffsetAnimator : Animator<PixelOffset>` — linear per component.

Registered in `AnimatorRegistry`'s static initialization (existing pattern for built-in types).

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

Add scale-aware overloads:

```csharp
public sealed class FilterEffectContext : IDisposable
{
    public RenderScale RenderScale     { get; } // snapshot at construction
    public PixelSize  ReferenceFrame   { get; }

    // existing overloads kept (raw pixels) for back-compat / non-pixel uses
    public void Blur(Size sigma);
    public void DropShadow(Point position, Size sigma, Color color);
    public void Erode(float radiusX, float radiusY);
    public void Dilate(float radiusX, float radiusY);
    // …

    // NEW overloads that take wrappers and resolve internally
    public void Blur(PixelExtent sigma);
    public void DropShadow(PixelOffset position, PixelExtent sigma, Color color);
    public void DropShadowOnly(PixelOffset position, PixelExtent sigma, Color color);
    public void InnerShadow(PixelOffset position, PixelExtent sigma, Color color);
    public void InnerShadowOnly(PixelOffset position, PixelExtent sigma, Color color);
    public void Erode(PixelLength radiusX, PixelLength radiusY);
    public void Dilate(PixelLength radiusX, PixelLength radiusY);
    public void ColorShift(PixelOffset r, PixelOffset g, PixelOffset b);
    // …
}
```

Effects migrate from the raw overloads to the wrapper overloads. Plugins can keep calling the raw overloads (raw-pixel behavior) or move to wrappers when they want resolution independence.

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

## In-scope built-in effect migrations

Each row below lists the existing parameter, the new wrapper type, and the test that must accompany the migration. **Files live in `src/Beutl.Engine/Graphics/FilterEffects/` unless noted.**

> Table updated by the **T001 audit** against the actual code in `src/Beutl.Engine/Graphics/FilterEffects/`. Items the audit corrected or removed are tagged in the **Source** column. The audit walked every file in the directory and inspected each `IProperty<…>` declaration.

| Effect file | Existing property (verified) | New property | Test (under `tests/Beutl.UnitTests/Engine/Graphics/FilterEffects/`) | Source |
|---|---|---|---|---|
| `Blur.cs` | `Sigma: IProperty<Size>` | `Sigma: IProperty<PixelExtent>` | `BlurResolutionTests.cs` | plan |
| `DropShadow.cs` | `Position: IProperty<Point>`, `Sigma: IProperty<Size>` | `Position: IProperty<PixelOffset>`, `Sigma: IProperty<PixelExtent>` | `DropShadowResolutionTests.cs` | plan |
| `InnerShadow.cs` | `Position: IProperty<Point>`, `Sigma: IProperty<Size>` | `Position: IProperty<PixelOffset>`, `Sigma: IProperty<PixelExtent>` | `InnerShadowResolutionTests.cs` | plan |
| `StrokeEffect.cs` | `Offset: IProperty<Point>` (Pen stays raw) | `Offset: IProperty<PixelOffset>` (Pen thickness → follow-up, see research R6) | `StrokeEffectResolutionTests.cs` | plan |
| `Erode.cs` | `RadiusX / RadiusY: IProperty<float>` | `RadiusX / RadiusY: IProperty<PixelLength>` | `ErodeResolutionTests.cs` | plan |
| `Dilate.cs` | `RadiusX / RadiusY: IProperty<float>` | `RadiusX / RadiusY: IProperty<PixelLength>` | `DilateResolutionTests.cs` | plan |
| `FlatShadow.cs` | `Length: IProperty<float>` (Angle stays raw, Brush stays raw, ShadowOnly stays raw) | `Length: IProperty<PixelLength>` | `FlatShadowResolutionTests.cs` | plan |
| `ColorShift.cs` | `RedOffset / GreenOffset / BlueOffset / AlphaOffset: IProperty<Beutl.Media.PixelPoint>` **(integer)** | each → `IProperty<PixelOffset>` (float). Wire-compatible: legacy `{"x":3,"y":5}` deserializes cleanly into floats. | `ColorShiftResolutionTests.cs` | **audit (corrected — type changes from integer to float; the old "per-channel offsets" wording hid this)** |
| `DisplacementMapTransform.cs` | File holds **three subclasses**: `DisplacementMapTranslateTransform.X / Y: IProperty<float>`; `DisplacementMapScaleTransform.Scale / ScaleX / ScaleY: IProperty<float>` (% — stay raw) + `CenterX / CenterY: IProperty<float>`; `DisplacementMapRotationTransform.Rotation: IProperty<float>` (deg — stays raw) + `CenterX / CenterY: IProperty<float>`. | each `X / Y / CenterX / CenterY` (5 properties × 3 classes) → `IProperty<PixelLength>` | `DisplacementMapTransformResolutionTests.cs` | **audit (corrected — three sibling classes, not one; `Scale*` and `Rotation` are confirmed dimensionless)** |
| `MosaicEffect.cs` | `TileSize: IProperty<Size>` (`Origin: IProperty<RelativePoint>` is normalized 0..1 and stays raw) | `TileSize: IProperty<PixelExtent>` | `MosaicEffectResolutionTests.cs` | **audit (corrected — explicit `TileSize: Size`; `Origin` is out of scope)** |
| `ShakeEffect.cs` | `StrengthX: IProperty<float>`, `StrengthY: IProperty<float>` (`Speed: IProperty<float>` is frequency, dimensionless, stays raw) | `StrengthX / StrengthY: IProperty<PixelLength>` | `ShakeEffectResolutionTests.cs` | **audit (corrected — two distinct strength axes; `Speed` is out of scope)** |
| `SplitEffect.cs` | `HorizontalSpacing / VerticalSpacing: IProperty<float>` (`Horizontal/VerticalDivisions: IProperty<int>` are counts, stay raw) | `HorizontalSpacing / VerticalSpacing: IProperty<PixelLength>` | `SplitEffectResolutionTests.cs` | plan |
| `Clipping.cs` | `Left / Top / Right / Bottom: IProperty<float>` (`AutoCenter / AutoClip: IProperty<bool>` stay raw) | each `Left / Top / Right / Bottom` → `IProperty<PixelLength>` | `ClippingResolutionTests.cs` | **audit (corrected — 4 individual `float` edges, not a single `Rect`; no dedicated `PixelRect` is needed)** |
| `PartsSplitEffect.cs` | **No public pixel-absolute properties** — operation is purely contour-driven, no user-tunable lengths | — | — | **audit (REMOVED — false entry in the original plan; T034 should be dropped from `tasks.md`)** |
| `TransformEffect.cs` | `Transform: IProperty<Transform?>`, `TransformOrigin: IProperty<RelativePoint>`, `BitmapInterpolationMode`, `ApplyToTarget: IProperty<bool>`. **No direct pixel-absolute property on the effect itself.** Pixel translation lives inside the referenced `Transform` (in `Beutl.Graphics.Transformation.*`, out of scope for this feature). | — (deferred; see "Deferred follow-ups" below) | — | **audit (REMOVED from this PR — T037 should be dropped from `tasks.md`)** |
| Out-of-scope (verified by audit) | `Brightness`, `Saturate`, `HueRotate`, `Gamma`, `Invert`, `Threshold`, `Negaposi`, `ColorGrading`, `HighContrast`, `ColorKey`, `ChromaKey`, `LutEffect`, `DelayAnimationEffect`, `PathFollowEffect`, `PixelSortEffect`, `Lighting`, `BlendEffect`, `Curves`, `PerlinNoise`, `DisplacementMapEffect` (wrapper; its `Transform` child is migrated above) | dimensionless / non-pixel / parameter-less — no migration | — | audit |

**Effect-count summary after audit**: **13 effects in scope** — `Blur`, `DropShadow`, `InnerShadow`, `StrokeEffect`, `Erode`, `Dilate`, `FlatShadow`, `ColorShift`, `DisplacementMapTransform` (3 subclasses), `MosaicEffect`, `ShakeEffect`, `SplitEffect`, `Clipping`. Was 15 in the original plan; the audit dropped `PartsSplitEffect` (no pixel-absolute props) and `TransformEffect` (pixel translation lives in `Transform`, out of scope). Corresponding `tasks.md` rows `T034 PartsSplitEffect` and `T037 TransformEffect` should be removed during implementation.

### Property-editor registration (audit finding — NEW)

Beutl's editor dispatches property editors via an **exact `Type` → editor** lookup in `src/Beutl/Services/PropertyEditorService.cs` (the `s_editors` `FrozenDictionary<Type, Editor>`). Adding the three wrapper types without registering them leaves their property fields rendered as unknown / fallback. The audit therefore adds a new foundational obligation:

| Type | Existing analog used as template | Registration to add | New ViewModel file under `src/Beutl/ViewModels/Editors/` |
|---|---|---|---|
| `PixelLength` | `typeof(float) → NumberEditor<float>` (line 121) | `new(typeof(PixelLength), new(_ => new NumberEditor<float>(), s => new PixelLengthEditorViewModel(s.ToTyped<PixelLength>())))` | `PixelLengthEditorViewModel.cs` (model: unwrap `ReferencePixels`, edit, re-wrap; show "px @ ref" unit suffix in the label if practical) |
| `PixelExtent` | `typeof(Size) → Vector2Editor<float> + SizeEditorViewModel` (line 144) | `new(typeof(PixelExtent), new(_ => new Vector2Editor<float>(), s => new PixelExtentEditorViewModel(s.ToTyped<PixelExtent>())))` | `PixelExtentEditorViewModel.cs` (Width/Height edits like `SizeEditorViewModel`) |
| `PixelOffset` | `typeof(Point) → Vector2Editor<float> + PointEditorViewModel` (line 143) | `new(typeof(PixelOffset), new(_ => new Vector2Editor<float>(), s => new PixelOffsetEditorViewModel(s.ToTyped<PixelOffset>())))` | `PixelOffsetEditorViewModel.cs` (X/Y edits like `PointEditorViewModel`) |

This work belongs to **Phase 2 (Foundational)** in `tasks.md` — it must land **before** any per-effect migration so a half-migrated build does not break property-editor UI for the migrated effects. The cost is small (3 entries + 3 short ViewModel classes that delegate to the underlying primitive editor's logic).

### Deferred follow-ups discovered by the audit

- **`Beutl.Graphics.Transformation.*` (e.g. `TranslateTransform`, `RotateTransform`, `ScaleTransform`)**: same `PixelLength` / `PixelOffset` treatment is appropriate but they ship with `Beutl.Graphics`-level Drawables, not just FilterEffects. Bundling here would balloon scope — track as a follow-up feature alongside `Pen.Thickness`.
- **`Pen.Thickness`**: already deferred per research R6.

If a later code change introduces a new pixel-absolute parameter, it MUST be added to this table and given a test.

## Serialization

`PixelLength` / `PixelExtent` / `PixelOffset` serialize as the same JSON numeric shape as the primitives they replace:

- `PixelLength { ReferencePixels = 4.0 }` → `4.0` (single number).
- `PixelExtent { Width = 10, Height = 5 }` → `{ "width": 10, "height": 5 }` (same shape as `Size`).
- `PixelOffset { X = 3, Y = 4 }` → `{ "x": 3, "y": 4 }` (same shape as `Point`).

This is the key to FR-003 (no migration step): a project file that previously had `"sigma": { "width": 20, "height": 20 }` deserializes into either `Size` (legacy plugin code) or `PixelExtent` (migrated built-in) **with the same numeric value**, and renders the same at the scene's export resolution because `RenderScale.Identity` makes `Resolve` a no-op.

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
| `PixelLength.ReferencePixels >= 0` for parameters where negative is nonsensical (sigma, radius, length) | per-property validator passed to `Property.CreateAnimatable<PixelLength>(..., validator)` |
| Wrapper construction rejects `NaN` on every numeric field | `PixelLength` / `PixelExtent` / `PixelOffset` constructors |
| Resolved value clamps to rasterizer minimum (FR-009) | per-effect `ApplyTo` after `Resolve(scale)` — using `Math.Max(resolved, MinSupported)` where `MinSupported = 0` for zero-preservation or 1 for "must-not-be-invisible" |
| Zero stays zero (FR-009) | `Resolve` returns `0` when input is `0` (multiplication is exact for finite scales) |

## State transitions

None. The wrappers are immutable value types; the scale plumbing is a simple stack. No state machine.
