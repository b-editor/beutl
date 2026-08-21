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
    private static readonly RenderResourceSlot<Brush.Resource> s_fallbackFillSlot = new();
    private static readonly OpaqueRenderDefinition<ParticleFallbackState> s_fallbackDefinition =
        OpaqueRenderDefinition<ParticleFallbackState>.Create(
            static (session, _) => session.UseResource(
                s_fallbackFillSlot,
                fill => DrawFallbackParticle(session, fill)),
            OpaqueRenderBoundsContract.Source(s_fallbackBounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector,
            resources: [s_fallbackFillSlot]);

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
        TargetCommandDefinition<ParticleCommandState> definition =
            TargetCommandDefinition<ParticleCommandState>.Create(
                static (session, _) => session.UseResource(
                    s_particlesSlot,
                    current => DrawParticles(session, current)),
                affectedRegion: requiresClippedLayer
                    ? TargetRegion.Full
                    : TargetRegion.Region(totalBounds),
                queryBounds: totalBounds,
                hitTest: RenderHitTestContract.None,
                resources: [s_particlesSlot]);
        RenderFragmentHandle painter = context.TargetCommand(
            [source],
            definition.Call(default, [s_particlesSlot.Bind(particlesToken)]));

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
            drawable.GetOriginal().Render(graphics, drawable);
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
        return context.OpaqueSource(
            s_fallbackDefinition.Call(
                default,
                [s_fallbackFillSlot.Bind(fillToken)]));
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
            if (!particle.IsAlive)
                continue;

            float scale = particle.CurrentSize / 10f;
            float opacity = particle.CurrentOpacity / 100f;
            if (!float.IsFinite(scale)
                || !float.IsFinite(opacity)
                || scale <= 0
                || opacity <= 0)
            {
                continue;
            }

            float rotation = particle.Rotation * MathF.PI / 180f;
            Matrix transform = Matrix.CreateTranslation(-center.X, -center.Y)
                               * Matrix.CreateScale(scale, scale)
                               * Matrix.CreateRotation(rotation)
                               * Matrix.CreateTranslation(particle.X, particle.Y);
            Color color = particle.CurrentColor;
            using SKColorFilter? colorFilter = color == Colors.White
                ? null
                : SKColorFilter.CreateBlendMode(
                    new SKColor(color.R, color.G, color.B, color.A),
                    SKBlendMode.Modulate);
            using (canvas.PushTransform(transform))
            using (var paint = new SKPaint
            {
                IsAntialias = true,
                ColorFilter = colorFilter,
                Color = SKColors.White.WithAlpha(
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
            if (!particle.IsAlive)
                continue;

            float scale = particle.CurrentSize / 10f;
            if (!float.IsFinite(scale) || scale <= 0)
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
        PixelRect footprint = PixelRect.FromRect(bounds, scale);
        if (footprint.Width > RenderScaleUtilities.MaxBufferDimension
            || footprint.Height > RenderScaleUtilities.MaxBufferDimension)
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
