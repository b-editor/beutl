using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the describe-throw builder-tracked disposable leak: <see cref="EffectGraphBuilder.Sampler"/>,
/// <see cref="EffectGraphBuilder.Child"/> and <see cref="EffectGraphBuilder.Track{T}"/> register native shaders
/// eagerly, but ownership transfers to the <c>EffectGraph</c> only at <see cref="EffectGraphBuilder.Build"/>. A
/// <c>Describe</c> that registers a shader and then throws (before <c>Build</c>) stranded the handle. The builder is now
/// <see cref="IDisposable"/>: disposing an un-built builder releases the still-owned disposables, while a successful
/// <c>Build</c> detaches them so disposing the builder afterward does not double-dispose the graph's shaders. The
/// production sites (<c>PlanFilterEffectRenderNode.Process</c>, the nested-graph branch describe) now
/// <c>using</c>-scope the builder. SkiaSharp zeroes a shader's handle on dispose, so the handle is the observability seam.
/// </summary>
[NonParallelizable]
[TestFixture]
public class DescribeThrowBuilderLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void DescribeThrowsAfterRegisteringSampler_BuilderDisposeReleasesShader()
    {
        SKShader shader = SKShader.CreateColor(SKColors.Red);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);

        // A Describe that registers an eager sampler and then fails: ownership never reaches a graph.
        Assert.Throws<InvalidOperationException>(() =>
        {
            builder.Sampler("lut", shader);
            throw new InvalidOperationException("simulated describe failure after registering a sampler");
        });

        Assert.That(shader.Handle, Is.Not.EqualTo(IntPtr.Zero),
            "sanity: the registered shader is still alive until the builder is disposed");

        builder.Dispose();

        Assert.That(shader.Handle, Is.EqualTo(IntPtr.Zero),
            "disposing an un-built builder must release the disposables Describe registered before it threw");
    }

    [Test]
    public void DescribeThrowsAfterRegisteringChildAndTrack_BuilderDisposeReleasesBoth()
    {
        SKShader child = SKShader.CreateColor(SKColors.Green);
        SKShader tracked = SKShader.CreateColor(SKColors.Blue);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);

        Assert.Throws<InvalidOperationException>(() =>
        {
            builder.Child("map", child);
            builder.Track(tracked);
            throw new InvalidOperationException("simulated describe failure after registering a child and a tracked shader");
        });

        builder.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(child.Handle, Is.EqualTo(IntPtr.Zero), "the child shader must be released on the throw path");
            Assert.That(tracked.Handle, Is.EqualTo(IntPtr.Zero), "the tracked shader must be released on the throw path");
        });
    }

    [Test]
    public void SuccessfulBuild_TransfersOwnership_SoBuilderDisposeDoesNotDoubleDispose()
    {
        SKShader shader = SKShader.CreateColor(SKColors.Red);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        builder.Sampler("lut", shader);

        EffectGraph graph = builder.Build();

        // Build transferred ownership: disposing the builder afterward must NOT free the graph's shader.
        builder.Dispose();
        Assert.That(shader.Handle, Is.Not.EqualTo(IntPtr.Zero),
            "a successful Build detaches the disposables, so disposing the builder must not touch the graph's shader");

        graph.Dispose();
        Assert.That(shader.Handle, Is.EqualTo(IntPtr.Zero),
            "the graph owns the shader after Build and releases it exactly once on its own dispose");
    }
}
