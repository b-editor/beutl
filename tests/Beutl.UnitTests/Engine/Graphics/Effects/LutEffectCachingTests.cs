using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Effects;

// LutEffect.Describe ran per frame and re-rasterized the whole cube into an SKShader every time (per-frame on an
// animated chain). The shader is a pure function of the CubeFile, so the resource now caches it keyed on cube
// instance identity and owns its lifetime.
[TestFixture]
public class LutEffectCachingTests
{
    private static CubeFile MakeCube()
        => new()
        {
            Title = "t",
            Dimention = CubeFileDimension.ThreeDimension,
            Size = 2,
            Min = Vector3.Zero,
            Max = Vector3.One,
            Data = new Vector3[8],
        };

    [Test]
    public void GetOrBuildLutShader_SameCubeReusesShader_ChangedCubeRebuildsAndDisposesOld()
    {
        var effect = new LutEffect();
        using var resource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        CubeFile cubeA = MakeCube();
        CubeFile cubeB = MakeCube();

        SKShader first = resource.GetOrBuildLutShader(cubeA);
        SKShader again = resource.GetOrBuildLutShader(cubeA);
        SKShader rebuilt = resource.GetOrBuildLutShader(cubeB);

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.SameAs(first), "the same cube must reuse the cached LUT shader, not re-rasterize");
            Assert.That(rebuilt, Is.Not.SameAs(first), "a changed cube must rebuild the LUT shader");
            Assert.That(first.Handle, Is.EqualTo(nint.Zero), "the superseded LUT shader must be disposed on rebuild");
        });
    }

    [Test]
    public void GetOrBuildLutShader_SwappingBackToAPreviousCube_ReturnsALiveShader()
    {
        var effect = new LutEffect();
        using var resource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        CubeFile cubeA = MakeCube();
        CubeFile cubeB = MakeCube();

        resource.GetOrBuildLutShader(cubeA);
        resource.GetOrBuildLutShader(cubeB);
        SKShader back = resource.GetOrBuildLutShader(cubeA);

        Assert.That(back.Handle, Is.Not.EqualTo(nint.Zero),
            "the cache must never hand out a disposed shader after cube swaps in either direction");
    }

    [Test]
    public void Update_WithoutSourceCube_ReleasesCachedShader()
    {
        var effect = new LutEffect();
        using var resource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        SKShader cached = resource.GetOrBuildLutShader(MakeCube());

        bool updateOnly = true;
        resource.Update(effect, CompositionContext.Default, ref updateOnly);

        Assert.That(cached.Handle, Is.EqualTo(nint.Zero),
            "removing the LUT source must release the cached native shader immediately");
    }

    [Test]
    public void Update_WithoutSourceCube_ReleasesCacheAndAdvancesVersion()
    {
        var effect = new LutEffect();
        using var resource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        _ = resource.GetOrBuildLutShader(MakeCube());
        int previousVersion = resource.Version;

        bool updateOnly = true;
        resource.Update(effect, CompositionContext.Default, ref updateOnly);

        Assert.That(resource.Version, Is.GreaterThan(previousVersion),
            "discarding a cached LUT must invalidate any retained plan that captured that shader");
    }

    [Test]
    public void Update_WithFailedCubeLoad_ReleasesCacheAndAdvancesVersion()
    {
        var effect = new LutEffect();
        var missing = new CubeSource();
        missing.ReadFrom(new Uri("file:///path/that/does/not/exist.cube"));
        effect.Source.CurrentValue = missing;
        using var resource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        SKShader cached = resource.GetOrBuildLutShader(MakeCube());
        int previousVersion = resource.Version;

        bool updateOnly = true;
        resource.Update(effect, CompositionContext.Default, ref updateOnly);

        Assert.Multiple(() =>
        {
            Assert.That(cached.Handle, Is.EqualTo(nint.Zero),
                "a failed cube load must discard the shader built from the previous cube");
            Assert.That(resource.Version, Is.GreaterThan(previousVersion),
                "a failed cube load must invalidate any retained plan that captured the old shader");
        });
    }

    [Test]
    public void Describe_WithoutSourceCube_DoesNotMutateCachedShader()
    {
        var effect = new LutEffect();
        using var resource = (LutEffect.Resource)effect.ToResource(CompositionContext.Default);
        SKShader cached = resource.GetOrBuildLutShader(MakeCube());
        var builder = new EffectGraphBuilder(new Rect(0, 0, 16, 16), outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);

        effect.Describe(builder, resource);

        Assert.That(cached.Handle, Is.Not.EqualTo(nint.Zero),
            "Describe must remain side-effect-free apart from appending graph descriptors");
    }
}
