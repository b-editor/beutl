using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class DeliveryIntentDeclarationContractTests
{
    private static readonly Rect s_domain = new(0, 0, 100, 100);

    [Test]
    public void TheRendererConstructor_RequiresAnExplicitIntent()
    {
        ParameterInfo intent = RequireParameter(typeof(Renderer), "intent");

        Assert.That(intent.HasDefaultValue, Is.False);
    }

    [TestCase("intent")]
    [TestCase("drawableBrushMaterializer")]
    public void TheBrushConstructor_RequiresAnExplicit(string parameterName)
    {
        ParameterInfo parameter = RequireParameter(typeof(BrushConstructor), parameterName);

        Assert.That(parameter.HasDefaultValue, Is.False);
    }

    [Test]
    public void TheCanvasConstructor_RequiresAnExplicitIntent()
    {
        ParameterInfo intent = RequireParameter(typeof(ImmediateCanvas), "intent");

        Assert.Multiple(() =>
        {
            Assert.That(intent.HasDefaultValue, Is.False);
            Assert.That(
                typeof(ImmediateCanvas).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .Any(static constructor => constructor.GetParameters()
                        .All(static parameter => parameter.Name == "renderTarget" || parameter.HasDefaultValue)),
                Is.False,
                "no public constructor may be reached with a render target alone, which would hand a delivery "
                + "host a canvas that degrades instead of failing");
        });
    }

    [Test]
    public void ADeliveryRendererStillDeclaresItsIntent()
    {
        using var renderer = new Renderer(4, 4, RenderIntent.Delivery);

        Assert.That(renderer.Intent, Is.EqualTo(RenderIntent.Delivery));
    }

    [Test]
    public void ABrushConstructorWithoutAMaterializer_StillStatesIt()
    {
        var constructor = new BrushConstructor(
            new Rect(0, 0, 4, 4),
            Brushes.Resource.White,
            BlendMode.SrcOver,
            RenderIntent.Delivery,
            drawableBrushMaterializer: null);

        Assert.That(constructor.Intent, Is.EqualTo(RenderIntent.Delivery));
    }

    // A canvas acts on its intent when it configures a fill: a brush whose content cannot be produced paints
    // transparent under Preview and fails the render under Delivery. Construction is the only place that
    // decision is made, so an intent stated there has to reach the paint.
    [Test]
    public void AnExplicitDeliveryIntent_SurvivesToTheCanvasFill()
    {
        using DrawableBrush.Resource brush = CreateUnmaterializableBrushResource();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => FillARectangle(RenderIntent.Preview, brush),
                Throws.Nothing,
                "a preview canvas degrades the fill to transparent and keeps drawing");
            Assert.That(
                () => FillARectangle(RenderIntent.Delivery, brush),
                Throws.InvalidOperationException.With.Message.Contains("no runtime materializer"));
        });
    }

    private static void FillARectangle(RenderIntent intent, Brush.Resource brush)
    {
        using RenderTarget target = RenderTarget.Create(64, 36)
                                    ?? throw new InvalidOperationException("Could not create the canvas target.");
        using var canvas = new ImmediateCanvas(target, intent, logicalSize: new Size(64, 36));
        canvas.DrawRectangle(new Rect(0, 0, 64, 36), brush, pen: null);
    }

    private static DrawableBrush.Resource CreateUnmaterializableBrushResource()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        return new DrawableBrush(content).ToResource(CompositionContext.Default);
    }

    [Test]
    public void TheRendererRequest_RequiresAnExplicitIntent()
    {
        PropertyInfo intent = RequireProperty(typeof(RenderNodeRenderRequest), nameof(RenderNodeRenderRequest.Intent));

        Assert.That(intent.GetCustomAttribute<RequiredMemberAttribute>(), Is.Not.Null);
    }

    [Test]
    public void TheRenderNodeRendererConstructor_RequiresACompleteDefaultRequest()
    {
        ParameterInfo defaultRequest = RequireParameter(typeof(RenderNodeRenderer), "defaultRequest");

        Assert.Multiple(() =>
        {
            Assert.That(defaultRequest.HasDefaultValue, Is.False);
            Assert.That(
                typeof(RenderNodeRenderer).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .Any(static constructor => constructor.GetParameters()
                        .All(static parameter => parameter.Name == "root" || parameter.HasDefaultValue)),
                Is.False,
                "no public constructor may be reached with a root alone, which would synthesize a Preview request");
        });
    }

    // Rasterize has no destination canvas to promote the intent from, so the request it was handed is the
    // only thing that can carry Delivery into execution: a delivery rasterization fails on an intermediate a
    // preview would have dropped.
    [Test]
    public void AnExplicitDeliveryIntent_SurvivesToRasterize()
    {
        using FilterEffect.Resource resource = CreateStrokeEffectResource();
        using FilterEffectRenderNode previewNode = CreateScene(resource);
        using var previewRenderer = new RenderNodeRenderer(
            previewNode,
            CreateRequest(RenderIntent.Preview),
            new FailSecondTargetFactory());
        using RenderNodeRasterization dropped = previewRenderer.Rasterize();

        using FilterEffectRenderNode deliveryNode = CreateScene(resource);
        using var deliveryRenderer = new RenderNodeRenderer(
            deliveryNode,
            CreateRequest(RenderIntent.Delivery),
            new FailSecondTargetFactory());

        Assert.Multiple(() =>
        {
            Assert.That(dropped.Bitmap, Is.Not.Null, "a preview drops the intermediate and still ships a frame");
            Assert.That(
                () =>
                {
                    using RenderNodeRasterization unexpected = deliveryRenderer.Rasterize();
                },
                Throws.InvalidOperationException);
        });
    }

    private static RenderNodeRenderRequest CreateRequest(RenderIntent intent)
        => new()
        {
            Intent = intent,
            TargetDomain = s_domain,
            OutputScale = 1,
            MaxWorkingScale = intent == RenderIntent.Delivery ? float.PositiveInfinity : 2,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        };

    private static FilterEffect.Resource CreateStrokeEffectResource()
    {
        var pen = new Pen
        {
            Thickness = { CurrentValue = 9 },
            Brush = { CurrentValue = Brushes.OrangeRed },
        };
        var effect = new StrokeEffect
        {
            Pen = { CurrentValue = pen },
        };
        return effect.ToResource(CompositionContext.Default);
    }

    private static FilterEffectRenderNode CreateScene(FilterEffect.Resource resource)
    {
        var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_domain, Brushes.Resource.White, null));
        return node;
    }

    private static PropertyInfo RequireProperty(Type type, string name)
        => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
           ?? throw new InvalidOperationException($"{type.Name} declares no public '{name}'.");

    private sealed class FailSecondTargetFactory : IRenderTargetFactory
    {
        private int _createCalls;

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
            => _createCalls++ == 1
                ? null
                : RenderTarget.Create(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private static ParameterInfo RequireParameter(Type type, string name)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (parameter.Name == name)
                    return parameter;
            }
        }

        throw new InvalidOperationException($"No public {type.Name} constructor declares '{name}'.");
    }
}
