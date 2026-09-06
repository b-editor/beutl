using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[TestFixture]
public sealed class RawSessionSlotResourceTests
{
    private static readonly Rect s_domain = new(0, 0, 8, 8);

    [Test]
    public void ARawScopeReachesTheResourceBoundToItsDeclaredSlot()
    {
        using var node = new SlotAddressingNode(throughScope: true);

        Render(node);

        Assert.That(node.Reached, Is.EqualTo(new[] { "bound" }));
    }

    [Test]
    public void ARawCommandReachesTheResourceBoundToItsDeclaredSlot()
    {
        using var node = new SlotAddressingNode(throughScope: false);

        Render(node);

        Assert.That(node.Reached, Is.EqualTo(new[] { "bound" }));
    }

    [Test]
    public void RebindingTheSlotChangesWhatTheSameDefinitionReaches()
    {
        using var node = new SlotAddressingNode(throughScope: true);

        Render(node);
        node.RebindToTheOtherPayload();
        Render(node);

        Assert.That(
            node.Reached,
            Is.EqualTo(new[] { "bound", "rebound" }),
            "The slot is what the callback names, so a new binding has to reach it without the callback "
            + "changing.");
    }

    private static void Render(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_domain,
            CacheOptions = RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, new CpuTargetFactory());
        renderer.Rasterize().Dispose();
    }

    private sealed class SlotAddressingNode(bool throughScope) : RenderNode
    {
        private static readonly RenderResourceSlot<Payload> s_slot = new();

        private readonly Payload _bound = new("bound");
        private readonly Payload _rebound = new("rebound");
        private bool _useRebound;

        public List<string> Reached { get; } = [];

        public void RebindToTheOtherPayload()
        {
            _useRebound = true;
            MarkChanged();
        }

        public override void Process(RenderNodeContext context)
        {
            Payload payload = _useRebound ? _rebound : _bound;
            payload.Reached = Reached;
            RenderResource<Payload> token = context.Borrow(payload);

            if (!throughScope)
            {
                context.Publish(context.RawTargetCommand(RawTargetCommandDescription.Create(
                    s_domain,
                    static (session, _) => session.UseResource(s_slot, static payload => payload.Reach()),
                    s_domain,
                    RenderHitTestContract.OutputBounds,
                    resources: [s_slot.Bind(token)])));
                return;
            }

            // A scope needs something to replay, and this inner command must not reach the slot itself or
            // the assertion could not tell which of the two sessions addressed it.
            RenderFragmentHandle inert = context.RawTargetCommand(
                RawTargetCommandDescription.CreateRequestLocal(
                    static _ => { },
                    s_domain,
                    RenderHitTestContract.OutputBounds));
            context.Publish(context.RawTargetScope(inert, RawTargetScopeDescription.Create(
                s_domain,
                static (session, _) =>
                {
                    session.UseResource(s_slot, static payload => payload.Reach());
                    session.ReplayInput();
                },
                RenderBoundsContract.FullInput,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                resources: [s_slot.Bind(token)])));
        }
    }

    private sealed class Payload(string name)
    {
        public List<string>? Reached { get; set; }

        public void Reach() => Reached?.Add(name);
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
