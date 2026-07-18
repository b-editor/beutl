using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class CompositionPublicApiTests
{
    [Test]
    public void LegacyFrameOnlyCompositor_RemainsImplementableWhileAuxiliaryFallbackIsExplicit()
    {
        ICompositor compositor = new PublicFrameOnlyCompositor();

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => compositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Frame));
            Assert.DoesNotThrow(() => compositor.EvaluateAudio(default, RenderPullPurpose.Frame));
            Assert.Throws<NotSupportedException>(() =>
                compositor.EvaluateGraphics(TimeSpan.Zero, RenderPullPurpose.Auxiliary));
            Assert.Throws<NotSupportedException>(() =>
                compositor.EvaluateAudio(default, RenderPullPurpose.Auxiliary));
        });
    }

    [Test]
    public void CompositionFrame_PurposeProvenancePreservesThePublishedThreeValueShape()
    {
        var frame = new CompositionFrame(
            ImmutableArray<EngineObject.Resource>.Empty,
            default,
            default,
            RenderIntent.Delivery,
            RenderPullPurpose.Auxiliary);
        var (objects, time, size) = frame;

        Assert.Multiple(() =>
        {
            Assert.That(frame.RenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(frame.PullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.That(objects, Is.EqualTo(frame.Objects));
            Assert.That(time, Is.EqualTo(frame.Time));
            Assert.That(size, Is.EqualTo(frame.Size));
            Assert.That(
                typeof(IRenderer).GetMethod(
                    nameof(IRenderer.GetBoundary),
                    [typeof(CompositionFrame), typeof(Beutl.Graphics.Drawable)]),
                Is.Not.Null);
            Assert.That(
                typeof(IRenderer).GetMethod(
                    nameof(IRenderer.GetBoundaries),
                    [typeof(CompositionFrame), typeof(int)]),
                Is.Not.Null);
        });
    }

    [Test]
    public void LegacyFrameOnlyRenderer_RejectsAuxiliaryBoundaryFallback()
    {
        IRenderer renderer = new PublicFrameOnlyRenderer();
        var frame = new CompositionFrame(
            [],
            default,
            default,
            RenderIntent.Preview,
            RenderPullPurpose.Auxiliary);

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(() => renderer.GetBoundaries(frame, 0));
            Assert.Throws<NotSupportedException>(() => renderer.GetBoundary(frame, null!));
            Assert.That(((PublicFrameOnlyRenderer)renderer).UpdateFrameCalls, Is.Zero,
                "the legacy frame tree must remain untouched by an unsupported auxiliary pull");
        });
    }

    [Test]
    public void LegacyComposer_RemainsImplementableWithPreviewIntentDefault()
    {
        IComposer composer = new PublicLegacyComposer();

        Assert.Multiple(() =>
        {
            Assert.That(composer.RenderIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(composer.SampleRate, Is.EqualTo(48_000));
            Assert.That(composer.IsDisposed, Is.False);
        });

        composer.Dispose();
        Assert.That(composer.IsDisposed, Is.True);
    }

    private sealed class PublicFrameOnlyCompositor : ICompositor
    {
        public CompositionFrame EvaluateGraphics(TimeSpan time)
            => new(ImmutableArray<EngineObject.Resource>.Empty, new TimeRange(time, TimeSpan.Zero), default);

        public CompositionFrame EvaluateAudio(TimeRange timeRange)
            => new(ImmutableArray<EngineObject.Resource>.Empty, timeRange, default);

        public void Dispose()
        {
        }
    }

    private sealed class PublicFrameOnlyRenderer : IRenderer
    {
        public int UpdateFrameCalls { get; private set; }

        public PixelSize FrameSize => default;

        public TimeSpan Time => default;

        public bool IsDisposed { get; private set; }

        public bool IsGraphicsRendering => false;

        public RenderCacheOptions CacheOptions
        {
            get => throw new NotSupportedException();
            set { }
        }

        public void Render(CompositionFrame frame)
        {
        }

        public Bitmap Snapshot() => throw new NotSupportedException();

        public Drawable? HitTest(CompositionFrame frame, Point point) => null;

        public void UpdateFrame(CompositionFrame frame) => UpdateFrameCalls++;

        public Rect[] GetBoundaries(int zIndex) => [];

        public DrawableRenderNode? FindRenderNode(Drawable drawable) => null;

        public void Dispose() => IsDisposed = true;
    }

    private sealed class PublicLegacyComposer : IComposer
    {
        public bool IsAudioRendering => false;

        public bool IsDisposed { get; private set; }

        public int SampleRate => 48_000;

        public AudioBuffer? Compose(TimeRange range, CompositionFrame frame) => null;

        public void Dispose() => IsDisposed = true;
    }
}
