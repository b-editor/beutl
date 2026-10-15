using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RawSessionSlotResourceTests
{
    private static readonly Rect s_domain = new(0, 0, 8, 8);

    [Test]
    public void ARawCommandReachesTheResourceBoundToItsDeclaredSlot()
    {
        using var node = new SlotAddressingNode();

        Render(node);

        Assert.That(node.Reached, Is.EqualTo(new[] { "bound" }));
    }

    private static void Render(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_domain,
            CacheOptions = RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, new CpuRenderTargetFactory());
        renderer.Rasterize().Dispose();
    }

    private sealed class SlotAddressingNode : RenderNode
    {
        private static readonly RenderResourceSlot<Payload> s_slot = new();

        private readonly Payload _bound = new("bound");

        public List<string> Reached { get; } = [];

        public override void Process(RenderNodeContext context)
        {
            _bound.Reached = Reached;
            RenderResource<Payload> token = context.Borrow(_bound);
            context.Publish(context.RawTargetCommand(RawTargetCommandDescription.Create(
                s_domain,
                static (session, _) => session.UseResource(s_slot, static payload => payload.Reach()),
                s_domain,
                RenderHitTestContract.OutputBounds,
                resources: [s_slot.Bind(token)])));
        }
    }

    private sealed class Payload(string name)
    {
        public List<string>? Reached { get; set; }

        public void Reach() => Reached?.Add(name);
    }
}
