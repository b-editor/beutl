using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Particles;

internal sealed class ParticleRenderNode(ParticleEmitter.Resource particle) : RenderNode
{
    private (RenderTarget RT, Drawable.Resource? Resource, int? Version)? _cachedRenderTarget;
    private Rect _drawableBounds;

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

        if (!_cachedRenderTarget.HasValue ||
            !ReferenceEquals(_cachedRenderTarget.Value.Resource, resource.ParticleDrawable) ||
            _cachedRenderTarget.Value.Version != resource.ParticleDrawable?.Version)
        {
            _cachedRenderTarget?.RT.Dispose();
            _cachedRenderTarget = null;

            if (resource.ParticleDrawable is { } tracked)
            {
                _cachedRenderTarget = RenderDrawableToTarget(tracked, out _drawableBounds);
            }
            else
            {
                _cachedRenderTarget = RenderFallbackEllipse(out _drawableBounds);
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

        return
        [
            RenderNodeOperation.CreateLambda(totalBounds,
                canvas => DrawAllParticles(canvas, cachedRT, particles, _drawableBounds))
        ];
    }

    private static void DrawAllParticles(
        ImmediateCanvas canvas,
        RenderTarget cachedRT,
        ReadOnlyMemory<Particle> particles,
        Rect drawableBounds)
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
                    using var paint = new SKPaint();
                    paint.ColorFilter = SKColorFilter.CreateBlendMode(
                        new SKColor(color.R, color.G, color.B, color.A),
                        SKBlendMode.Modulate);

                    using (canvas.PushPaint(paint))
                    {
                        canvas.DrawRenderTarget(cachedRT, new(-drawableBounds.Width / 2, -drawableBounds.Height / 2));
                    }
                }
                else
                {
                    canvas.DrawRenderTarget(cachedRT, new(-drawableBounds.Width / 2, -drawableBounds.Height / 2));
                }
            }
        }
    }

    private static (RenderTarget, Drawable.Resource, int)? RenderDrawableToTarget(
        Drawable.Resource drawable,
        out Rect bounds)
    {
        using var node = new DrawableRenderNode(drawable);
        using (var gctx = new GraphicsContext2D(node, new PixelSize(1920, 1080)))
        {
            drawable.GetOriginal().Render(gctx, drawable);
        }

        var processor = new RenderNodeProcessor(node, false);
        var ops = processor.PullToRoot();

        bounds = ops.Aggregate(Rect.Empty, (a, n) => a.Union(n.Bounds));
        var rect = PixelRect.FromRect(bounds);

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

        using (var canvas = new ImmediateCanvas(renderTarget))
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

    private static (RenderTarget, Drawable.Resource?, int?)? RenderFallbackEllipse(out Rect bounds)
    {
        bounds = new Rect(-5, -5, 10, 10);

        var renderTarget = RenderTarget.Create(10, 10);
        if (renderTarget == null) return null;

        using (var canvas = new ImmediateCanvas(renderTarget))
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
