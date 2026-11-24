using Avalonia;
using Avalonia.Controls;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TimelineThumbnailPreviewTests
{
    private sealed class BoundsControl : Control
    {
        public BoundsControl(double width, double height)
        {
            Arrange(new Avalonia.Rect(0, 0, width, height));
        }
    }

    [Test]
    public void TimelineSlotRange_DefaultMode_ClampsToZeroAndCountMinusOne()
    {
        var control = new BoundsControl(width: 200, height: 25);

        (int start, int end) = TimelineSlotRange.Compute(control, scrollViewer: null, count: 4, buffer: 2);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(0));
            Assert.That(end, Is.EqualTo(3));
        });
    }

    [Test]
    public void TimelineSlotRange_PreviewExtendedRight_RequestsSlotsBeyondCount()
    {
        // Base strip had 4 slots across 200px (50px/slot). Extending width to 300px reveals slots 4 and 5.
        var control = new BoundsControl(width: 300, height: 25);

        (int start, int end) = TimelineSlotRange.Compute(
            control,
            scrollViewer: null,
            count: 4,
            buffer: 0,
            contentWidth: 200,
            contentOffsetX: 0,
            allowOutOfRange: true);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(0));
            Assert.That(end, Is.EqualTo(6));
        });
    }

    [Test]
    public void TimelineSlotRange_PreviewPositiveOffset_RequestsNegativeSlots()
    {
        // Positive contentOffsetX shifts existing slots right (left roll/slide extend or slip left), revealing negative indices.
        var control = new BoundsControl(width: 200, height: 25);

        (int start, int end) = TimelineSlotRange.Compute(
            control,
            scrollViewer: null,
            count: 4,
            buffer: 0,
            contentWidth: 200,
            contentOffsetX: 100,
            allowOutOfRange: true);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(-2));
            Assert.That(end, Is.EqualTo(2));
        });
    }

    [Test]
    public void TimelineSlotRange_PreviewNegativeOffset_ShiftsRangeForward()
    {
        // Negative contentOffsetX shifts existing slots left (right roll/slide trim or slip right), revealing indices >= count.
        var control = new BoundsControl(width: 200, height: 25);

        (int start, int end) = TimelineSlotRange.Compute(
            control,
            scrollViewer: null,
            count: 4,
            buffer: 0,
            contentWidth: 200,
            contentOffsetX: -100,
            allowOutOfRange: true);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(2));
            Assert.That(end, Is.EqualTo(6));
        });
    }

    [Test]
    public void SourceVideo_TryComputePreviewSourcePosition_HandlesNegativeAndExtendedTimesWithoutWrappingNonLoop()
    {
        var decoders = TestMediaHelper.CreateRegistry();
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(64, 36, new Rational(30, 1), 180))); // 6s
        var video = new SourceVideo
        {
            Source = { Value = videoSource },
            OffsetPosition = { Value = MediaTime.FromSeconds(2) },
            Speed = { Value = 100f },
            IsLoop = { Value = false },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2)) },
        };

        using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(MediaTime.FromSeconds(1)) { Decoders = decoders });

        Assert.Multiple(() =>
        {
            Assert.That(video.TryComputePreviewSourcePosition(resource, MediaTime.FromSeconds(-1), out var beforePos), Is.True);
            Assert.That(beforePos, Is.EqualTo(MediaTime.FromSeconds(1)));

            Assert.That(video.TryComputePreviewSourcePosition(resource, MediaTime.FromSeconds(-3), out _), Is.False,
                "Times before source start must report out-of-bounds instead of wrapping to the end.");

            Assert.That(video.TryComputePreviewSourcePosition(resource, MediaTime.FromSeconds(3), out var afterPos), Is.True);
            Assert.That(afterPos, Is.EqualTo(MediaTime.FromSeconds(5)));

            Assert.That(video.TryComputePreviewSourcePosition(resource, MediaTime.FromSeconds(4.5), out _), Is.False,
                "Times past source end must report out-of-bounds when loop is off.");
        });
    }

    [Test]
    public void SourceVideo_TryComputePreviewSourcePosition_ScalesBySpeedAndWrapsWhenLooping()
    {
        var decoders = TestMediaHelper.CreateRegistry();
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(64, 36, new Rational(30, 1), 180))); // 6s
        var video = new SourceVideo
        {
            Source = { Value = videoSource },
            OffsetPosition = { Value = MediaTime.FromSeconds(2) },
            Speed = { Value = 200f },
            IsLoop = { Value = true },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)) },
        };

        using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(MediaTime.Zero) { Decoders = decoders });

        Assert.Multiple(() =>
        {
            // At 200% speed, -0.5s timeline = -1s source -> 2s - 1s = 1s.
            Assert.That(video.TryComputePreviewSourcePosition(resource, MediaTime.FromSeconds(-0.5), out var backPos), Is.True);
            Assert.That(backPos, Is.EqualTo(MediaTime.FromSeconds(1)));

            // At 200% speed, -1.5s timeline = -3s source -> 2s - 3s = -1s -> wraps to 5s on a 6s looping source.
            Assert.That(video.TryComputePreviewSourcePosition(resource, MediaTime.FromSeconds(-1.5), out var wrappedPos), Is.True);
            Assert.That(wrappedPos, Is.EqualTo(MediaTime.FromSeconds(5)));
        });
    }

    [Test]
    public async Task SourceSound_GetPreviewWaveformChunks_ReadsNegativeAndExtendedIndicesAndSilencesOutOfBounds()
    {
        var decoders = TestMediaHelper.CreateRegistry();
        string path = TestMediaHelper.CreateTestAudioFile(sampleRate: 44100, channels: 2, durationSeconds: 3.0);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));
        var sound = new SourceSound
        {
            Source = { Value = soundSource },
            OffsetPosition = { Value = MediaTime.FromSeconds(1) },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)) },
        };
        var cache = new ThumbnailCacheService();
        string? cacheKey = sound.GetThumbnailsCacheKey();
        try
        {
            // baseChunkCount = 10 -> each chunk is 0.1s.
            // Index -15 -> -1.5s relative -> -0.5s source (before source start -> silence).
            // Index -5  -> -0.5s relative ->  0.5s source (in source -> non-zero audio).
            // Index 15  ->  1.5s relative ->  2.5s source (in source -> non-zero audio).
            // Index 25  ->  2.5s relative ->  3.5s source (past source end -> silence).
            int[] requested = [-15, -5, 15, 25];
            var chunks = new Dictionary<int, WaveformChunk>();
            await foreach (var chunk in sound.GetPreviewWaveformChunksAsync(
                               baseChunkCount: 10,
                               samplesPerChunk: 4096,
                               indices: requested,
                               cacheService: cache,
                               decoders: decoders))
            {
                chunks[chunk.Index] = chunk;
            }

            Assert.Multiple(() =>
            {
                Assert.That(chunks.Keys, Is.EquivalentTo(requested));
                Assert.That(chunks[-15].MinValue, Is.Zero);
                Assert.That(chunks[-15].MaxValue, Is.Zero);
                Assert.That(chunks[-5].MaxValue - chunks[-5].MinValue, Is.GreaterThan(0.01f));
                Assert.That(chunks[15].MaxValue - chunks[15].MinValue, Is.GreaterThan(0.01f));
                Assert.That(chunks[25].MinValue, Is.Zero);
                Assert.That(chunks[25].MaxValue, Is.Zero);
            });
        }
        finally
        {
            if (cacheKey != null)
                cache.Invalidate(cacheKey);
        }
    }
}
