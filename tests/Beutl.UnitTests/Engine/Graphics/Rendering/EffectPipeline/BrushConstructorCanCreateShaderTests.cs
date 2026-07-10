using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Locks <see cref="BrushConstructor.CanCreateShader"/> to the real null/non-null outcome of
/// <see cref="BrushConstructor.CreateShader"/> for every brush-resource kind, so the non-rendering describe-time
/// predicate (contract A1 / FR-001) cannot drift from the render-time truth it stands in for.
/// </summary>
[NonParallelizable]
[TestFixture]
public class BrushConstructorCanCreateShaderTests
{
    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // Non-rendering brush kinds: CreateShader is a pure shader construction, so parity can be checked directly.
    [Test]
    public void CanCreateShader_MatchesCreateShader_ForNonRenderingBrushes()
    {
        Assert.Multiple(() =>
        {
            AssertParity(null, "null brush");
            AssertParity(Solid(255, 10, 20, 30), "solid colour");
            AssertParity(Gradient(2), "gradient with stops");
            AssertParity(Gradient(0), "gradient with no stops");
            AssertParity(Perlin(), "perlin noise");
            AssertParity(Perlin((PerlinNoiseType)999), "perlin noise with an out-of-range type");
            AssertParity(DrawableBrushResource(drawable: null), "drawable brush with no drawable");
            AssertParity(Presenter(new SolidColorBrush(Colors.Red)), "presenter to a solid");
            AssertParity(Presenter(null), "presenter with no target");
        });
    }

    // A drawable brush WITH a drawable resolves its shader by RENDERING the drawable, so parity for this kind must
    // exercise the real render path — the exact path CanCreateShader lets an author skip at describe time.
    [Test]
    public void CanCreateShader_MatchesCreateShader_ForDrawableBrushWithDrawable()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var shape = new RectShape();
            shape.Width.CurrentValue = 32;
            shape.Height.CurrentValue = 32;
            shape.Fill.CurrentValue = new SolidColorBrush(Colors.White);
            Brush.Resource brush = DrawableBrushResource(shape);

            bool predicted = BrushConstructor.CanCreateShader(brush);
            using SKShader? shader = new BrushConstructor(new Rect(0, 0, 32, 32), brush, BlendMode.SrcOver).CreateShader();
            Assert.That(predicted, Is.EqualTo(shader != null),
                "CanCreateShader must match CreateShader for a drawable brush with a drawable");
        });
    }

    // An image brush with no bitmap yields no usable shader; CanCreateShader reports false. (CreateShader THROWS for
    // this shape rather than returning null, so it is asserted on the predicate alone, per the predicate's contract.)
    [Test]
    public void CanCreateShader_False_ForImageBrushWithoutBitmap()
    {
        Brush.Resource brush = (Brush.Resource)new ImageBrush().ToResource(CompositionContext.Default);
        Assert.That(BrushConstructor.CanCreateShader(brush), Is.False);
    }

    private static void AssertParity(Brush.Resource? brush, string because)
    {
        using SKShader? shader = new BrushConstructor(new Rect(0, 0, 40, 40), brush, BlendMode.SrcOver).CreateShader();
        Assert.That(BrushConstructor.CanCreateShader(brush), Is.EqualTo(shader != null), because);
    }

    private static Brush.Resource Solid(byte a, byte r, byte g, byte b)
        => (Brush.Resource)new SolidColorBrush(Color.FromArgb(a, r, g, b)).ToResource(CompositionContext.Default);

    private static Brush.Resource Gradient(int stops)
    {
        var brush = new LinearGradientBrush();
        for (int i = 0; i < stops; i++)
            brush.GradientStops.Add(new GradientStop(Colors.White, i / (float)Math.Max(1, stops - 1)));
        return (Brush.Resource)brush.ToResource(CompositionContext.Default);
    }

    private static Brush.Resource Perlin(PerlinNoiseType type = PerlinNoiseType.Turbulence)
    {
        var brush = new PerlinNoiseBrush();
        brush.PerlinNoiseType.CurrentValue = type;
        return (Brush.Resource)brush.ToResource(CompositionContext.Default);
    }

    private static Brush.Resource DrawableBrushResource(Drawable? drawable)
    {
        var brush = new DrawableBrush();
        brush.Drawable.CurrentValue = drawable;
        return (Brush.Resource)brush.ToResource(CompositionContext.Default);
    }

    private static Brush.Resource Presenter(Brush? target)
    {
        var presenter = new BrushPresenter();
        presenter.Target.CurrentValue = target;
        return (Brush.Resource)presenter.ToResource(CompositionContext.Default);
    }
}
