using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>
/// A <see cref="TransformEffect"/> with <c>ApplyToTarget</c> maps its target through a user-supplied matrix,
/// so it is the one transform in the engine whose box is drawn from a matrix nobody vetted for perspective.
/// These fix the two answers it must give when that matrix crosses <c>w = 0</c>: the delivered box wherever
/// the request still shows the image, and nothing at all where it does not — never the untransformed target.
/// </summary>
[TestFixture]
public sealed class TransformEffectPerspectiveTargetTests
{
    private static readonly Rect s_frame = new(0, 0, 256, 144);

    /// <summary>
    /// The divisor of <c>Rotation3DTransform</c> is <c>1 + (x - CenterX) * sin(RotationY) / Depth</c>, so a
    /// centre far enough to the right drops every corner of a target this size below
    /// <see cref="Rect.DefaultNearPlane"/> while the right edge stays in front of
    /// <see cref="Rect.RasterizerNearPlane"/> — the band the pragmatic box answers <see cref="Rect.Empty"/> for
    /// and the rasterizer still draws. An animated card flip sweeps through it.
    /// </summary>
    [Test]
    public void ApplyToTarget_WhenTheStraddlingImageMissesTheDeliveryRegion_DropsTheTargetInsteadOfPassingItThrough()
    {
        var bounds = new Rect(66, 43, 124, 58);
        Matrix m1 = ComposeTargetMatrix(bounds, Rotation3D(rotationY: 30f, centerX: 1030f));

        TestContext.WriteLine($"pragmatic={bounds.TransformToAABB(m1)} "
                              + $"rasterizer={bounds.TransformToAABB(m1, Rect.RasterizerNearPlane)} "
                              + $"delivered={bounds.TransformToDeliveredAABB(m1, s_frame)}");
        Assert.Multiple(() =>
        {
            Assert.That(bounds.TransformToAABB(m1), Is.EqualTo(Rect.Empty),
                "the fixture must sit in the band where the pragmatic box gives up");
            Assert.That(bounds.TransformToAABB(m1, Rect.RasterizerNearPlane).Width, Is.GreaterThan(0),
                "the fixture must still be something the rasterizer would draw");
            Assert.That(bounds.TransformToDeliveredAABB(m1, s_frame), Is.EqualTo(Rect.Empty),
                "the fixture must put that image outside the delivery region, so dropping it is the "
                + "geometrically correct answer rather than an allocation failure");
        });

        using EffectTargets targets = ApplyToSingleTarget(bounds, rotationY: 30f, centerX: 1030f);

        Assert.That(targets, Is.Empty,
            "a transform whose image reaches nothing the request delivers must drop the target; returning it "
            + "renders the layer as if the transform had never been applied");
    }

    /// <summary>
    /// Near edge-on the same transform keeps corners in front of <see cref="Rect.DefaultNearPlane"/>, so the
    /// pragmatic box is non-empty — but it still cuts a wedge that lands inside the frame. The effect must
    /// declare <see cref="Rect.TransformToDeliveredAABB"/>, which is exact wherever the request delivers.
    /// </summary>
    [Test]
    public void ApplyToTarget_WhenTheStraddlingImageReachesTheDeliveryRegion_DeclaresTheDeliveredBox()
    {
        var bounds = new Rect(-472, 45, 1200, 54);
        Matrix m1 = ComposeTargetMatrix(bounds, Rotation3D(rotationY: 89.5f, centerX: 0f));
        Rect pragmatic = bounds.TransformToAABB(m1);
        Rect delivered = bounds.TransformToDeliveredAABB(m1, s_frame);

        TestContext.WriteLine($"pragmatic={pragmatic} delivered={delivered}");
        Assert.Multiple(() =>
        {
            Assert.That(pragmatic.Width, Is.GreaterThan(0),
                "this fixture is the non-empty half of the crossing case");
            Assert.That(delivered, Is.Not.EqualTo(pragmatic));
            Assert.That(delivered.Contains(pragmatic), Is.True,
                "the delivered box never gives up what the pragmatic one already covered");
            Assert.That(delivered.Intersect(s_frame).Width, Is.GreaterThan(pragmatic.Intersect(s_frame).Width),
                "the fixture must put the recovered wedge inside the frame, where the viewer sees it");
        });

        using EffectTargets targets = ApplyToSingleTarget(bounds, rotationY: 89.5f, centerX: 0f);

        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].Bounds, Is.EqualTo(delivered),
            "the pragmatic box would clip the in-frame wedge out of the transformed layer");
    }

    private static Rotation3DTransform Rotation3D(float rotationY, float centerX)
        => new(0f, rotationY, 0f, centerX, 0f, 0f);

    /// <summary>
    /// Rebuilds the matrix <see cref="TransformEffect"/> maps a target's own bounds through: the transform
    /// re-centred on the target's <c>TransformOrigin</c>, which defaults to its centre.
    /// </summary>
    private static Matrix ComposeTargetMatrix(Rect bounds, Transform transform)
    {
        Vector origin = RelativePoint.Center.ToPixels(bounds.Size);
        Matrix offset = Matrix.CreateTranslation(origin + bounds.Position);
        return -offset * transform.CreateMatrix(CompositionContext.Default) * offset;
    }

    /// <summary>
    /// Runs the effect over one CPU-backed target covering <paramref name="bounds"/> and hands back whatever
    /// the activation left in the target list.
    /// </summary>
    private static EffectTargets ApplyToSingleTarget(Rect bounds, float rotationY, float centerX)
    {
        var deviceBounds = new PixelRect(
            (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
        using var backing = new CpuRenderTarget(deviceBounds.Width, deviceBounds.Height);
        var targets = new EffectTargets
        {
            new EffectTarget(backing, bounds, EffectiveScale.At(1), deviceBounds),
        };

        var effect = new TransformEffect
        {
            ApplyToTarget = { CurrentValue = true },
            Transform = { CurrentValue = Rotation3D(rotationY, centerX) },
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(effect, resource);

        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            drawableBrushMaterializer: null,
            targetDomain: s_frame);
        activator.Apply(context);
        activator.Flush(false);
        return targets;
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height)
    {
        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("A CPU effect-test surface could not be created.");
    }
}
