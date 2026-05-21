# Contract: RenderScale and Reference-Frame Propagation

> **Design pivot**: This document still describes the `RenderScale` plumbing (which is unchanged). What changed is **who applies the scale**: instead of wrapper types' `Resolve(scale)` methods at call sites, every length-taking helper on `FilterEffectContext` (`Blur(Size)`, etc.) applies the multiplication internally. See `effect-helper-scaling.md` for the helper-side contract and `research.md` § R2 for why the wrapper approach was abandoned.

**Surface**: `Beutl.Graphics.Rendering.RenderScale`, `IRenderer.ReferenceFrame`, `GraphicsContext2D.PushReferenceFrame`, `FilterEffectContext.RenderScale` / `.ReferenceFrame`.

**Audience**: anyone implementing a custom `Renderer`, a custom container that materializes its own raster, or an effect that needs to know the scale.

## Invariants

1. **Every rendering context has a `ReferenceFrame` and a `RenderScale`.** No "unknown" state.
2. **`RenderScale = currentRaster / ReferenceFrame`** — by construction (`RenderScale.FromFrames(renderTarget, referenceFrame)`).
3. **Default `ReferenceFrame` equals the renderer's `FrameSize`**, so `RenderScale = Identity` when nothing opts in. This is the legacy behavior.
4. **Nested compositions push a new `ReferenceFrame`** equal to the nested scene's configured `FrameSize`. When the nested composition pops, the previous value is restored exactly.
5. **`FilterEffectContext` is immutable in its scale info**: it snapshots `(ReferenceFrame, RenderScale)` at construction and does not observe later pushes/pops on the outer `GraphicsContext2D`.

## Propagation order

```
Renderer (FrameSize, ReferenceFrame)
        │
        └─► GraphicsContext2D (initial = renderer's pair)
                │
                ├─ PushReferenceFrame(subSceneFrame)   ┐
                │  …draw nested scene…                  │  scoped via using
                │  Dispose()                            ┘
                │
                └─► FilterEffectContext snapshotted
                        on PushNode(...)
```

- `SceneDrawable.Render(ctx, r)` MUST wrap its draw in `using (ctx.PushReferenceFrame(r.ReferencedScene.FrameSize)) { ... }`.
- `LayerEffect`, and any future container that materializes a sub-raster (e.g. an "isolation" effect), MUST do the same.
- `FilterEffectContext.Activate` / `Apply` read the snapshotted values; an effect's `ApplyTo` MUST NOT mutate them.

## Renderer construction

Two constructors:

```csharp
public Renderer(int width, int height);
// ⇒ FrameSize = ReferenceFrame = (width, height), RenderScale.Identity

public Renderer(int width, int height, PixelSize referenceFrame);
// ⇒ FrameSize = (width, height), ReferenceFrame as given,
//   RenderScale = (width / refX, height / refY)
```

`SceneRenderer` keeps its current constructor (no proxy support yet); a future proxy-preview feature adds:

```csharp
public SceneRenderer(Scene scene, PixelSize proxySize, bool disableResourceShare = false)
    : base(proxySize.Width, proxySize.Height, scene.FrameSize) { ... }
```

…with `proxySize` constrained to be a **uniform scale** of `scene.FrameSize` (per spec Edge Cases). Enforcement lives in `RenderScale.FromFrames`, which throws `ArgumentException` if the per-axis ratios differ beyond `1e-3` of the larger. Higher-level proxy-preview UI (when it ships) is expected to **snap** the requested proxy size to the nearest uniform-scaled `PixelSize` *before* constructing the renderer, so the throw is a defensive guard for callers — not the user-facing experience.

## Custom container effects

A container effect (`LayerEffect`-like) that allocates its own raster and re-enters the rendering loop is responsible for:

1. Choosing the appropriate `ReferenceFrame` for the inner work (typically: the container's own logical size).
2. Calling `ctx.PushReferenceFrame(thatFrame)` before invoking the inner pipeline.
3. Disposing the `PushedState` (a `using` is sufficient).

If a container forgets step 2, inner effects will use the outer reference frame — measurably wrong for nested-scene cases. There is no automatic detection. Containers are reviewed by the `beutl-design-reviewer` subagent.

## Sub-pixel and zero handling

After the internal `arg * RenderScale` multiplication inside a scaled helper, the rasterizer-facing code MUST guard against:

- **Zero values**: pass through exactly (do not clamp to a minimum) — see FR-009. A `0` input stays `0`.
- **Sub-pixel positive values** (`0 < scaled < 1`): allowed to pass through. The rasterizer (Skia for most effects) clamps as appropriate. Helpers that wrap an operation where a sub-pixel value would produce visually invisible output that a user probably did not intend (e.g. stroke width < 1 px) MAY clamp using `Math.Max(scaled, 1.0f)` — this is per-helper policy and is called out in that helper's test.

## NaN / infinity

`RenderScale` constructor rejects non-positive and non-finite values, so the multiplication inside a scaled helper cannot produce `NaN` or `±∞` from finite arguments. Each scaled helper additionally guards its incoming argument against `NaN` (`ArgumentException`) and against negative lengths where negative is nonsensical (sigma, radius — `ArgumentOutOfRangeException`).

## Versioning

`RenderScale`, `IRenderer.ReferenceFrame`, `GraphicsContext2D.PushReferenceFrame`, the `FilterEffectContext` snapshotted properties, and the `*Raw` helper variants are all new public API on `Beutl.Engine`. Adding them is a backward-compatible source change (`IRenderer.ReferenceFrame` ships with a default-interface implementation returning `FrameSize`, so existing `IRenderer` implementors don't break; the `*Raw` helpers are pure additions).

The **semantic change** to existing scaled helpers (`Blur(Size)` etc.) is not a signature change but is a behavior change. It only becomes observable when `RenderScale ≠ Identity`, which today never happens. When a future proxy-preview UX ships, that PR carries the behavior change in its changelog. Plugins that intentionally relied on raw-raster semantics opt out via the `*Raw` twin (one method-name change per call site).

Removing or renaming any of these later is a breaking change requiring `refactor!:` / `feat!:` and a `BREAKING CHANGE:` footer per `AGENTS.md`.
