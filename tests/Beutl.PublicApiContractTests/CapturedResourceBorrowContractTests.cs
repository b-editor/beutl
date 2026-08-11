using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class CapturedResourceBorrowContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 4, 3);
    private static readonly RenderResourceSlot<BorrowedPayload> s_payloadSlot = new();
    private static readonly OpaqueRenderDefinition<byte> s_definition =
        OpaqueRenderDefinition<byte>.Create(
            static (session, _) => session.UseResource(s_payloadSlot, payload =>
            {
                payload.Uses++;
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(static canvas => canvas.Clear(Colors.White));
                session.Publish(output);
            }),
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            resources: [s_payloadSlot]);

    [Test]
    public void BorrowedResource_IsBoundByItsTypedSlotWithoutAnAuthorIdentity()
    {
        var payload = new BorrowedPayload();
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<BorrowedPayload> token = context.Borrow(payload);
            context.Publish(context.OpaqueSource(
                s_definition.Call(default, [s_payloadSlot.Bind(token)])));
        });
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(payload.Uses, Is.EqualTo(1));
        });
    }

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class BorrowedPayload
    {
        public int Uses { get; set; }
    }
}
