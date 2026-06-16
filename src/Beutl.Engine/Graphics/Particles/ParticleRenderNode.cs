using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Particles;

internal sealed class ParticleRenderNode(ParticleEmitter.Resource particle) : RenderNode
{
    private (RenderTarget RT, Drawable.Resource? Resource, int? Version)? _cachedRenderTarget;
    private Rect _drawableBounds;
    // Working density the cached particle-drawable buffer was rasterized at.
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
        // Snapshot once and reuse across the loop (w == 1 uses point-blit instead).
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

    // Blit the cached particle buffer: point-blit at w == 1, scaled image at w != 1.
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
        float nominalScale,
        float maxWorkingScale,
        out Rect bounds)
    {
        using var node = new DrawableRenderNode(drawable);
        // 1920x1080 is only the logical measurement canvas; actual buffer is sized from drawable bounds.
        using (var gctx = new GraphicsContext2D(node, new Size(1920, 1080), nominalScale))
        {
            drawable.GetOriginal().Render(gctx, drawable);
        }

        var processor = new RenderNodeProcessor(node, false, nominalScale, maxWorkingScale);
        var ops = processor.PullToRoot();

        bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
        // Clamp density so oversized buffers degrade instead of failing to allocate.
        float w = nominalScale > 1f
            ? RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, nominalScale)
            : nominalScale;
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

        int dim = w == 1f ? 10 : (int)MathF.Ceiling(10 * w);
        var renderTarget = RenderTarget.Create(dim, dim);
        if (renderTarget == null) return null;

        using (var canvas = new ImmediateCanvas(renderTarget, w, maxWorkingScale, logicalSize: bounds.Size))
        {
            canvas.Clear();
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
