using System.Reactive;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class FilterEffectBoundsTests
{
    private static readonly Rect s_inputBounds = new(10, 20, 100, 60);

    [Test]
    public void TwoArgumentCustomEffect_KeepsBoundsUnknownAfterLaterFiniteTransform()
    {
        using var context = new FilterEffectContext(s_inputBounds);

        context.CustomEffect(Unit.Default, static (_, _) => { });
        context.CustomEffect(
            Unit.Default,
            static (_, _) => { },
            static (_, _) => new Rect(1, 2, 3, 4));

        Assert.That(context.Bounds.IsInvalid, Is.True);
    }

    [Test]
    public void UnknownCustomEffect_WithTargetDomain_UsesCompleteDomain()
    {
        var domain = new Rect(-20, -10, 320, 180);
        using var node = CreateUnknownBoundsNode();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = domain,
                UseRenderCache = false,
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.That(measurement.OutputBounds, Is.EqualTo(domain));
    }

    [Test]
    public void UnknownCustomEffect_WithoutTargetDomain_FailsDuringDomainResolution()
    {
        using var node = CreateUnknownBoundsNode();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions { UseRenderCache = false });

        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() => renderer.Measure());

        Assert.That(error!.Message, Does.Contain("transformBounds").And.Contain("TargetDomain"));
    }

    [Test]
    public void UnknownCustomEffect_RenderDestinationSuppliesFiniteDomain()
    {
        using var node = CreateUnknownBoundsNode();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });
        using var target = new CpuRenderTarget(64, 48);
        using var canvas = new ImmediateCanvas(target);

        Assert.That(() => renderer.Render(canvas), Throws.Nothing);
    }

    [Test]
    public void LayerEffect_DeclaresIdentityBounds()
    {
        Assert.That(ApplyBounds(new LayerEffect()), Is.EqualTo(s_inputBounds));
    }

    [Test]
    public void DisplacementMapPreview_DeclaresIdentityBounds()
    {
        var effect = new DisplacementMapEffect
        {
            ShowDisplacementMap = { CurrentValue = true },
        };

        Assert.That(ApplyBounds(effect), Is.EqualTo(s_inputBounds));
    }

    [TestCaseSource(nameof(DisplacementMapTransforms))]
    public void DisplacementMapTransform_DeclaresIdentityBounds(DisplacementMapTransform transform)
    {
        var effect = new DisplacementMapEffect
        {
            Transform = { CurrentValue = transform },
        };

        Assert.That(ApplyBounds(effect), Is.EqualTo(s_inputBounds));
    }

    [Test]
    public void PartsSplitEffect_DeclaresInputUpperBound()
    {
        Assert.That(ApplyBounds(new PartsSplitEffect()), Is.EqualTo(s_inputBounds));
    }

    [Test]
    public void DelayAnimationEffect_RemainsUnknownForArbitraryChildEffect()
    {
        Assert.That(ApplyBounds(new DelayAnimationEffect()).IsInvalid, Is.True);
    }

    [Test]
    public void SplitEffect_InflatesForSpacing()
    {
        var effect = new SplitEffect
        {
            HorizontalDivisions = { CurrentValue = 3 },
            VerticalDivisions = { CurrentValue = 2 },
            HorizontalSpacing = { CurrentValue = 12 },
            VerticalSpacing = { CurrentValue = 8 },
        };

        Assert.That(ApplyBounds(effect), Is.EqualTo(new Rect(-2, 16, 124, 68)));
    }

    [Test]
    public void ShakeEffect_InflatesToClampedMaximumOffset()
    {
        var effect = new ShakeEffect
        {
            StrengthX = { CurrentValue = 250_000 },
            StrengthY = { CurrentValue = -40 },
        };

        Assert.That(ApplyBounds(effect), Is.EqualTo(new Rect(-99_990, -20, 200_100, 140)));
    }

    [Test]
    public void PathFollowEffect_DeclaresFiniteConservativeBounds()
    {
        var effect = new PathFollowEffect
        {
            Geometry = { CurrentValue = PathGeometry.Parse("M0,0 L0,100") },
            Progress = { CurrentValue = 100 },
            FollowRotation = { CurrentValue = true },
        };
        var center = new Vector(s_inputBounds.Width / 2, s_inputBounds.Height / 2);
        Matrix origin = Matrix.CreateTranslation(center + s_inputBounds.Position);
        Matrix expectedTransform = -origin
            * Matrix.CreateRotation(MathF.PI / 2)
            * origin
            * Matrix.CreateTranslation(0, 100);
        Rect expected = s_inputBounds.TransformToAABB(expectedTransform);

        Rect actual = ApplyBounds(effect);

        AssertRectContains(actual, expected, 0.001f);
    }

    [Test]
    public void PathFollowEffect_DeclaredBoundsContainSeparatedRuntimeTargets()
    {
        var inputBounds = new Rect(0, 0, 100, 10);
        var effect = new PathFollowEffect
        {
            Geometry = { CurrentValue = PathGeometry.Parse("M0,0 L0,100") },
            Progress = { CurrentValue = 100 },
            FollowRotation = { CurrentValue = true },
        };

        using var context = new FilterEffectContext(inputBounds);
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        effect.ApplyTo(context, resource);

        using RenderTarget firstSource = RenderTarget.Create(10, 10)
            ?? throw new InvalidOperationException("A render target is required for this test.");
        using RenderTarget secondSource = RenderTarget.Create(10, 10)
            ?? throw new InvalidOperationException("A render target is required for this test.");
        using var targets = new EffectTargets
        {
            new EffectTarget(firstSource, new Rect(0, 0, 10, 10)),
            new EffectTarget(secondSource, new Rect(90, 0, 10, 10)),
        };
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Delivery,
            RenderRequestPurpose.Auxiliary);

        activator.Apply(context);

        AssertRectContains(context.Bounds, activator.CurrentTargets.CalculateBounds(), 0.001f);
    }

    private static Rect ApplyBounds(FilterEffect effect)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_inputBounds);
        effect.ApplyTo(context, resource);
        return context.Bounds;
    }

    private static IEnumerable<TestCaseData> DisplacementMapTransforms()
    {
        yield return new TestCaseData(new DisplacementMapTranslateTransform()).SetName("Translate");
        yield return new TestCaseData(new DisplacementMapScaleTransform()).SetName("Scale");
        yield return new TestCaseData(new DisplacementMapRotationTransform()).SetName("Rotation");
    }

    private static FilterEffectRenderNode CreateUnknownBoundsNode()
    {
        var effect = new UnknownBoundsFilterEffect();
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(new EllipseRenderNode(
            new Rect(5, 6, 20, 12),
            Brushes.Resource.White,
            null));
        return node;
    }

    private static void AssertRectWithin(Rect actual, Rect expected, float tolerance)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance));
            Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(tolerance));
            Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(tolerance));
        });
    }

    private static void AssertRectContains(Rect outer, Rect inner, float tolerance)
    {
        Assert.Multiple(() =>
        {
            Assert.That(outer.Left, Is.LessThanOrEqualTo(inner.Left + tolerance));
            Assert.That(outer.Top, Is.LessThanOrEqualTo(inner.Top + tolerance));
            Assert.That(outer.Right, Is.GreaterThanOrEqualTo(inner.Right - tolerance));
            Assert.That(outer.Bottom, Is.GreaterThanOrEqualTo(inner.Bottom - tolerance));
        });
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
    }

    private sealed class CpuRenderTarget : RenderTarget
    {
        public CpuRenderTarget(int width, int height)
            : base(CreateSurface(width, height), width, height)
        {
        }

        private static SKSurface CreateSurface(int width, int height)
            => SKSurface.Create(new SKImageInfo(
                   width,
                   height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   SKColorSpace.CreateSrgbLinear()))
               ?? throw new InvalidOperationException("A CPU test surface could not be created.");
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class UnknownBoundsFilterEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        => context.CustomEffect(Unit.Default, static (_, _) => { });

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource;
}
