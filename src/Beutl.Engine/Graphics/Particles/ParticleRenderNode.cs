using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Particles;

internal sealed class ParticleRenderNode(ParticleEmitter.Resource particle) : RenderNode
{
    private const long MaximumFiniteLayerBytes = 1L * 1024 * 1024 * 1024;
    private static readonly Rect s_drawableRecordingDomain = new(0, 0, 1920, 1080);
    private static readonly Rect s_fallbackBounds = new(-5, -5, 10, 10);
    private static readonly RenderResourceSlot<Particle[]> s_particlesSlot = new();

    // Colors.White computes its value in a getter and SkiaSharp's named colours are plain static fields
    // anything can assign; a keyed recording callback needs values nothing can change under it.
    private static readonly Color s_untintedParticle = Colors.White;
    private static readonly SKColor s_opaqueWhite = SKColors.White;
    private static readonly RenderResourceSlot<Brush.Resource> s_fallbackFillSlot = new();
    private static readonly RenderResourceSlot[] s_fallbackSlots = [s_fallbackFillSlot];
    private static readonly RenderResourceSlot[] s_particleSlots = [s_particlesSlot];
    private static readonly OpaqueRenderBoundsContract s_fallbackBoundsContract =
        OpaqueRenderBoundsContract.Source(s_fallbackBounds);

    public (ParticleEmitter.Resource Resource, int Version)? Particle { get; private set; } = particle.Capture();

    public bool Update(ParticleEmitter.Resource resource)
    {
        if (!resource.Compare(Particle))
        {
            Particle = resource.Capture();
            MarkChanged();
            return true;
        }

        return false;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Particle is not { } snapshot)
            return;

        ParticleEmitter.Resource resource = snapshot.Resource;
        Particle[] particles = resource.GetAliveParticles().ToArray();
        if (particles.Length == 0)
            return;

        RenderFragmentHandle? source = resource.ParticleDrawable is { } drawable
            ? RecordDrawableSource(context, drawable)
            : RecordFallbackSource(context);
        if (source is null)
            return;

        if (!source.TryGetMetadata(out RenderFragmentMetadata sourceMetadata))
        {
            throw new InvalidOperationException(
                "A particle source with symbolic metadata must be localized by an explicit finite Layer.");
        }

        Rect sourceBounds = sourceMetadata.Bounds;
        Rect totalBounds = CalculateParticleBounds(particles, sourceBounds);
        if (totalBounds.Width <= 0 || totalBounds.Height <= 0)
            return;

        bool requiresClippedLayer = context.TargetDomain is not null
                                    && RequiresClippedLayer(totalBounds, context.OutputScale);
        RenderResource<Particle[]> particlesToken = context.Borrow(particles);
        RenderFragmentHandle painter = context.TargetCommand(
            [source],
            TargetCommandDescription.Create(
                default(ParticleCommandState),
                static (session, _) => session.UseResource(
                    s_particlesSlot,
                    current => DrawParticles(session, current)),
                affectedRegion: requiresClippedLayer
                    ? TargetRegion.Full
                    : TargetRegion.Region(totalBounds),
                queryBounds: totalBounds,
                hitTest: RenderHitTestContract.None,
                resources: [s_particlesSlot.Bind(particlesToken)],
                slots: s_particleSlots));

        // A union beyond the buffer budget is mostly off-target travel. Preserve the finite layer for
        // ordinary emitters, but clip an oversized union to its owning target before allocation.
        context.Publish(requiresClippedLayer
            ? context.OwningTargetLayer([painter])
            : context.Layer([painter], totalBounds));
    }

    private static RenderFragmentHandle? RecordDrawableSource(
        RenderNodeContext context,
        Drawable.Resource drawable)
    {
        using var root = new DrawableRenderNode(drawable);
        using (var graphics = new GraphicsContext2D(
                   root,
                   s_drawableRecordingDomain.Size,
                   context.OutputScale))
        {
            // This only builds the child's RenderNode tree. Pixel execution remains in the parent
            // request after RecordSubtree imports the complete child sequence.
            drawable.GetOriginal()!.Render(graphics, drawable);
        }

        IReadOnlyList<RenderFragmentHandle> outputs = context.RecordSubtree(root);
        Rect bounds = CalculateBounds(outputs, s_drawableRecordingDomain);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        return context.Layer(outputs, bounds);
    }

    private static RenderFragmentHandle RecordFallbackSource(RenderNodeContext context)
    {
        Brush.Resource fill = Brushes.Resource.White;
        RenderResource<Brush.Resource> fillToken = context.Borrow(fill);
        return context.OpaqueSource(OpaqueRenderDescription.Create(
            default(ParticleFallbackState),
            static (session, _) => session.UseResource(
                s_fallbackFillSlot,
                fill => DrawFallbackParticle(session, fill)),
            s_fallbackBoundsContract,
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector,
            resources: [s_fallbackFillSlot.Bind(fillToken)],
            slots: s_fallbackSlots));
    }

    private static void DrawFallbackParticle(OpaqueRenderSession session, Brush.Resource fill)
    {
        using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
        output.Canvas.Use(canvas => canvas.DrawEllipse(s_fallbackBounds, fill, null));
        session.Publish(output);
    }

    /// <summary>
    /// The particle's own transform scales and rotates the source, so the blit has to resample it.
    /// </summary>
    /// <remarks>
    /// Point sampling here reduces a particle to whichever texels its sample points land on, which is visible
    /// as stair-stepped edges on every particle whose size is not exactly the source's. Mitchell is the same
    /// resampler the canvas applies to any other scaled bitmap.
    /// </remarks>
    private static readonly SKSamplingOptions s_particleSampling = new(SKCubicResampler.Mitchell);

    private static void DrawParticles(TargetCommandSession session, Particle[] particles)
    {
        session.Canvas.Use(canvas =>
        {
            foreach (RenderExecutionInput input in session.Inputs)
            {
                DrawParticleInput(canvas, input, particles);
            }
        });
    }

    private static void DrawParticleInput(
        ImmediateCanvas canvas,
        RenderExecutionInput input,
        Particle[] particles)
    {
        Point center = input.Bounds.Center;
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle particle = ref particles[i];
            if (!IsVisibleParticle(in particle, out float scale, out float opacity))
                continue;

            float rotation = particle.Rotation * MathF.PI / 180f;
            Matrix transform = Matrix.CreateTranslation(-center.X, -center.Y)
                               * Matrix.CreateScale(scale, scale)
                               * Matrix.CreateRotation(rotation)
                               * Matrix.CreateTranslation(particle.X, particle.Y);
            Color color = particle.CurrentColor;
            using SKColorFilter? colorFilter = color == s_untintedParticle
                ? null
                : SKColorFilter.CreateBlendMode(
                    new SKColor(color.R, color.G, color.B, color.A),
                    SKBlendMode.Modulate);
            using (canvas.PushTransform(transform))
            using (var paint = new SKPaint
            {
                IsAntialias = true,
                ColorFilter = colorFilter,
                Color = s_opaqueWhite.WithAlpha(
                           (byte)Math.Clamp(MathF.Round(opacity * byte.MaxValue), 0, byte.MaxValue)),
            })
            {
                // The particle transform already carries the resampling, so the blit itself stays
                // unfiltered -- the same footprint the source would have drawn under that transform.
                input.Draw(canvas, paint, s_particleSampling);
            }
        }
    }

    /// <summary>
    /// Whether a particle reaches the canvas at all, and the scale and opacity it reaches it with.
    /// </summary>
    /// <remarks>
    /// The bounds loop and the drawing loop must decide this identically. The union is what the layer buffer is
    /// allocated from, so a particle the bounds loop counts but the drawing loop skips - a fully faded one, which
    /// <see cref="ParticleEmitter.EndOpacityMultiplier"/> drives every particle towards - buys buffer for pixels
    /// that never appear. A non-finite value is rejected rather than merely a non-positive one, because
    /// <c>NaN &lt;= 0</c> is false and a NaN would otherwise reach the union as an unbounded coordinate.
    /// </remarks>
    private static bool IsVisibleParticle(in Particle particle, out float scale, out float opacity)
    {
        scale = particle.CurrentSize / 10f;
        opacity = particle.CurrentOpacity / 100f;
        return particle.IsAlive
               && float.IsFinite(scale)
               && float.IsFinite(opacity)
               && scale > 0
               && opacity > 0;
    }

    /// <summary>
    /// The union of the axis-aligned extents each live particle draws into.
    /// </summary>
    /// <remarks>
    /// Every particle is scaled and then rotated about the source's own centre, so its extent is the rotated
    /// source rectangle's bounding box, not a square of the source's longer side: a 20x20 source turned 45
    /// degrees reaches about 4.14 further along each axis than that square. This is the rectangle the layer
    /// buffer is allocated from, so anything it misses is clipped away rather than merely mismeasured.
    /// </remarks>
    private static Rect CalculateParticleBounds(ReadOnlySpan<Particle> particles, Rect sourceBounds)
    {
        Rect totalBounds = Rect.Empty;
        bool hasBounds = false;
        var sourceWidth = (float)sourceBounds.Width;
        var sourceHeight = (float)sourceBounds.Height;
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle particle = ref particles[i];
            if (!IsVisibleParticle(in particle, out float scale, out _))
                continue;

            float radians = particle.Rotation * MathF.PI / 180f;
            if (!float.IsFinite(radians))
                continue;

            float cos = MathF.Abs(MathF.Cos(radians));
            float sin = MathF.Abs(MathF.Sin(radians));
            float width = ((sourceWidth * cos) + (sourceHeight * sin)) * scale;
            float height = ((sourceWidth * sin) + (sourceHeight * cos)) * scale;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
                continue;

            var particleBounds = new Rect(
                particle.X - (width / 2f),
                particle.Y - (height / 2f),
                width,
                height);
            totalBounds = hasBounds ? totalBounds.Union(particleBounds) : particleBounds;
            hasBounds = true;
        }

        return hasBounds ? totalBounds : Rect.Empty;
    }

    private static bool RequiresClippedLayer(Rect bounds, float scale)
    {
        // The pool refuses this layer against the device's attachment limit, not against the engine ceiling,
        // so a union between the two has to be clipped here or the pool drops the whole emitter. Predicted
        // rather than resolved: resolving builds a shared context, which Process may not do.
        int maxBufferDimension = RenderScaleUtilities.PredictRenderThreadMaxBufferDimension();
        PixelRect footprint = PixelRect.FromRect(bounds, scale);
        if (footprint.Width > maxBufferDimension || footprint.Height > maxBufferDimension)
        {
            return true;
        }

        try
        {
            long bytes = checked((long)footprint.Width * footprint.Height * 8);
            return bytes > MaximumFiniteLayerBytes;
        }
        catch (OverflowException)
        {
            return true;
        }
    }

    private static Rect CalculateBounds(
        IReadOnlyList<RenderFragmentHandle> fragments,
        Rect symbolicOwnerDomain)
    {
        Rect bounds = Rect.Empty;
        bool hasSymbolicMetadata = false;
        foreach (RenderFragmentHandle fragment in fragments)
        {
            if (!fragment.TryGetMetadata(out RenderFragmentMetadata metadata))
            {
                hasSymbolicMetadata = true;
                continue;
            }

            bounds = bounds.Union(metadata.Bounds);
        }

        return hasSymbolicMetadata ? bounds.Union(symbolicOwnerDomain) : bounds;
    }

    protected override void OnDispose(bool disposing)
    {
        Particle = null;
    }

    private readonly record struct ParticleCommandState;

    private readonly record struct ParticleFallbackState;
}
