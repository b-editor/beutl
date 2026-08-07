using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Particles;

internal sealed class ParticleRenderNode(ParticleEmitter.Resource particle) : RenderNode
{
    private static readonly Rect s_drawableRecordingDomain = new(0, 0, 1920, 1080);

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

        RenderResource<Particle[]> particlesToken = context.Borrow(
            particles,
            new ParticleSnapshotIdentity(resource.GetOriginal().Id, snapshot.Version),
            snapshot.Version);
        TargetCommandDescription description = TargetCommandDescription.CreateRequestLocal(
            execute: session => session.UseResource(
                particlesToken,
                current => DrawParticles(session, current)),
            affectedRegion: TargetRegion.Region(totalBounds),
            queryBounds: totalBounds,
            hitTest: RenderHitTestContract.None,
            resources: [particlesToken]);
        RenderFragmentHandle painter = context.TargetCommand([source], description);

        // Repetition is an engine-controlled target command over a pre-recorded value. The finite
        // layer turns that ordered painter result back into the single value published by this node.
        context.Publish(context.Layer([painter], totalBounds));
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
        var bounds = new Rect(-5, -5, 10, 10);
        Brush.Resource fill = Brushes.Resource.White;
        RenderResource<Brush.Resource> fillToken = context.Borrow(
            fill,
            EngineResourceIdentity.Of(fill),
            fill.Version);
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            bounds,
            static (session, state) => DrawFallbackParticle(session, state),
            bounds: OpaqueRenderBoundsContract.Source(bounds),
            hitTest: RenderHitTestContract.OutputBounds,
            valueCardinality: RenderValueCardinality.Single,
            scale: RenderScaleContract.Vector,
            resources: [fillToken]);
        return context.OpaqueSource(description);
    }

    private static void DrawFallbackParticle(OpaqueRenderSession session, Rect bounds)
    {
        using OpaqueRenderOutput output = session.CreateOutput(session.RequiredRegion);
        output.Canvas.Use(canvas => session.UseDeclaredResource<Brush.Resource>(
            0,
            fill => canvas.DrawEllipse(bounds, fill, null)));
        session.Publish(output);
    }

    private static void DrawParticles(TargetCommandSession session, Particle[] particles)
    {
        session.Canvas.Use(canvas =>
        {
            foreach (RenderExecutionInput input in session.Inputs)
            {
                input.UseShader(shader => DrawParticleInput(canvas, input.Bounds, shader, particles));
            }
        });
    }

    private static void DrawParticleInput(
        ImmediateCanvas canvas,
        Rect inputBounds,
        SKShader shader,
        Particle[] particles)
    {
        Point center = inputBounds.Center;
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
                Shader = shader,
                ColorFilter = colorFilter,
                Color = SKColors.White.WithAlpha(
                           (byte)Math.Clamp(MathF.Round(opacity * byte.MaxValue), 0, byte.MaxValue)),
            })
            {
                canvas.Canvas.DrawRect(inputBounds.ToSKRect(), paint);
            }
        }
    }

    private static Rect CalculateParticleBounds(ReadOnlySpan<Particle> particles, Rect sourceBounds)
    {
        Rect totalBounds = Rect.Empty;
        bool hasBounds = false;
        float sourceDiameter = MathF.Max((float)sourceBounds.Width, (float)sourceBounds.Height);
        for (int i = 0; i < particles.Length; i++)
        {
            ref readonly Particle particle = ref particles[i];
            if (!particle.IsAlive)
                continue;

            float scale = particle.CurrentSize / 10f;
            if (!float.IsFinite(scale) || scale <= 0)
                continue;

            float diameter = sourceDiameter * scale;
            if (!float.IsFinite(diameter) || diameter <= 0)
                continue;

            var particleBounds = new Rect(
                particle.X - diameter / 2f,
                particle.Y - diameter / 2f,
                diameter,
                diameter);
            totalBounds = hasBounds ? totalBounds.Union(particleBounds) : particleBounds;
            hasBounds = true;
        }

        return hasBounds ? totalBounds : Rect.Empty;
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

    private readonly record struct ParticleSnapshotIdentity(Guid ResourceId, int Version);

    private sealed class ParticleFallbackSource
    {
    }
}
