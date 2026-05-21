# Contract: Source RenderNode Proxy Behaviour

**Surface**: `VideoSourceRenderNode`, `ImageSourceRenderNode`, `DrawableRenderNode` (when rendering a sub-canvas like a nested Scene), and any future "source" node subclass.

**Audience**: authors of `RenderNode` subclasses that produce a raster from a non-render-graph origin (decoding a media file, drawing a nested scene, materializing into a saveLayer at a chosen resolution). Authors of `Drawable` / `FilterEffect` / `Shape` (the much larger group) do NOT touch this file.

## The contract in one paragraph

A source-producing RenderNode is responsible for **two things** at `Process` time: (1) producing a `RenderNodeOperation` whose `CorrectionScale` correctly describes the operation's raster scale relative to its `Bounds`, and (2) (for sub-canvas-rendering sources) setting up the inner `ImmediateCanvas` so that its `SKCanvas.Matrix` includes a `Scale(1 / CorrectionScale)` transformation, allowing the inner render pass to draw in authoring space while Skia transforms to raster space automatically.

## Source types and their responsibilities

### Type A — Media-decoding sources (`VideoSourceRenderNode`, `ImageSourceRenderNode`)

These nodes get their raster from a media decoder. The decoded raster's resolution may or may not match the bounds:

- A 4K video clip placed in a 1080p scene at full size: bounds = `1920×1080`; decoder default output = `3840×2160`; if proxy mode is on, decoder is configured to output at `960×540` instead.
- An image placed at half-size: bounds = `960×540`; bitmap = `1920×1080`.

Pattern:

```csharp
public override RenderNodeOperation[] Process(RenderNodeContext context)
{
    var decodedRaster = decoder.Decode(currentFrame, /* size hint */ proxyConfig.TargetRaster);
    var bounds = ComputeBoundsInParentAuthoringSpace();
    var correctionScale = RenderScale.FromFrames(decodedRaster.PixelSize, bounds.PixelSize);

    return new[]
    {
        RenderNodeOperation.CreateFromRenderTarget(
            bounds: bounds,
            position: bounds.TopLeft,
            renderTarget: decodedRaster,
            correctionScale: correctionScale)   // <-- new arg on the factory
    };
}
```

A new factory overload accepts `CorrectionScale`. The `LambdaRenderNodeOperation` internal type gains a stored `CorrectionScale` and overrides the `CorrectionScale` virtual.

### Type B — Sub-canvas-rendering sources (`DrawableRenderNode`, `SceneDrawable`-produced node)

These nodes allocate an `SKSurface` / `RenderTarget` and run an internal `ImmediateCanvas` render pass to fill it. The choice of raster size is theirs; the internal pass draws in authoring space.

Pattern:

```csharp
public override RenderNodeOperation[] Process(RenderNodeContext context)
{
    var bounds = ComputeBoundsInAuthoringSpace();
    var (rasterSize, correctionScale) = ResolveProxy(bounds, proxyConfig);
        // For no proxy: rasterSize = bounds.PixelSize, correctionScale = Identity.
        // For proxy 1/4: rasterSize = bounds.PixelSize / 4, correctionScale = (4, 4).

    using var renderTarget = RenderTarget.Create(rasterSize.Width, rasterSize.Height);
    using var canvas = new ImmediateCanvas(renderTarget, rasterSize);
    using (canvas.SKCanvas.PushPreTransform(SKMatrix.CreateScale(
        1f / correctionScale.ScaleX, 1f / correctionScale.ScaleY)))
    {
        // Run the inner render pass in authoring space — Skia transforms to raster space.
        innerDrawable.Render(canvas);
    }

    return new[]
    {
        RenderNodeOperation.CreateFromRenderTarget(
            bounds: bounds,
            position: bounds.TopLeft,
            renderTarget: renderTarget,
            correctionScale: correctionScale)
    };
}
```

The key insight: by pre-multiplying the SKCanvas matrix with `Scale(1/CorrectionScale)`, every length-typed value drawn inside (Rect, Pen.Thickness, SKImageFilter.Blur sigma, font size, geometry path coords, brush internal matrices) is automatically transformed by Skia. The inner Drawable / Shape / TextBlock / Geometry / Brush code **does not change**. This is the simplification the user's design enables.

### Type C — Static or no-proxy sources

A node that always produces raster matching its bounds (a fresh vector draw at native scale, a no-proxy image source, a no-proxy nested scene) does the minimum:

```csharp
public override RenderNodeOperation[] Process(RenderNodeContext context)
{
    // … existing logic …
    // CorrectionScale defaults to Identity via the base virtual property; no override needed.
}
```

These nodes need zero source change to participate correctly.

## How a source resolves its proxy choice

The source consults its **proxy configuration**, which is a per-clip / per-source setting. The persistence schema and UX for this setting is **out of scope for this PR** (see `spec.md` § "Future work"). For now, source nodes default to no-proxy (`Identity`), and tests inject specific configurations via a test-only API.

Conceptually:

```csharp
private (PixelSize raster, RenderScale scale) ResolveProxy(Rect bounds, ProxyConfig? config)
{
    if (config?.Enabled != true)
        return (bounds.PixelSize, RenderScale.Identity);

    var ratio = config.Ratio;   // e.g. 0.25 for 1/4
    var raster = new PixelSize(
        Math.Max(1, (int)(bounds.PixelSize.Width  * ratio)),
        Math.Max(1, (int)(bounds.PixelSize.Height * ratio)));
    return (raster, RenderScale.FromFrames(raster, bounds.PixelSize));
}
```

The actual `ProxyConfig` type is named and persisted by the follow-up feature.

## Tests required

A new source-node test (`tests/Beutl.UnitTests/Engine/Graphics/Rendering/SourceNodeCorrectionScaleTests.cs`) covers:

- Type A: video / image source with mocked decoder, asserting `CorrectionScale` is reported correctly for several ratios.
- Type B: a sub-canvas drawable rendered at proxy 1/4 produces a raster of the expected reduced size and reports `CorrectionScale = 4`.
- Type C: a default source reports `Identity`.
- SSIM equivalence: a Type B drawable rendered at proxy vs at full, then compositor-upscaled, produces SSIM ≥ 0.97.
