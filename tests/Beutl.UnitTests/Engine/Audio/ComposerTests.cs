using System.Collections.Immutable;
using System.Reflection;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Audio;

public class ComposerTests
{
    [Test]
    public void Compose_EmptyFrame_ReturnsSilentBufferWithCeilingSampleCount()
    {
        // Composer.BuildFinalOutput の silence fallback (mixedBuffer == null) が
        // 他経路 (AudioProcessContext.GetSampleCount) と同じ Ceiling サンプル数で
        // バッファを確保することを担保する回帰テスト。
        // 226 ticks は 1 サンプル境界 (~226.7575 ticks @ 44100Hz) を下回るが、+1 した
        // 227 ticks は境界をわずかに越えるため、truncation だと 1 サンプル / Ceiling だと 2 サンプル。
        const int sampleRate = 44100;
        var oneSampleTicksFloor = TimeSpan.TicksPerSecond / sampleRate;
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromTicks(oneSampleTicksFloor + 1));
        var frame = new CompositionFrame(ImmutableArray<EngineObject.Resource>.Empty, range, default);

        using var composer = new Composer { SampleRate = sampleRate };
        using AudioBuffer? buffer = composer.Compose(range, frame);

        Assert.That(buffer, Is.Not.Null);
        Assert.That(buffer!.SampleRate, Is.EqualTo(sampleRate));
        Assert.That(buffer.ChannelCount, Is.EqualTo(2));
        Assert.That(buffer.SampleCount, Is.EqualTo(AudioProcessContext.GetSampleCount(range, sampleRate)));
        Assert.That(buffer.SampleCount, Is.EqualTo(2));
    }

    [Test]
    public void Compose_EmptyFrame_IntegerSecondDuration_MatchesGetSampleCount()
    {
        const int sampleRate = 48000;
        var range = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        var frame = new CompositionFrame(ImmutableArray<EngineObject.Resource>.Empty, range, default);

        using var composer = new Composer { SampleRate = sampleRate };
        using AudioBuffer? buffer = composer.Compose(range, frame);

        Assert.That(buffer, Is.Not.Null);
        // ヘルパー経由で取得した値とそのまま比較し、Composer の silence fallback がヘルパーと
        // 乖離した場合 (整数秒入力でも) に検出できるようにする。
        Assert.That(buffer!.SampleCount, Is.EqualTo(AudioProcessContext.GetSampleCount(range, sampleRate)));
    }

    [Test]
    public void Compose_RejectsFrameFromDifferentRenderIntent()
    {
        var frame = new CompositionFrame(
            [], default, default, RenderIntent.Preview, RenderPullPurpose.Frame);
        using var composer = new Composer(RenderIntent.Delivery);

        ArgumentException? error = Assert.Throws<ArgumentException>(
            () => composer.Compose(default, frame));

        Assert.That(error!.ParamName, Is.EqualTo("frame"));
    }

    [Test]
    public void Compose_SeparatesFrameAndAuxiliaryNodeCaches()
    {
        var sound = new ComposerPolicyProbeSound();
        using var frameResource = (Sound.Resource)sound.ToResource(new CompositionContext(
            TimeSpan.Zero, RenderIntent.Preview, RenderPullPurpose.Frame));
        using var auxiliaryResource = (Sound.Resource)sound.ToResource(new CompositionContext(
            TimeSpan.Zero, RenderIntent.Preview, RenderPullPurpose.Auxiliary));
        var frame = new CompositionFrame(
            [frameResource], default, default, RenderIntent.Preview, RenderPullPurpose.Frame);
        var auxiliary = new CompositionFrame(
            [auxiliaryResource], default, default, RenderIntent.Preview, RenderPullPurpose.Auxiliary);
        using var composer = new Composer(RenderIntent.Preview);

        using AudioBuffer? frameBuffer = composer.Compose(default, frame);
        using AudioBuffer? auxiliaryBuffer = composer.Compose(default, auxiliary);
        using AudioBuffer? repeatedAuxiliaryBuffer = composer.Compose(default, auxiliary);

        Assert.Multiple(() =>
        {
            Assert.That(frameResource.Version, Is.EqualTo(auxiliaryResource.Version),
                "the regression requires equal-version resources with distinct policy provenance");
            Assert.That(sound.ComposeCount, Is.EqualTo(2),
                "each pull purpose must build its own graph, while a repeated same-purpose pull reuses it");
            Assert.That(sound.ObservedPurposes,
                Is.EqualTo(new[] { RenderPullPurpose.Frame, RenderPullPurpose.Auxiliary }));
        });
    }

    [Test]
    public void Compose_WhenGraphBuildFails_EvictsPartialGraphAndRetryBuildsFreshNodes()
    {
        var composeFailure = new InvalidOperationException("compose failure");
        var cleanupFailure = new InvalidOperationException("cleanup failure");
        var sound = new FaultingComposerSound
        {
            ComposeFailure = composeFailure,
            FirstNodeCleanupFailure = cleanupFailure,
        };
        using var resource = (Sound.Resource)sound.ToResource(new CompositionContext(
            TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
        var frame = new CompositionFrame(
            [resource], default, default, RenderIntent.Delivery, RenderPullPurpose.Frame);
        var composer = new Composer(RenderIntent.Delivery);

        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => composer.Compose(default, frame));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(composeFailure),
                    "partial-graph cleanup must not replace the composition callback failure");
                Assert.That(sound.CreatedNodeCount, Is.EqualTo(2));
                Assert.That(sound.DisposedNodeCount, Is.EqualTo(2),
                    "every old/new node owned by the failed graph must be swept");
            });

            sound.ComposeFailure = null;
            sound.FirstNodeCleanupFailure = null;
            using AudioBuffer? buffer = composer.Compose(default, frame);

            Assert.Multiple(() =>
            {
                Assert.That(buffer, Is.Not.Null);
                Assert.That(sound.ComposeCount, Is.EqualTo(2));
                Assert.That(sound.CreatedNodeCount, Is.EqualTo(4),
                    "retry must not reuse nodes mutated by the failed differential update");
            });
        }
        finally
        {
            Assert.DoesNotThrow(composer.Dispose);
        }

        Assert.That(sound.DisposedNodeCount, Is.EqualTo(4));
    }

    [Test]
    public void Compose_WhenEndUpdateCleanupFails_DisposesEachOwnedNodeOnce()
    {
        var cleanupFailure = new InvalidOperationException("cleanup failure");
        var sound = new FaultingComposerSound
        {
            FirstNodeCleanupFailure = cleanupFailure,
        };
        using var firstResource = (Sound.Resource)sound.ToResource(new CompositionContext(
            TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
        using var secondResource = (Sound.Resource)sound.ToResource(new CompositionContext(
            TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
        var firstFrame = new CompositionFrame(
            [firstResource], default, default, RenderIntent.Delivery, RenderPullPurpose.Frame);
        var secondFrame = new CompositionFrame(
            [secondResource], default, default, RenderIntent.Delivery, RenderPullPurpose.Frame);
        var composer = new Composer(RenderIntent.Delivery);

        using AudioBuffer? firstBuffer = composer.Compose(default, firstFrame);
        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => composer.Compose(default, secondFrame));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(sound.CreatedNodeCount, Is.EqualTo(4));
            Assert.That(sound.DisposedNodeCount, Is.EqualTo(4),
                "the retired and replacement graphs must each be swept exactly once");
        });

        Assert.DoesNotThrow(composer.Dispose);
        Assert.That(sound.DisposedNodeCount, Is.EqualTo(4),
            "the failed cache entry must not retain aliases to nodes already owned and swept by the build context");
    }

    [Test]
    public void Dispose_PublishesIsDisposedAfterVirtualCleanupEvenWhenCleanupThrows()
    {
        var cleanupFailure = new InvalidOperationException("composer cleanup failure");
        var composer = new CompatibilityComposer(cleanupFailure);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(composer.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(composer.IsDisposedDuringCleanup, Is.False,
                "existing overrides may guard their cleanup with IsDisposed");
            Assert.That(composer.CacheInvalidationFailure, Is.Null,
                "public operations historically available from the cleanup hook must remain available");
            Assert.That(composer.IsDisposed, Is.True);
            Assert.That(composer.DisposeCalls, Is.EqualTo(1));
            Assert.DoesNotThrow(composer.Dispose);
            Assert.That(composer.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Finalizer_PublishesIsDisposedAfterVirtualCleanup()
    {
        var composer = new CompatibilityComposer();
        MethodInfo finalizer = typeof(Composer).GetMethod(
            "Finalize",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            Assert.DoesNotThrow(() => finalizer.Invoke(composer, null));
            Assert.Multiple(() =>
            {
                Assert.That(composer.IsDisposedDuringCleanup, Is.False);
                Assert.That(composer.LastDisposing, Is.False);
                Assert.That(composer.IsDisposed, Is.True);
                Assert.That(composer.DisposeCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            GC.SuppressFinalize(composer);
        }
    }

    [Test]
    public void DisposedComposer_RejectsComposeAndCacheInvalidation()
    {
        using var resource = new EngineObject.Resource();
        var frame = new CompositionFrame(
            [resource], default, default, RenderIntent.Preview, RenderPullPurpose.Frame);
        var composer = new Composer();
        composer.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => composer.Compose(default, frame));
            Assert.Throws<ObjectDisposedException>(composer.InvalidateCache);
        });
    }

    [Test]
    public void Compose_WhenNodeProcessThrows_RetryBuildsFreshGraph()
    {
        var processFailure = new InvalidOperationException("process failure");
        var sound = new ThrowOnceProcessSound(processFailure);
        using var resource = (Sound.Resource)sound.ToResource(new CompositionContext(
            TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
        var frame = new CompositionFrame(
            [resource], default, default, RenderIntent.Delivery, RenderPullPurpose.Frame);
        var composer = new Composer(RenderIntent.Delivery);

        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => composer.Compose(default, frame));
            using AudioBuffer? retry = composer.Compose(default, frame);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(processFailure));
                Assert.That(retry, Is.Not.Null);
                Assert.That(sound.CreatedNodeCount, Is.EqualTo(2),
                    "a process-mutated node must not be reused after a failed frame");
                Assert.That(sound.DisposedNodeCount, Is.EqualTo(1));
            });
        }
        finally
        {
            composer.Dispose();
        }

        Assert.That(sound.DisposedNodeCount, Is.EqualTo(2));
    }

    private sealed class CompatibilityComposer(Exception? cleanupFailure = null) : Composer
    {
        public bool? IsDisposedDuringCleanup { get; private set; }

        public bool? LastDisposing { get; private set; }

        public Exception? CacheInvalidationFailure { get; private set; }

        public int DisposeCalls { get; private set; }

        protected override void OnDispose(bool disposing)
        {
            IsDisposedDuringCleanup = IsDisposed;
            LastDisposing = disposing;
            if (IsDisposed)
                return;

            DisposeCalls++;
            try
            {
                InvalidateCache();
            }
            catch (Exception ex)
            {
                CacheInvalidationFailure = ex;
            }

            base.OnDispose(disposing);
            if (cleanupFailure != null)
                throw cleanupFailure;
        }
    }
}

internal sealed partial class ComposerPolicyProbeSound : Sound
{
    public int ComposeCount { get; private set; }

    public List<RenderPullPurpose> ObservedPurposes { get; } = [];

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        ComposeCount++;
        ObservedPurposes.Add(((Resource)resource).PullPurpose);
        context.Clear();
    }

    public partial class Resource
    {
        public RenderPullPurpose PullPurpose { get; private set; }

        public override SoundSource.Resource? GetSoundSource() => null;

        partial void PostUpdate(ComposerPolicyProbeSound obj, CompositionContext context)
        {
            PullPurpose = context.PullPurpose;
        }
    }
}

internal sealed partial class FaultingComposerSound : Sound
{
    public Exception? ComposeFailure { get; set; }

    public Exception? FirstNodeCleanupFailure { get; set; }

    public int ComposeCount { get; private set; }

    public int CreatedNodeCount { get; private set; }

    public int DisposedNodeCount { get; private set; }

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        ComposeCount++;
        var first = context.AddNode(CreateNode(FirstNodeCleanupFailure));
        var second = context.AddNode(CreateNode(null));
        context.Connect(first, second);
        context.MarkAsOutput(second);
        if (ComposeFailure != null)
        {
            throw ComposeFailure;
        }
    }

    private AudioNode CreateNode(Exception? cleanupFailure)
    {
        CreatedNodeCount++;
        return new TrackingAudioNode(this, cleanupFailure);
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }

    private sealed class TrackingAudioNode(
        FaultingComposerSound owner,
        Exception? cleanupFailure) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
            => new(context.SampleRate, 2, context.GetSampleCount());

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                owner.DisposedNodeCount++;
                if (cleanupFailure != null)
                {
                    throw cleanupFailure;
                }
            }
        }
    }
}

internal sealed partial class ThrowOnceProcessSound(Exception processFailure) : Sound
{
    private bool _hasThrown;

    public int CreatedNodeCount { get; private set; }

    public int DisposedNodeCount { get; private set; }

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        CreatedNodeCount++;
        var node = context.AddNode(new ThrowOnceNode(this, processFailure));
        context.MarkAsOutput(node);
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }

    private sealed class ThrowOnceNode(
        ThrowOnceProcessSound owner,
        Exception failure) : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            if (!owner._hasThrown)
            {
                owner._hasThrown = true;
                throw failure;
            }

            return new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                owner.DisposedNodeCount++;
            }
        }
    }
}
