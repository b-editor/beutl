using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

/// <summary>
/// Characterizes which pixel-affecting channels are rejected as mutable callback state and which channels
/// remain explicit author-owned resource identities whose versions must be maintained correctly.
/// </summary>
/// <remarks>
/// State passing rejects a mutable reference or delegate anywhere in its complete field graph. Static reads
/// and resources registered behind an explicitly pinned key and version remain author-declared identity
/// channels, so the stale-output tests and verification backstop still characterize those cases.
/// </remarks>
[TestFixture]
public sealed class RenderCacheIdentityChannelTests
{
    internal static class RenderCacheIdentityChannelSlots
    {
        internal static readonly RenderResourceSlot<Func<Color>> ReadColor = new();
        internal static readonly RenderResourceSlot<ColorBox> Payload = new();
        internal static readonly RenderResourceSlot<ExecutionProbe> Probe = new();
    }

    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    public void StaticFieldReadByTheStaticCallback_IsServedStale()
    {
        using var node = new StaticFieldNode();
        StaticFieldNode.Color = Colors.Red;
        AssertServedStale(node, static () => StaticFieldNode.Color = Colors.Blue);
    }

    [Test]
    public void BorrowedResourceBehindAPinnedCacheKeyAndVersion_IsServedStale()
    {
        using var node = new PinnedResourceNode();
        AssertServedStale(node, () => node.Payload.Color = Colors.Blue);
    }

    [Test]
    public void CapturingDelegateBorrowedAsAResource_IsServedStale()
    {
        using var node = new BorrowedDelegateNode();
        AssertServedStale(node, () => node.Color = Colors.Blue);
    }

    private static void AssertMutableStateRejected(ProbedRenderNode node)
    {
        using RenderNodeRenderer renderer = CreateRenderer(node);

        ArgumentException? rejection = Assert.Throws<ArgumentException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(rejection!.ParamName, Is.EqualTo("state"));
            Assert.That(rejection.Message, Does.Contain("deeply immutable"));
        });
    }

    /// <summary>
    /// Drives two frames around <paramref name="changeTheDrawnValue"/> and asserts the second frame reused the
    /// first frame's pixels without re-executing the producer.
    /// </summary>
    private static void AssertServedStale(ProbedRenderNode node, Action changeTheDrawnValue)
    {
        node.Cache.RecordStableRequests();
        using RenderNodeRenderer renderer = CreateRenderer(node);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        changeTheDrawnValue();
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecuteCount, Is.EqualTo(1),
                "a cache hit serves the stored pixels without re-executing the producer");
            Assert.That(TopLeft(second), Is.EqualTo(ToPremultipliedHalfBits(Colors.Red)),
                "the second frame shows the first frame's colour");
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private static ulong TopLeft(RenderNodeRasterization rasterization)
    {
        Assert.That(rasterization.IsEmpty, Is.False);
        return ReadFirstPixel(rasterization.Bitmap!);
    }

    private static ulong ToPremultipliedHalfBits(Color color)
    {
        using var target = new CpuRenderTarget(1, 1);
        target.Value.Canvas.Clear(color.ToSKColor());
        using Bitmap snapshot = target.Snapshot();
        return ReadFirstPixel(snapshot);
    }

    private static ulong ReadFirstPixel(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        return ((ulong)pixels[0] << 48)
               | ((ulong)pixels[1] << 32)
               | ((ulong)pixels[2] << 16)
               | pixels[3];
    }

    private static OpaqueRenderDescription Describe<TState>(
        TState state,
        Action<OpaqueRenderSession, TState> execute,
        IEnumerable<RenderResourceBinding>? resources = null)
        where TState : notnull
        => OpaqueRenderDescription.Create(
            state,
            execute,
            OpaqueRenderBoundsContract.FullInputs(static _ => s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            resources: resources);

    internal sealed class ColorBox
    {
        public Color Color { get; set; }
    }

    private abstract class ProbedRenderNode : RenderNode
    {
        protected readonly ExecutionProbe Probe = new();
        private readonly object _probeKey = new();

        public int ExecuteCount => Probe.Count;

        protected RenderResourceBinding BindProbe(RenderNodeContext context)
            => RenderCacheIdentityChannelSlots.Probe.Bind(context.Borrow(Probe));
    }

    /// <summary>Channel 1: state holds a reference whose contents change between frames.</summary>
    private sealed class MutableStateHolderNode : ProbedRenderNode
    {
        public ColorBox Box { get; } = new() { Color = Colors.Red };

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = Describe(
                Box,
                static (session, box) => session.UseResource(RenderCacheIdentityChannelSlots.Probe, probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(box.Color));
                    session.Publish(output);
                }),
                resources: [BindProbe(context)]);
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    /// <summary>Channel 2: the static callback reads a static field the state never mentions.</summary>
    private sealed class StaticFieldNode : ProbedRenderNode
    {
        public static Color Color { get; set; } = Colors.Red;

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = Describe(
                typeof(StaticFieldNode),
                static (session, _) => session.UseResource(RenderCacheIdentityChannelSlots.Probe, probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Color));
                    session.Publish(output);
                }),
                resources: [BindProbe(context)]);
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    /// <summary>
    /// Channel 3: a capturing delegate one level down. The state walk descends through tuple elements, so the
    /// same delegate placed directly in the state tuple is now rejected; a holder object is where it survives.
    /// </summary>
    private sealed class DelegateInHolderNode : ProbedRenderNode
    {
        private readonly ColorSource _source;

        public DelegateInHolderNode() => _source = new ColorSource(() => Color);

        public Color Color { get; set; } = Colors.Red;

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = Describe(
                _source,
                static (session, source) => session.UseResource(RenderCacheIdentityChannelSlots.Probe, probe =>
                {
                    probe.Record();
                    Color color = source.Read();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(color));
                    session.Publish(output);
                }),
                resources: [BindProbe(context)]);
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }

        private sealed class ColorSource(Func<Color> read)
        {
            public Color Read() => read();
        }
    }

    /// <summary>Channel 4: a borrowed resource whose content changes behind a pinned cache key and version.</summary>
    private sealed class PinnedResourceNode : ProbedRenderNode
    {
        private static readonly object s_pinnedCacheKey = new();

        public ColorBox Payload { get; } = new() { Color = Colors.Red };

        public override void Process(RenderNodeContext context)
        {
            RenderResource<ColorBox> resource =
                context.Borrow(Payload);
            OpaqueRenderDescription description = Describe(
                typeof(PinnedResourceNode),
                static (session, _) => session.UseResource(RenderCacheIdentityChannelSlots.Probe, probe =>
                {
                    probe.Record();
                    session.UseResource(RenderCacheIdentityChannelSlots.Payload, payload =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                        output.Canvas.Use(canvas => canvas.Clear(payload.Color));
                        session.Publish(output);
                    });
                }),
                resources: [RenderCacheIdentityChannelSlots.Payload.Bind(resource), BindProbe(context)]);
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    /// <summary>
    /// Channel 5: the route the contract itself recommends and
    /// <c>FilterEffectInputBinding.PublishDeferredPreviews</c> took. The capturing delegate did not stop
    /// capturing; it moved out of the callback closure into a declared resource under an author-declared identity.
    /// </summary>
    private sealed class BorrowedDelegateNode : ProbedRenderNode
    {
        private static readonly object s_declaredIdentity = new();

        private readonly Func<Color> _readColor;

        public BorrowedDelegateNode() => _readColor = () => Color;

        public Color Color { get; set; } = Colors.Red;

        public override void Process(RenderNodeContext context)
        {
            RenderResource<Func<Color>> sink = context.Borrow(_readColor);
            OpaqueRenderDescription description = Describe(
                typeof(BorrowedDelegateNode),
                static (session, _) => session.UseResource(RenderCacheIdentityChannelSlots.Probe, probe =>
                {
                    probe.Record();
                    session.UseResource(RenderCacheIdentityChannelSlots.ReadColor, readColor =>
                    {
                        Color color = readColor();
                        using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                        output.Canvas.Use(canvas => canvas.Clear(color));
                        session.Publish(output);
                    });
                }),
                resources: [RenderCacheIdentityChannelSlots.ReadColor.Bind(sink), BindProbe(context)]);
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation) => RenderScaleUtilities.MaxBufferDimension;

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
