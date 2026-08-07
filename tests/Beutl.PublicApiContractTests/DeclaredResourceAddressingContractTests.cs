using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Pins why a session exposes the token-taking <c>UseResource</c>, the positional
/// <c>UseDeclaredResource</c>, or only one of them.
/// </summary>
/// <remarks>
/// A callback reaches a token only by capturing it, and captures are permitted exactly on the channels that do
/// not derive their runtime identity from the callback's state. A painted source has no such channel, so its
/// session is positional-only and no token form can be added to it that any caller could reach.
/// </remarks>
[TestFixture]
public sealed class DeclaredResourceAddressingContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void APaintedSourceState_CannotCarryAResourceToken()
    {
        ArgumentException? rejection = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");
            rejection = Assert.Throws<ArgumentException>(() => context.PaintedSource(
                state: (s_bounds, token),
                draw: static (session, state) => session.UseDeclaredResource<Payload>(
                    0,
                    _ => session.Canvas.Clear(Colors.Red)),
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(DeclaredResourceAddressingContractTests),
                resources: [token]));
        });

        _ = Measure(node);

        Assert.That(rejection, Is.Not.Null);
        TestContext.Out.WriteLine(rejection!.Message);
        Assert.That(rejection.ParamName, Is.EqualTo("state"),
            "the state is the output-cache runtime identity, so a request-scoped token may not travel in it");
    }

    [Test]
    public void APaintedSourceDrawCallback_CannotCaptureAResourceToken()
    {
        ArgumentException? rejection = null;
        using var node = new DelegateSourceNode(context =>
        {
            RenderResource<Payload> token = context.Borrow(new Payload(), cacheKey: "payload");
            rejection = Assert.Throws<ArgumentException>(() => context.PaintedSource(
                state: s_bounds,
                draw: (session, _) => session.UseDeclaredResource<Payload>(
                    IndexOf(token),
                    _ => session.Canvas.Clear(Colors.Red)),
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(DeclaredResourceAddressingContractTests),
                resources: [token]));
        });

        _ = Measure(node);

        Assert.That(rejection, Is.Not.Null);
        TestContext.Out.WriteLine(rejection!.Message);
        Assert.That(rejection.ParamName, Is.EqualTo("draw"),
            "the only other route to a token is capture, which a state-passing callback may not do");
    }

    [Test]
    public void EachSession_ExposesTheAddressingModesItsChannelsCanReach()
    {
        (Type Session, bool Token, bool Positional)[] expected =
        [
            (typeof(OpaqueRenderSession), true, true),
            (typeof(GeometrySession), true, true),
            (typeof(TargetScopeSession), true, true),
            (typeof(TargetCommandSession), true, true),
            (typeof(RawTargetScopeSession), true, false),
            (typeof(RawTargetCommandSession), true, false),
            (typeof(PaintedRenderSession), false, true),
        ];

        string[] actual = [.. expected.Select(item => Describe(
            item.Session,
            HasPublicMethod(item.Session, "UseResource"),
            HasPublicMethod(item.Session, "UseDeclaredResource")))];
        string[] pinned = [.. expected.Select(item => Describe(item.Session, item.Token, item.Positional))];

        Assert.That(actual, Is.EqualTo(pinned),
            "a session takes tokens exactly when a capturing channel can reach it, and positions exactly when "
            + "a state-passing one can");
    }

    private static int IndexOf(RenderResource resource)
    {
        _ = resource;
        return 0;
    }

    private static string Describe(Type session, bool token, bool positional)
        => $"{session.Name}: token={token}, positional={positional}";

    private static bool HasPublicMethod(Type session, string name)
        => session.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(method => method.Name == name);

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.Measure();
    }

    private sealed class Payload;

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }
}
