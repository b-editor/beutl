using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A budget named on an executor must bound every allocation it makes, not only Flush and custom targets:
// typed fallback stages and the nested executor Activate builds allocated against the device instead.
[TestFixture]
[NonParallelizable]
public sealed class NamedBufferBudgetReachTests
{
    // Below every device the suite runs on, so the clamp is observable without depending on the GPU.
    private const int TestBufferDimension = 16;

    private static readonly Rect s_bounds = new(0, 0, 64, 8);

    [Test]
    public void TypedShaderStage_ClampsToTheNamedBudget()
    {
        PixelSize observed = RunShader(BufferDimensionBudget.Named(TestBufferDimension));

        Assert.That(observed.Width, Is.LessThanOrEqualTo(TestBufferDimension));
    }

    [Test]
    public void TypedShaderStage_WithoutANamedBudget_KeepsItsDensity()
    {
        PixelSize observed = RunShader(budget: null);

        Assert.That(observed, Is.EqualTo(new PixelSize(64, 8)));
    }

    [Test]
    public void TypedGeometryStage_ClampsToTheNamedBudget()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        var effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.Geometry(GeometryDescription.CreateRequestLocal(
                static session => session.Canvas.Use(session.Input.Draw),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput)));

        Apply(effect, targets, BufferDimensionBudget.Named(TestBufferDimension));

        Assert.Multiple(() =>
        {
            Assert.That(targets, Has.Count.EqualTo(1));
            Assert.That(targets[0].DeviceBounds.Width, Is.LessThanOrEqualTo(TestBufferDimension));
            Assert.That(targets[0].Scale.Value, Is.LessThan(1f));
        });
    }

    [Test]
    public void NestedActivation_ClampsToTheNamedBudget()
    {
        PixelSize observed = default;
        var effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.Shader(CreateObservingShader(size => observed = size)));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(effect, resource);
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        using var builder = new SKImageFilterBuilder();
        using var executor = CreateExecutor(targets, builder, BufferDimensionBudget.Named(TestBufferDimension));

        using SKImageFilter? _ = executor.Activate(context);

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.Not.EqualTo(default(PixelSize)), "The nested shader stage did not run.");
            Assert.That(observed.Width, Is.LessThanOrEqualTo(TestBufferDimension));
        });
    }

    private static PixelSize RunShader(BufferDimensionBudget? budget)
    {
        PixelSize observed = default;
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        var effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.Shader(CreateObservingShader(size => observed = size)));

        Apply(effect, targets, budget);

        Assert.That(observed, Is.Not.EqualTo(default(PixelSize)), "The shader stage did not run.");
        return observed;
    }

    private static ShaderDescription CreateObservingShader(Action<PixelSize> observe)
        => ShaderDescription.CurrentPixel(
            "uniform float marker; half4 apply(half4 color) { return color * marker; }",
            bindings => bindings.Uniform(
                "marker",
                1f,
                (writer, value, execution) =>
                {
                    observe(execution.DeviceSize);
                    writer.Set(value);
                }));

    private static void Apply(FilterEffect effect, EffectTargets targets, BufferDimensionBudget? budget)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds);
        context.ApplyTransactional(effect, resource);
        using var builder = new SKImageFilterBuilder();
        using var executor = CreateExecutor(targets, builder, budget);
        executor.Apply(context);
        executor.Flush(false);
    }

    private static FilterEffectExecutor CreateExecutor(
        EffectTargets targets,
        SKImageFilterBuilder builder,
        BufferDimensionBudget? budget)
        => new(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            deviceGridOffset: default,
            budget: budget);

    private static EffectTargets CreateSolidTargets(Rect bounds)
    {
        using RenderTarget renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("A CPU render target is required for this test.");
        using (var canvas = new ImmediateCanvas(
                   renderTarget,
                   RenderIntent.Preview,
                   density: 1,
                   maxWorkingScale: 1,
                   logicalSize: bounds.Size))
        {
            canvas.Clear(Colors.Red);
        }

        return new EffectTargets
        {
            new EffectTarget(renderTarget, bounds, EffectiveScale.At(1)),
        };
    }
}
