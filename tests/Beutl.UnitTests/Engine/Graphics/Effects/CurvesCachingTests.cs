using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

[TestFixture]
public class CurvesCachingTests
{
    private static CurveMap CreateCurve(float midpoint)
        => new([
            new CurveControlPoint(0, 0),
            new CurveControlPoint(0.5f, midpoint),
            new CurveControlPoint(1, 1),
        ]);

    [Test]
    public void GetOrBuildCurveShader_ReplacementConstructionFailureKeepsOldCacheAlive()
    {
        var effect = new Curves();
        using var resource = (Curves.Resource)effect.ToResource(CompositionContext.Default);
        CurveMap firstCurve = CreateCurve(0.4f);
        CurveMap replacementCurve = CreateCurve(0.6f);
        SKShader first = resource.GetOrBuildCurveShader(0, firstCurve);
        var expected = new InvalidOperationException("simulated curve shader construction failure");

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            resource.GetOrBuildCurveShaderForTest(
                0,
                replacementCurve,
                _ => throw expected,
                static shader => shader.Dispose()));
        SKShader reused = resource.GetOrBuildCurveShader(0, firstCurve);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(reused, Is.SameAs(first),
                "a failed replacement must leave the previous cache key and shader installed");
            Assert.That(first.Handle, Is.Not.EqualTo(nint.Zero),
                "a failed replacement must not dispose the still-cached shader");
            Assert.That(resource.CurveShaderBuildCountForTest, Is.EqualTo(1),
                "failed construction is not a completed cache build");
        });
    }

    [Test]
    public void GetOrBuildCurveShader_OldDisposeFailureLeavesReplacementCached()
    {
        var effect = new Curves();
        using var resource = (Curves.Resource)effect.ToResource(CompositionContext.Default);
        CurveMap firstCurve = CreateCurve(0.4f);
        CurveMap replacementCurve = CreateCurve(0.6f);
        SKShader first = resource.GetOrBuildCurveShader(0, firstCurve);
        SKShader? replacement = null;
        var expected = new InvalidOperationException("simulated old curve shader dispose failure");

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
            resource.GetOrBuildCurveShaderForTest(
                0,
                replacementCurve,
                curve => replacement = curve.ToShader(),
                shader =>
                {
                    shader.Dispose();
                    throw expected;
                }));
        SKShader cached = resource.GetOrBuildCurveShader(0, replacementCurve);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected), "the first disposal failure must be preserved by identity");
            Assert.That(replacement, Is.Not.Null, "the replacement must be built before the old shader is disposed");
            Assert.That(cached, Is.SameAs(replacement),
                "a disposal failure after the swap must leave the replacement cache internally consistent");
            Assert.That(cached.Handle, Is.Not.EqualTo(nint.Zero), "the installed replacement remains usable");
            Assert.That(first.Handle, Is.EqualTo(nint.Zero), "the injected disposer still released the old shader");
            Assert.That(resource.CurveShaderBuildCountForTest, Is.EqualTo(2));
        });
    }

    [Test]
    public void GetOrBuildCurveShader_ReusesSameCurveAndDisposesReplacedShader()
    {
        var effect = new Curves();
        using var resource = (Curves.Resource)effect.ToResource(CompositionContext.Default);
        CurveMap firstCurve = CreateCurve(0.4f);
        CurveMap replacementCurve = CreateCurve(0.6f);

        SKShader first = resource.GetOrBuildCurveShader(0, firstCurve);
        SKShader reused = resource.GetOrBuildCurveShader(0, firstCurve);
        SKShader replacement = resource.GetOrBuildCurveShader(0, replacementCurve);

        Assert.Multiple(() =>
        {
            Assert.That(reused, Is.SameAs(first), "the same CurveMap instance must reuse its cached shader");
            Assert.That(resource.CurveShaderBuildCountForTest, Is.EqualTo(2));
            Assert.That(replacement, Is.Not.SameAs(first));
            Assert.That(first.Handle, Is.EqualTo(nint.Zero),
                "replacing a CurveMap must dispose the superseded native shader");
        });
    }

    [Test]
    public void Describe_ReusesAllNineResourceOwnedCurveShaders()
    {
        var effect = new Curves();
        using var resource = (Curves.Resource)effect.ToResource(CompositionContext.Default);
        var first = new EffectGraphBuilder(
            new Rect(0, 0, 16, 16), outputScale: 1f, workingScale: 1f,
            renderIntent: RenderIntent.Delivery);
        var second = new EffectGraphBuilder(
            new Rect(0, 0, 16, 16), outputScale: 1f, workingScale: 1f,
            renderIntent: RenderIntent.Delivery);

        effect.Describe(first, resource);
        using EffectGraph firstGraph = first.Build();
        effect.Describe(second, resource);
        using EffectGraph secondGraph = second.Build();

        Assert.That(resource.CurveShaderBuildCountForTest, Is.EqualTo(9),
            "re-describing an unchanged Curves resource must reuse all nine resource-owned shaders");
    }
}
