using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

/// <summary>
/// Characterizes the channels a per-frame value still reaches a non-capturing execution callback through
/// without changing the output-cache identity that selected the callback's cached pixels.
/// </summary>
/// <remarks>
/// State passing closes the accidental channel — the ordinary capturing lambda — but a deliberate or indirect
/// one stays open, because <c>TState : notnull</c> admits a mutable reference type whose identity is its
/// reference. Each test drives two frames with the cache enabled, changes the drawn value between them, and
/// asserts the second frame was served from the first. They are the empirical anchor for the enumerated
/// channel list in <c>docs/specs/004-gpu-pass-fusion/contracts/public-api.md</c>.
/// </remarks>
[TestFixture]
public sealed class RenderCacheIdentityChannelTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    public void MutableObjectReferencedByState_IsServedStale()
    {
        using var node = new MutableStateHolderNode();
        AssertServedStale(node, () => node.Box.Color = Colors.Blue);
    }

    [Test]
    public void StaticFieldReadByTheStaticCallback_IsServedStale()
    {
        using var node = new StaticFieldNode();
        StaticFieldNode.Color = Colors.Red;
        AssertServedStale(node, static () => StaticFieldNode.Color = Colors.Blue);
    }

    [Test]
    public void CapturingDelegateInsideAStateHolderObject_IsServedStale()
    {
        using var node = new DelegateInHolderNode();
        AssertServedStale(node, () => node.Color = Colors.Blue);
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

    [Test]
    [NonParallelizable]
    public void AnOpenChannelIsCaughtByRenderCacheVerification()
    {
        using var node = new MutableStateHolderNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using RenderNodeRenderer renderer = CreateRenderer(node, null);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        node.Box.Color = Colors.Blue;
        RenderCacheOutputMismatchException? mismatch;
        using (RenderCacheVerification.EnableForAllRequests())
        {
            mismatch = Assert.Throws<RenderCacheOutputMismatchException>(() => renderer.Rasterize());
        }

        TestContext.Out.WriteLine(mismatch!.Message);
        Assert.That(mismatch.Message, Does.Contain(nameof(MutableStateHolderNode)),
            "verification is the backstop for a channel the recording-time rules cannot close");
    }

    /// <summary>
    /// Drives two frames around <paramref name="changeTheDrawnValue"/> and asserts the second frame reused the
    /// first frame's pixels without re-executing the producer.
    /// </summary>
    private static void AssertServedStale(ProbedRenderNode node, Action changeTheDrawnValue)
    {
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using RenderNodeRenderer renderer = CreateRenderer(node, diagnostics);

        using (RenderNodeRasterization _ = renderer.Rasterize())
        {
        }

        changeTheDrawnValue();
        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1),
                "the changed value never reached the cache identity, so the lookup still matches");
            Assert.That(node.ExecuteCount, Is.EqualTo(1),
                "a cache hit serves the stored pixels without re-executing the producer");
            Assert.That(TopLeft(second), Is.EqualTo(ToPremultipliedHalfBits(Colors.Red)),
                "the second frame shows the first frame's colour");
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        RenderPipelineDiagnosticsState? diagnostics)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                    Diagnostics = diagnostics,
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
        IEnumerable<RenderResource>? resources = null)
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

        public int ExecuteCount => Probe.Count;
    }

    /// <summary>Channel 1: state holds a reference whose contents change between frames.</summary>
    private sealed class MutableStateHolderNode : ProbedRenderNode
    {
        public ColorBox Box { get; } = new() { Color = Colors.Red };

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = Describe(
                (Box, Probe),
                static (session, state) =>
                {
                    state.Probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(state.Box.Color));
                    session.Publish(output);
                });
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
                Probe,
                static (session, probe) =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(Color));
                    session.Publish(output);
                });
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
                (_source, Probe),
                static (session, state) =>
                {
                    state.Probe.Record();
                    Color color = state._source.Read();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(color));
                    session.Publish(output);
                });
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
                context.Borrow(Payload, cacheKey: s_pinnedCacheKey, version: 1);
            OpaqueRenderDescription description = Describe(
                Probe,
                static (session, probe) =>
                {
                    probe.Record();
                    session.UseDeclaredResource<ColorBox>(0, payload =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                        output.Canvas.Use(canvas => canvas.Clear(payload.Color));
                        session.Publish(output);
                    });
                },
                resources: [resource]);
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
            RenderResource<Func<Color>> sink = context.Borrow(_readColor, cacheKey: s_declaredIdentity);
            OpaqueRenderDescription description = Describe(
                Probe,
                static (session, probe) =>
                {
                    probe.Record();
                    session.UseDeclaredResource<Func<Color>>(0, readColor =>
                    {
                        Color color = readColor();
                        using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                        output.Canvas.Use(canvas => canvas.Clear(color));
                        session.Publish(output);
                    });
                },
                resources: [sink]);
            context.Publish(context.ContributeValues(context.OpaqueCombine([], description)));
        }
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
