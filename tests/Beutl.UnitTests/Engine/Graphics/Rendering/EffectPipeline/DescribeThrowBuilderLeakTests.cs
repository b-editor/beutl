using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the describe-throw builder-tracked disposable leak: <see cref="EffectGraphBuilder.Sampler"/>,
/// <see cref="EffectGraphBuilder.Child"/> and <see cref="EffectGraphBuilder.Track{T}"/> register native shaders
/// eagerly, but ownership transfers to the <c>EffectGraph</c> only at <see cref="EffectGraphBuilder.Build"/>. A
/// <c>Describe</c> that registers a shader and then throws (before <c>Build</c>) stranded the handle. The engine now
/// aborts an unbuilt builder, releasing still-owned disposables; a successful <c>Build</c> transfers ownership so a
/// later abort is a no-op. Effect authors cannot dispose the engine-owned builder themselves. SkiaSharp zeroes a
/// shader's handle on dispose, so the handle is the observability seam.
/// </summary>
[NonParallelizable]
[TestFixture]
public class DescribeThrowBuilderLeakTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    [Test]
    public void DescribeThrowsAfterRegisteringSampler_BuilderAbortReleasesShader()
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

        builder.Abort();

        Assert.That(shader.Handle, Is.EqualTo(IntPtr.Zero),
            "aborting an unbuilt builder must release the disposables Describe registered before it threw");
    }

    [Test]
    public void DescribeThrowsAfterRegisteringChildAndTrack_BuilderAbortReleasesBoth()
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

        builder.Abort();

        Assert.Multiple(() =>
        {
            Assert.That(child.Handle, Is.EqualTo(IntPtr.Zero), "the child shader must be released on the throw path");
            Assert.That(tracked.Handle, Is.EqualTo(IntPtr.Zero), "the tracked shader must be released on the throw path");
        });
    }

    [Test]
    public void SuccessfulBuild_TransfersOwnership_SoBuilderAbortDoesNotDoubleDispose()
    {
        SKShader shader = SKShader.CreateColor(SKColors.Red);
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        builder.Sampler("lut", shader);

        EffectGraph graph = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(() => builder.Blur(new Size(1, 1)), Throws.InvalidOperationException,
                "Build permanently closes append surfaces");
            Assert.That(() => builder.Build(), Throws.InvalidOperationException,
                "a builder can transfer ownership only once");
        });

        // Build transferred ownership: aborting the builder afterward must NOT free the graph's shader.
        builder.Abort();
        Assert.That(shader.Handle, Is.Not.EqualTo(IntPtr.Zero),
            "a successful Build transfers the disposables, so aborting the builder must not touch the graph's shader");

        graph.Dispose();
        Assert.That(shader.Handle, Is.EqualTo(IntPtr.Zero),
            "the graph owns the shader after Build and releases it exactly once on its own dispose");
    }

    [Test]
    public void BuilderLifetime_IsEngineOwned_AndAbortPermanentlyClosesIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IDisposable).IsAssignableFrom(typeof(EffectGraphBuilder)), Is.False,
                "effect authors do not own the builder lifetime");
            Assert.That(typeof(EffectGraphBuilder).GetMethod("Dispose", Type.EmptyTypes), Is.Null,
                "the public surface must not expose a disposal trap");
        });

        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        builder.Abort();
        using SKShader rejected = SKShader.CreateColor(SKColors.Red);

        Assert.Multiple(() =>
        {
            Assert.That(() => builder.Blur(new Size(1, 1)), Throws.InvalidOperationException);
            Assert.That(() => builder.Blur(new Size(0, 0)), Throws.InvalidOperationException,
                "identity conveniences must not bypass the closed-builder state");
            Assert.That(() => builder.Track(rejected), Throws.InvalidOperationException);
            Assert.That(() => builder.Build(), Throws.InvalidOperationException);
            Assert.That(() => builder.Abort(), Throws.Nothing, "Abort is idempotent");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Effect_RejectsPlanChildAfterBuilderCloses(bool abort)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        EffectGraph? graph = null;
        if (abort)
            builder.Abort();
        else
            graph = builder.Build();

        using (graph)
        using (FilterEffect.Resource child = new FallbackFilterEffect().ToResource(CompositionContext.Default))
        {
            Assert.That(() => builder.Effect(child), Throws.InvalidOperationException,
                "an empty plan child must not bypass the builder's Built/Aborted invariant");
        }
    }

    [Test]
    public void PlanSetupThrows_DisposesInputOperationsBeforeExecutorOwnership()
    {
        var effect = new ThrowingDescribeEffect();
        using var resource = (FilterEffect.Resource)effect.ToResource(CompositionContext.Default);
        using var node = new PlanFilterEffectRenderNode(resource);
        bool disposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds, static _ => { }, onDispose: () => disposed = true);

        Assert.Throws<InvalidOperationException>(() => node.Process(new RenderNodeContext([input])));

        Assert.That(disposed, Is.True,
            "the plan node must release inputs when Describe fails before ownership reaches PlanExecutor");
    }
}

internal sealed partial class ThrowingDescribeEffect : FilterEffect
{
    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
        => throw new InvalidOperationException("simulated plan setup failure");
}
