using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class MetadataCallbackIdentityTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void TwoRecordingsOfOneStaticCallback_CompileOnePlan()
    {
        using var cache = new StructuralPlanCache();
        using var first = new CallbackSourceNode(PublishEmptyOutput);
        using var second = new CallbackSourceNode(PublishEmptyOutput);

        using (Compile(cache, first))
        {
        }

        using (Compile(cache, second))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(1),
                "a static method group is the same delegate at every call site, so both recordings key alike");
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
            Assert.That(cache.Statistics.Replacements, Is.Zero);
        });
    }

    [Test]
    public void TwoInstantiationsOfOneGenericHelper_CompileSeparatePlans()
    {
        using var cache = new StructuralPlanCache();
        using var first = new CallbackSourceNode(GenericSource<string>.PublishEmptyOutput);
        using var second = new CallbackSourceNode(GenericSource<object>.PublishEmptyOutput);

        using (Compile(cache, first))
        {
        }

        using (Compile(cache, second))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2),
                "each construction caches its own delegate, and a key built from the source location "
                + "would have collapsed both into one plan");
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.Zero);
        });
    }

    [Test]
    public void TwoDifferentCallbacks_CompileSeparatePlans()
    {
        using var cache = new StructuralPlanCache();
        using var first = new CallbackSourceNode(PublishEmptyOutput);
        using var second = new CallbackSourceNode(PublishOutputTwice);

        using (Compile(cache, first))
        {
        }

        using (Compile(cache, second))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.Zero);
        });
    }

    private static void PublishEmptyOutput(OpaqueRenderSession session, Rect bounds)
    {
        using OpaqueRenderOutput output = session.CreateOutput(bounds);
        session.Publish(output);
    }

    private static void PublishOutputTwice(OpaqueRenderSession session, Rect bounds)
    {
        using OpaqueRenderOutput output = session.CreateOutput(bounds);
        output.Canvas.Use(static canvas => canvas.Clear());
        session.Publish(output);
    }

    private static CompiledRenderRequest Compile(StructuralPlanCache cache, RenderNode node)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: FusionMode.Enabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler(cache).Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    /// <remarks>
    /// The lambda is cached in a static field of the constructed type, so <c>GenericSource&lt;string&gt;</c>
    /// and <c>GenericSource&lt;object&gt;</c> hand back different delegates even though the two share code.
    /// </remarks>
    private static class GenericSource<T>
    {
        public static Action<OpaqueRenderSession, Rect> PublishEmptyOutput { get; } =
            static (session, bounds) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(bounds);
                session.Publish(output);
            };
    }

    private sealed class CallbackSourceNode(Action<OpaqueRenderSession, Rect> execute) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                s_bounds,
                execute,
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale)));
    }
}
