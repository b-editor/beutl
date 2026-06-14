using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Particles;

internal sealed class ParticleRenderNode(ParticleEmitter.Resource particle) : RenderNode
{
    private (RenderTarget RT, Drawable.Resource? Resource, int? Version)? _cachedRenderTarget;
    private Rect _drawableBounds;
    // feature 003 (FR-029): the working density the cached particle-drawable buffer was rasterized at.
    private float _renderScale = 1f;

    public (ParticleEmitter.Resource Resource, int Version)? Particle { get; private set; } = particle.Capture();

    public bool Update(ParticleEmitter.Resource resource)
    {
        if (!resource.Compare(Particle))
        {
            Particle = resource.Capture();
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        if (!Particle.HasValue) return [];
        var resource = Particle.Value.Resource;
        var particles = resource.GetAliveParticles();
        if (particles.Length == 0) return [];

        // feature 003 (FR-029): honor the active render scale — rasterize the per-particle drawable into a
        // ceil(bounds × w) buffer (not a fixed 1x one) so it stays crisp under supersampled export and does not
        // over-allocate under reduced-scale preview. Particles have no concrete bitmap input (each per-particle
        // drawable re-rasterizes), so the supply-driven working density is just the output scale s_out.
        float w = context.OutputScale;
        if (!_cachedRenderTarget.HasValue ||
            _renderScale != w ||
            !ReferenceEquals(_cachedRenderTarget.Value.Resource, resource.ParticleDrawable) ||
            _cachedRenderTarget.Value.Version != resource.ParticleDrawable?.Version)
        {
            _cachedRenderTarget?.RT.Dispose();
            _cachedRenderTarget = null;
            _renderScale = w;

            if (resource.ParticleDrawable is { } tracked)
            {
                _cachedRenderTarget = RenderDrawableToTarget(tracked, w, context.MaxWorkingScale, out _drawableBounds);
            }
            else
            {
                _cachedRenderTarget = RenderFallbackEllipse(w, context.MaxWorkingScale, out _drawableBounds);
            }
        }

        if (_cachedRenderTarget == null)
        {
            return [];
        }

        // Compute total bounds from all alive particles
        Rect totalBounds = default;
        var particlesSpan = particles.Span;
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle p = ref particlesSpan[i];
            if (!p.IsAlive) continue;

            float scale = p.CurrentSize / 10f;
            if (scale <= 0) continue;

            // Use a conservative square bounding box that safely encloses any rotation
            float maxDim = MathF.Max((float)_drawableBounds.Width, (float)_drawableBounds.Height) * scale;
            var particleBounds = new Rect(
                p.X - maxDim / 2f,
                p.Y - maxDim / 2f,
                maxDim,
                maxDim);

            totalBounds = totalBounds.Union(particleBounds);
        }

        // Capture references for the lambda
        RenderTarget cachedRT = _cachedRenderTarget.Value.RT;
        Rect drawableBounds = _drawableBounds;

        return
        [
            RenderNodeOperation.CreateLambda(
                totalBounds,
                canvas => DrawAllParticles(canvas, cachedRT, particles, drawableBounds, w),
                // The composite is bitmap content at the density the cached drawable was rasterized at (FR-019b).
                effectiveScale: EffectiveScale.At(w))
        ];
    }

    private static void DrawAllParticles(
        ImmediateCanvas canvas,
        RenderTarget cachedRT,
        ReadOnlyMemory<Particle> particles,
        Rect drawableBounds,
        float w)
    {
        // At w != 1 every particle blits the SAME cached buffer scaled; snapshot it ONCE here and reuse the
        // SKImage across the loop (up to MaxParticles) rather than re-snapshotting + force-flushing per particle.
        // The cached buffer is immutable during this draw, so one snapshot is byte-equivalent. w == 1 keeps the
        // bare point-blit fast path (no snapshot).
        SKImage? cachedImage = null;
        if (w != 1f)
        {
            cachedRT.VerifyAccess();
            cachedImage = cachedRT.Value.Snapshot();
        }

        try
        {
            var particlesSpan = particles.Span;
            for (int i = 0; i < particles.Length; i++)
            {
                ref readonly Particle p = ref particlesSpan[i];
                if (!p.IsAlive) continue;

                float scale = p.CurrentSize / 10f;
                float opacity = p.CurrentOpacity / 100f;
                if (opacity <= 0 || scale <= 0) continue;

                float rotRad = p.Rotation * MathF.PI / 180f;
                Matrix transform = Matrix.CreateScale(scale, scale)
                                   * Matrix.CreateRotation(rotRad)
                                   * Matrix.CreateTranslation(p.X, p.Y);

                using (canvas.PushTransform(transform))
                using (canvas.PushOpacity(opacity))
                {
                    Color color = p.CurrentColor;
                    if (color != Colors.White)
                    {
                        using var colorFilter = SKColorFilter.CreateBlendMode(
                            new SKColor(color.R, color.G, color.B, color.A),
                            SKBlendMode.Modulate);
                        using var paint = new SKPaint();
                        paint.ColorFilter = colorFilter;

                        using (canvas.PushPaint(paint))
                        {
                            DrawCached(canvas, cachedRT, cachedImage, drawableBounds, w);
                        }
                    }
                    else
                    {
                        DrawCached(canvas, cachedRT, cachedImage, drawableBounds, w);
                    }
                }
            }
        }
        finally
        {
            cachedImage?.Dispose();
        }
    }

    // feature 003: blit the cached particle-drawable buffer. The buffer is ceil(footprint × w) device px; at
    // w == 1 keep the bare point blit (byte-identical), at w != 1 draw the once-snapshotted image into its
    // LOGICAL footprint so the per-particle + ambient CTM resample it once (crisp under SSAA export).
    private static void DrawCached(
        ImmediateCanvas canvas, RenderTarget cachedRT, SKImage? cachedImage, Rect drawableBounds, float w)
    {
        var offset = new Point(-drawableBounds.Width / 2, -drawableBounds.Height / 2);
        if (w == 1f)
        {
            canvas.DrawRenderTarget(cachedRT, offset);
        }
        else
        {
            canvas.DrawImageScaled(cachedImage!,
                new Rect(offset.X, offset.Y, drawableBounds.Width, drawableBounds.Height));
        }
    }

    private static (RenderTarget, Drawable.Resource, int)? RenderDrawableToTarget(
        Drawable.Resource drawable,
        float w,
        float maxWorkingScale,
        out Rect bounds)
    {
        using var node = new DrawableRenderNode(drawable);
        // 1920×1080 is only the LOGICAL measurement canvas (GraphicsContext2D.Size stays logical); the actual
        // buffer is sized from the drawable bounds below. Thread w as the output scale so the drawable and its
        // sub-pulls rasterize at the active render density (FR-029).
        using (var gctx = new GraphicsContext2D(node, new Size(1920, 1080), w))
        {
            drawable.GetOriginal().Render(gctx, drawable);
        }

        // Forward the request's FR-037 ceiling — without it the particle drawable's subtree falls back
        // to +∞ and a high-density source inside it escapes the preview/export cap.
        var processor = new RenderNodeProcessor(node, false, w, maxWorkingScale);
        var ops = processor.PullToRoot();

        bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
        // Size the buffer ceil(bounds × w) device px; w == 1 keeps the exact pre-feature 1x size (byte-identical).
        var rect = w == 1f ? PixelRect.FromRect(bounds) : PixelRect.FromRect(bounds, w);

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            foreach (var op in ops)
                op.Dispose();
            return null;
        }

        var renderTarget = RenderTarget.Create(rect.Width, rect.Height);
        if (renderTarget == null)
        {
            foreach (var op in ops)
                op.Dispose();
            return null;
        }

        using (var canvas = new ImmediateCanvas(renderTarget, w, maxWorkingScale, logicalSize: bounds.Size))
        {
            canvas.Clear();
            // feature 003: the canvas bakes the base CTM CreateScale(w) (identity at w == 1) so logical
            // content fills the denser buffer; only the logical translation to the bounds origin is needed.
            using (canvas.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                foreach (var op in ops)
                {
                    op.Render(canvas);
                    op.Dispose();
                }
            }
        }

        return (renderTarget, drawable, drawable.Version);
    }

    private static (RenderTarget, Drawable.Resource?, int?)? RenderFallbackEllipse(
        float w, float maxWorkingScale, out Rect bounds)
    {
        bounds = new Rect(-5, -5, 10, 10);

        // ceil(10 × w) device px so the placeholder stays crisp under SSAA; w == 1 keeps the exact 10×10 buffer.
        int dim = w == 1f ? 10 : (int)MathF.Ceiling(10 * w);
        var renderTarget = RenderTarget.Create(dim, dim);
        if (renderTarget == null) return null;

        // Forward the request's FR-037 ceiling for consistency with RenderDrawableToTarget — inert today (the
        // single SolidColorBrush DrawEllipse never consults MaxWorkingScale) but a latent trap if the fallback
        // ever gains nested / tiled content.
        using (var canvas = new ImmediateCanvas(renderTarget, w, maxWorkingScale, logicalSize: bounds.Size))
        {
            canvas.Clear();
            // feature 003: the canvas bakes the base CTM CreateScale(w); only the logical translation of the
            // (-5,-5)-origin placeholder bounds to (0,0) is needed.
            using (canvas.PushTransform(Matrix.CreateTranslation(5, 5)))
            {
                canvas.DrawEllipse(bounds, Brushes.Resource.White, null);
            }
        }

        return (renderTarget, null, null);
    }

    protected override void OnDispose(bool disposing)
    {
        _cachedRenderTarget?.RT.Dispose();
        _cachedRenderTarget = null;
        Particle = null;
    }
}
