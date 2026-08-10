using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins the declared resource list of the in-tree sources that borrow a captured engine resource.
/// </summary>
/// <remarks>
/// <c>RenderCacheResolver.AddResources</c> writes the list's count and then each binding name and
/// <c>CacheIdentity</c> in declaration order into the output-cache key, so the names, contents and order of a <c>resources:</c>
/// argument are part of every affected fragment's identity. <see cref="OutputIdentityDependsOnResourceOrder"/>
/// is what makes the pins below load-bearing rather than decorative.
/// </remarks>
[TestFixture]
public sealed class DeclaredResourceOrderTests
{
    [Test]
    public void GeometryRenderNode_DeclaresHitTestThenPrimaryGeometryThenPaint()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 40 }, Height = { CurrentValue = 30 } };
        var fill = new SolidColorBrush(Colors.Red);
        var pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 2 } };
        using Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
        using SolidColorBrush.Resource fillResource = fill.ToResource(CompositionContext.Default);
        using Pen.Resource penResource = pen.ToResource(CompositionContext.Default);
        using var node = new GeometryRenderNode(geometryResource, fillResource, penResource);

        WithOpaqueRoot(node, payload =>
        {
            IReadOnlyList<RenderResourceBinding> declared = payload.Description.Resources;
            Assert.Multiple(() =>
            {
                Assert.That(Keys(declared), Is.EqualTo(new object?[]
                {
                    null,
                    geometry.Id,
                    fill.Id,
                    pen.Id,
                    pen.Brush.CurrentValue!.Id,
                }));
                Assert.That(
                    declared.Select(static binding => binding.Name),
                    Is.EqualTo(new[] { "hitTest", "__primary", "__paint0", "__paint1", "__paint2" }));
                Assert.That(declared[1].Resource.CacheIdentity.Version, Is.EqualTo(geometryResource.Version));
                Assert.That(declared[0].Resource.CacheIdentity.Key, Is.Not.Null,
                    "the hit-test state carries a composite identity rather than a bare object id");
            });
        });
    }

    [Test]
    public void ImageSourceRenderNode_DeclaresTheSourceFirst()
    {
        var uri = TestMediaHelper.CreateTestImageUri(16, 16, Colors.White);
        var image = new ImageSource();
        image.ReadFrom(uri);
        using ImageSource.Resource imageResource = image.ToResource(CompositionContext.Default);
        var fill = new SolidColorBrush(Colors.Red);
        using SolidColorBrush.Resource fillResource = fill.ToResource(CompositionContext.Default);
        using var node = new ImageSourceRenderNode(imageResource, fillResource, null);

        WithOpaqueRoot(node, payload =>
        {
            IReadOnlyList<RenderResourceBinding> declared = payload.Description.Resources;
            Assert.Multiple(() =>
            {
                Assert.That(Keys(declared), Is.EqualTo(new object?[] { image.Id, fill.Id }));
                Assert.That(declared[0].Resource.CacheIdentity.Version, Is.EqualTo(imageResource.Version));
            });
        });
    }

    [Test]
    public void OutputIdentityDependsOnResourceOrder()
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);

        using var forward = new TwoResourceNode(swapped: false);
        using var reversed = new TwoResourceNode(swapped: true);
        RenderFragmentOutputIdentity forwardIdentity = OutputIdentity(forward, owner);
        RenderFragmentOutputIdentity reversedIdentity = OutputIdentity(reversed, owner);

        Assert.That(forwardIdentity, Is.Not.EqualTo(reversedIdentity),
            "swapping two declared resources must change the fragment's output-cache key");
    }

    private static object?[] Keys(IReadOnlyList<RenderResourceBinding> resources)
        => [.. resources.Select(static binding => binding.Resource.CacheIdentity.Key as Guid?)
            .Select(static id => (object?)id)];

    private static void WithOpaqueRoot(RenderNode node, Action<OpaqueRenderFragmentPayload> assert)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        assert((OpaqueRenderFragmentPayload)GetSingleRoot(graph).Payload!);
    }

    private static RenderFragmentOutputIdentity OutputIdentity(RenderNode node, RenderRequestOwner owner)
    {
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        return RenderFragmentOutputIdentity.Create(GetSingleRoot(graph), graph.RequestId);
    }

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            owner: owner));

    private static RenderFragmentReference GetSingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }

    private sealed class TwoResourceNode(bool swapped) : RenderNode
    {
        private static readonly Rect s_bounds = new(0, 0, 8, 8);
        private static readonly object s_firstKey = "first";
        private static readonly object s_secondKey = "second";

        private readonly Payload _first = new();
        private readonly Payload _second = new();

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Payload> first = context.Borrow(_first, s_firstKey, version: 1);
            RenderResource<Payload> second = context.Borrow(_second, s_secondKey, version: 2);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                s_bounds,
                static (session, bounds) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: swapped
                    ? [second.Bind("second"), first.Bind("first")]
                    : [first.Bind("first"), second.Bind("second")]);
            context.Publish(context.OpaqueSource(description));
        }

        internal sealed class Payload;
    }
}
