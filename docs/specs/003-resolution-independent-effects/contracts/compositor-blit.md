# Contract: Compositor Blit with CorrectionScale Upscale

**Surface**: `Renderer.Render(...)` (and / or whatever final pass consumes the operations and writes to the output raster); `ImmediateCanvas.DrawRenderTarget` / `DrawSurface` extension to accept `CorrectionScale`.

**Audience**: engine maintainers of the top-level render path. Plugin and extension authors do not touch this.

## The contract in one paragraph

The compositor (final render pass) walks the produced `RenderNodeOperation`s and, for each, blits the operation's raster into the output canvas at the operation's `Bounds`, applying an upscale transform equal to the operation's `CorrectionScale`. When `CorrectionScale = Identity` (= `(1, 1)`), the blit is the existing pre-feature path (no transform). When `CorrectionScale > Identity` (≥ 1 per axis), the raster is upscaled per axis to fill the bounds via `SKCanvas.Scale(CorrectionScale.ScaleX, CorrectionScale.ScaleY, …)`. The numeric convention is `CorrectionScale = bounds.Size / raster.Size`, so multiplying by it at blit time is mathematically the inverse of the smaller-raster downscale that the source applied.

## Pattern

```csharp
// Conceptual — the actual code lives at the top of Renderer.Render or in ImmediateCanvas.
void Compose(RenderNodeOperation op, ImmediateCanvas finalCanvas)
{
    if (op.CorrectionScale == RenderScale.Identity)
    {
        op.Render(finalCanvas);
        return;
    }

    // The operation's Render produces a raster whose intended display bounds are op.Bounds.
    // The raster is at op.Bounds.Size / op.CorrectionScale.
    // We push a transform that fits the raster into the bounds.
    using (finalCanvas.PushTransform(SKMatrix.CreateScale(
        op.CorrectionScale.ScaleX, op.CorrectionScale.ScaleY,
        op.Bounds.TopLeft.X, op.Bounds.TopLeft.Y)))
    {
        op.Render(finalCanvas);
    }
}
```

The implementation detail of how `op.Render(finalCanvas)` actually emits the raster into the canvas depends on the operation type:

- For operations created by `CreateFromRenderTarget` / `CreateFromSurface` — they call `canvas.DrawRenderTarget(rt, position)` / `canvas.DrawSurface(surface, position)`. The matrix push above pre-scales these calls.
- For operations created by `CreateLambda` whose render delegate manipulates the canvas directly — the matrix push composes with whatever the delegate does.
- For `CreateDecorator` — wraps a child, same composition rules.

## Why scale at blit time, not earlier

The operation's `Bounds` are in **parent authoring space**. The operation's raster is at **its own resolution**. Bridging the two requires a matrix transform; doing it at the compositor's blit is the natural place because that's where the `op.Bounds.TopLeft.X / Y` position is also applied.

Alternative considered: have each operation pre-scale itself before producing the raster. **Rejected** because:

- It requires every source to know about the parent's coordinate system, which breaks the source-as-self-contained model.
- It makes `op.Bounds` and `op.Render` semantically inconsistent (bounds in authoring, render in raster, no clear bridge).
- The blit-time scale is one place that knows both `op.Bounds` and `op.CorrectionScale`; consolidating the logic there is simpler.

## Quality

Compositor blit with `SKCanvas.Scale` matrix transform uses Skia's bilinear / bicubic filtering at the GPU (or CPU) level. For typical 2× / 4× / 8× upscales this is high-quality. The actual blit quality is identical to what `SKCanvas.DrawSurface(surface, dst: scaledRect)` produces today — Skia handles the resampling natively.

For the test suite, the SSIM tolerance (`SSIM ≥ 0.97`) accounts for the bilinear blit's slight smoothing compared to a hypothetical "render directly at full size" reference. In practice, the difference is invisible to the user at typical viewing distances; that's exactly what proxy preview is for.

## Backward compatibility

When `CorrectionScale = Identity`, the `if` branch above skips the transform push entirely — the existing `op.Render(finalCanvas)` path runs verbatim. Pre-feature projects (which never set non-Identity CorrectionScale) are byte-equivalent to today.

## Tests required

`tests/Beutl.UnitTests/Engine/Graphics/Rendering/CompositorBlitTests.cs`:

- Identity path: an operation with `CorrectionScale = Identity` is rendered with the existing code path (no transform pushed).
- Upscale path: an operation with `CorrectionScale = (4, 4)` produces output of the expected sizes via bilinear blit.
- Mixed compositor input: two operations with different CorrectionScale values blit independently and correctly.
- SSIM equivalence end-to-end: a scene composited with one source at proxy 1/4 produces SSIM ≥ 0.97 vs the same scene at full resolution.
