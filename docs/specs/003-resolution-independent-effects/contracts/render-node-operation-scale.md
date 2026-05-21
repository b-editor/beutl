# Contract: `RenderNodeOperation.CorrectionScale`

**Surface**: `Beutl.Graphics.Rendering.RenderNodeOperation` (existing abstract class) — adds a virtual `CorrectionScale` property; `Beutl.Graphics.Rendering.RenderScale` (new value type, `(ScaleX, ScaleY)`).

**Audience**: anyone authoring or maintaining a `RenderNode` subclass (which means the engine team and a small number of advanced plugin authors who extend the rendering pipeline at a low level). Authors of `Drawable` / `FilterEffect` / `Shape` (the much larger group) do **not** need to read this — see `plugin-author-guide.md`.

## The contract in one paragraph

A `RenderNodeOperation` carries (in addition to its existing `Bounds` and `Render` / `HitTest` methods) a `CorrectionScale: RenderScale` value. `Bounds` is in the parent's authoring coordinate space. The operation's raster (what `Render(canvas)` draws when called by a compositor that allocates a target raster from `Bounds`) is at `Bounds.Size / CorrectionScale` — i.e. `CorrectionScale > Identity` means "I produced a smaller raster than my bounds suggest; you must upscale by `CorrectionScale` when blitting". The default is `Identity = (1, 1)`, meaning "raster matches bounds 1:1" — pre-feature behavior.

## Surface

```csharp
namespace Beutl.Graphics.Rendering
{
    public readonly struct RenderScale : IEquatable<RenderScale>
    {
        public RenderScale(float scaleX, float scaleY);
        public float ScaleX { get; }
        public float ScaleY { get; }

        public static RenderScale Identity { get; }              // (1, 1)
        public static RenderScale FromRatio(float ratio);        // uniform (ratio, ratio)
        public static RenderScale FromFrames(PixelSize raster, PixelSize bounds);
            // raster.Width / bounds.Width per axis. Validates raster ≤ bounds.

        public float ApplyX(float lengthAuthoringSpace);         // / ScaleX  — convert authoring → raster
        public float ApplyY(float lengthAuthoringSpace);
        public float ApplyUniform(float lengthAuthoringSpace);   // / geometric-mean for scalar dimensions
        public Size  Apply(Size sizeAuthoringSpace);
        public Point Apply(Point pointAuthoringSpace);
        // Note the API is "Apply" = divide. Authoring-space → raster-space. The compositor
        // does the inverse multiply when upscaling for the final blit.
    }

    public abstract partial class RenderNodeOperation : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public abstract Rect Bounds { get; }

        // NEW:
        public virtual RenderScale CorrectionScale => RenderScale.Identity;

        public abstract void Render(ImmediateCanvas canvas);
        public abstract bool HitTest(Point point);
        // … existing members …
    }
}
```

The choice of `Apply = divide (authoring → raster)` direction matches how filter parameters consume it (`sigma / CorrectionScale.ScaleX` to produce the value Skia receives). The inverse multiply for the compositor's upscale blit is just `1 / CorrectionScale` or equivalently swapping authoring↔raster in `FromFrames`.

## Default behavior — Identity

A `RenderNodeOperation` that does not override `CorrectionScale` reports `Identity`. This means **every existing operation in the codebase is correct by default** — the new field is invisible until a source node opts into proxy by overriding it.

## Source vs transformer (the propagation contract)

Each RenderNode-derived class is classified — either **source-producing** or **transformer**. The classification dictates what the class does with `CorrectionScale`.

### Source-producing nodes

A node is **source-producing** if its operation's raster originates inside the operation (a video decode, an image bitmap, a sub-canvas render pass, a vector draw into a freshly-allocated SKSurface).

Source-producing nodes declare the scale of the raster they produced:

```csharp
public override RenderScale CorrectionScale =>
    new RenderScale(boundsWidth / rasterWidth, boundsHeight / rasterHeight);
```

If the source did no proxy (raster matches bounds), `CorrectionScale = Identity`.

If the source applied proxy (e.g. video at 1/4 resolution), `CorrectionScale = (4, 4)`.

### Transformer nodes

A node is **transformer** if its operation processes the raster of one or more upstream operations (a filter that applies an SKImageFilter, a transform that wraps a draw, a clip that intersects, a layer / opacity-mask that combines, a container that aggregates).

Transformer nodes:

1. Read the upstream operation's `CorrectionScale`.
2. Adjust their own internal parameters before invoking Skia: any length-typed parameter is divided by the upstream scale (`p_raster = scale.Apply(p_authoring)`).
3. Compute their own output `Bounds` in **authoring space** using **un-divided (authored) parameters** — so the compositor places the result correctly.
4. Propagate `CorrectionScale` on their output operation. Typically this is the same value as the upstream operation (the transformer did not re-rasterize at a different resolution). If the transformer **does** re-rasterize at a different resolution (e.g. a saveLayer with an explicit downscale), it computes its own `CorrectionScale`.

### Sub-canvas-rendering nodes (sources that draw into a raster)

A node that draws into a freshly-allocated raster (e.g. `DrawableRenderNode` for a Drawable, `SceneDrawable` for a nested Scene) is a hybrid: it acts as a source from its parent's perspective (declaring `CorrectionScale`), but internally constructs an `ImmediateCanvas` and runs a render pass. Within that internal render pass, the `ImmediateCanvas`'s `SKCanvas` is set up with `SKCanvas.Scale(1 / CorrectionScale)` so that everything drawn inside is in authoring space; Skia handles the per-API scaling automatically.

This is described in detail in `source-node-proxy.md`.

## Composition rules (the propagation invariants)

For a graph `Source A → Filter B → Filter C → Compositor`:

- A reports `CorrectionScale = sA`, raster at sizeA, bounds at sizeA × sA.
- B receives A's operation. B is a Blur with authored sigma `σ`. B applies `Skia.Blur(σ / sA)` to A's raster (still at sizeA). B's output operation has `Bounds` = `A.Bounds` ± authored sigma extent (in authoring space) and `CorrectionScale = sA` (unchanged).
- C receives B's operation. Same pattern, divides its parameter by `sA`. `CorrectionScale = sA`.
- Compositor receives C's operation. Upscales by `sA` when blitting onto the export canvas at C's `Bounds`.

For mixed-resolution composition (multiple sources):

- Source A reports `CorrectionScale = sA`.
- Source B reports `CorrectionScale = sB`.
- The compositor receives both operations independently. Each is blit with its own upscale.

## Special cases

- **`HitTest`**: operates on `Bounds` (authoring coordinates) and the operation's content. Hit testing is not affected by `CorrectionScale` — bounds and hit hits stay in authoring space.
- **`Dispose`**: unaffected; `CorrectionScale` is a value, not a resource.
- **Zero-extent operations** (`Bounds` empty): `CorrectionScale` is meaningless; convention is to report `Identity`.
- **Non-uniform `CorrectionScale`** (`ScaleX != ScaleY`): allowed by the type but should be rare. Use cases: a video source with non-square pixels, asymmetric downsampling. Transformer math handles per-axis scaling correctly.

## Backward compatibility

- Today, no `RenderNode` subclass overrides `CorrectionScale`. All operations report `Identity`. All transformer divisions and compositor upscales are no-ops. Output is SSIM-equivalent to pre-feature (FR-005 / SC-002), modulo new NaN/negative-length guards.
- The first per-clip-proxy feature (UX, persistence, automatic proxy generation) lives in a follow-up feature. That follow-up wires source nodes to read their per-clip proxy settings and produce non-Identity `CorrectionScale`.

## Versioning

`RenderScale` and `RenderNodeOperation.CorrectionScale` are new public API on `Beutl.Engine`. Adding them is a backward-compatible source change. Removing or renaming them later requires `refactor!:` / `feat!:` and a `BREAKING CHANGE:` footer per `AGENTS.md`.
