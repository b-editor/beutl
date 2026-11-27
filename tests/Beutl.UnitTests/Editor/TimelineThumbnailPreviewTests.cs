using Avalonia;
using Avalonia.Controls;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Composition;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

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

    [TestCase(200, 0, false, 0, 3)]
    [TestCase(300, 0, true, 0, 6)]
    [TestCase(200, 100, true, -2, 2)]
    [TestCase(200, -100, true, 2, 6)]
    public void TimelineSlotRange_KeepsOriginalScaleDuringPreview(
        double width, double offset, bool preview, int start, int end)
    {
        var control = new BoundsControl(width, height: 25);

        Assert.That(TimelineSlotRange.Compute(control, scrollViewer: null, count: 4, buffer: 0,
            contentWidth: 200, contentOffsetX: offset, allowOutOfRange: preview), Is.EqualTo((start, end)));
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
    public void SourceVideo_TryComputePreviewSourcePosition_AppliesExplicitOffsetDeltaForAnimatedSpeedSlip()
    {
        var decoders = TestMediaHelper.CreateRegistry();
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(64, 36, new Rational(30, 1), 300))); // 10s
        var speed = new KeyFrameAnimation<float> { UseGlobalClock = { Value = false } };
        speed.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.Zero },
            Value = { Value = 100 },
            Easing = { Value = new LinearEasing() }
        });
        speed.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(2) },
            Value = { Value = 300 },
            Easing = { Value = new LinearEasing() }
        });
        var video = new SourceVideo
        {
            Source = { Value = videoSource },
            OffsetPosition = { Value = MediaTime.FromSeconds(1) },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)) },
        };
        video.Speed.SetKeyFrameAnimation(speed);

        using var resource = (SourceVideo.Resource)video.ToResource(new CompositionContext(MediaTime.Zero) { Decoders = decoders });
        // At t = 1s on linear 100% -> 300% over 2s, f(1) = integral_0^1 (1 + u) du = 1.5s.
        // Adding a fixed slip source offset delta of +0.75s yields Offset(1) + f(1)(1.5) + 0.75 = 3.25s.
        bool ok = video.TryComputePreviewSourcePosition(
            resource,
            MediaTime.FromSeconds(1),
            out MediaTime sourcePos,
            offsetDelta: MediaTime.FromSeconds(0.75));

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(sourcePos.ToSeconds(), Is.EqualTo(3.25).Within(0.02));
        });
    }

    [Test]
    public void SourceVideo_PreviewAfterStartChangeUsesTheProspectiveRange()
    {
        var decoders = TestMediaHelper.CreateRegistry();
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(64, 36, new Rational(30, 1), 300)));
        var nextSource = new VideoSource();
        nextSource.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(64, 36, new Rational(30, 1), 240)));
        var video = new SourceVideo
        {
            Source = { Value = source },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2)) },
        };
        new TestHierarchicalRoot().HierarchicalChildren.Add(video);
        var sources = new KeyFrameAnimation<VideoSource?> { UseGlobalClock = { Value = true } };
        sources.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = source } });
        sources.KeyFrames.Add(new KeyFrame<VideoSource?> { KeyTime = { Value = MediaTime.FromSeconds(2.5) }, Value = { Value = nextSource } });
        video.Source.SetKeyFrameAnimation(sources);
        video.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(Start + 2 * Time + Duration / 10)");

        var loop = new KeyFrameAnimation<bool>();
        loop.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = false } });
        loop.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.FromSeconds(1.5) }, Value = { Value = true } });
        video.IsLoop.SetKeyFrameAnimation(loop);

        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = true } };
        animation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(4) },
            Value = { Value = 300 },
            Easing = { Value = new LinearEasing() },
        });
        video.Speed.SetKeyFrameAnimation(animation);

        MediaTime relativeTime = MediaTime.FromSeconds(1);
        MediaTime startDelta = MediaTime.FromSeconds(0.5);
        var conditions = CompositionConditions.Default with { Decoders = decoders };
        string? originalKey = video.GetThumbnailsCacheKey();
        using var original = video.ToResource(video.CreateThumbnailContext(relativeTime, MediaTime.Zero, conditions));
        using var previewResource = video.ToResource(video.CreateThumbnailContext(relativeTime, startDelta, conditions, durationDelta: -startDelta));
        Assert.That(video.TryComputePreviewSourcePosition(previewResource, relativeTime, out var preview, startDelta: startDelta), Is.True);

        video.TimeRange.Value = new TimeRange(video.Start + startDelta, video.Duration - startDelta);
        using var committed = video.ToResource(video.CreateThumbnailContext(relativeTime, MediaTime.Zero, conditions));
        Assert.Multiple(() =>
        {
            Assert.That(original.Source?.GetOriginal(), Is.SameAs(source));
            Assert.That(previewResource.Source?.GetOriginal(), Is.SameAs(nextSource));
            Assert.That(previewResource.Source?.GetOriginal(), Is.SameAs(committed.Source?.GetOriginal()));
            Assert.That(previewResource.OffsetPosition, Is.EqualTo(committed.OffsetPosition));
            Assert.That(previewResource.OffsetPosition, Is.Not.EqualTo(original.OffsetPosition));
            Assert.That(committed.IsLoop, Is.False);
            Assert.That(previewResource.IsLoop, Is.EqualTo(committed.IsLoop));
            Assert.That(preview, Is.EqualTo(committed.RequestedPosition + committed.OffsetPosition));
            Assert.That(preview, Is.Not.EqualTo(original.RequestedPosition + original.OffsetPosition));
            Assert.That(video.GetThumbnailsCacheKey(), Is.Not.EqualTo(originalKey));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task VideoThumbnailPreview_ContinuesAfterAnEmptyInPoint()
    {
        TestGpu.RequireAnyDevice();
        var decoders = TestMediaHelper.CreateRegistry();
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(64, 36, new Rational(30, 1), 180)));
        var video = new SourceVideo
        {
            Source = { Value = source },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2)) },
        };
        new TestHierarchicalRoot().HierarchicalChildren.Add(video);
        video.Source.Expression = Expression.Create<VideoSource?>("Start + Time >= 1.5 && Start + Time < 2 ? null : Value");
        MediaTime delta = MediaTime.FromSeconds(0.5);
        var conditions = CompositionConditions.Default with { Decoders = decoders };
        using var atStart = video.ToResource(video.CreateThumbnailContext(MediaTime.Zero, delta, conditions, -delta));
        Assert.That(atStart.Source, Is.Null);

        var slots = new List<(int Index, int Count, bool HasThumbnail)>();
        await foreach (var (index, count, thumbnail) in video.GetPreviewThumbnailStripAsync(
                           TestFonts.Manager, count: 4, maxHeight: 25, indices: [0, 1], cacheService: null,
                           cancellationToken: default, preferProxy: false, preferredProxyPreset: ProxyPreset.Quarter,
                           decoders, offsetDelta: MediaTime.Zero, previewLayout: new TimeRange(MediaTime.Zero, video.Duration),
                           startDelta: delta, durationDelta: -delta))
        {
            using (thumbnail)
                slots.Add((index, count, thumbnail != null));
        }

        Assert.That(slots, Is.EqualTo(new[] { (0, 4, false), (1, 4, true) }));
    }

    [TestCase("none")]
    [TestCase("channel map")]
    [TestCase("effect")]
    public async Task PreviewWaveform_ProcessesSourceInsideItsBounds(string processing)
    {
        var decoders = TestMediaHelper.CreateRegistry();
        var source = SoundSource.Open(TestMediaHelper.CreateTestAudioFile(durationSeconds: 3));
        var sound = new SourceSound
        {
            Source = { Value = source },
            OffsetPosition = { Value = MediaTime.FromSeconds(1) },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)) },
        };
        if (processing == "channel map")
            sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.Stereo, [-1, -1]);
        if (processing == "effect")
            sound.Effect.Value = new RingModulatorEffect { Frequency = { Value = 0 } };

        // Relative [-1.5, -0.5, 1.5, 2.5]s reads source [-0.5, 0.5, 2.5, 3.5]s.
        int[] requested = [-15, -5, 15, 25];
        var peaks = new Dictionary<int, float>();
        await foreach (var chunk in sound.GetPreviewWaveformChunksAsync(10, 4096, requested, decoders: decoders))
        {
            peaks[chunk.Index] = chunk.MaxValue - chunk.MinValue;
        }

        Assert.Multiple(() =>
        {
            Assert.That(peaks.Keys, Is.EquivalentTo(requested));
            Assert.That(peaks[-15], Is.Zero);
            Assert.That(peaks[25], Is.Zero);
            foreach (int index in new[] { -5, 15 })
            {
                if (processing == "none")
                    Assert.That(peaks[index], Is.GreaterThan(0.01f));
                else
                    Assert.That(peaks[index], Is.Zero);
            }
        });
    }

    [TestCase(0f, 800, null)]
    [TestCase(50f, 800, null)]
    [TestCase(0f, 400, null)]
    [TestCase(0f, 4096, "expression")]
    [TestCase(0f, 4096, "keyframes")]
    public async Task ScrolledSlipWaveform_MatchesTheCommittedEffectHistory(
        float feedback, int samplesPerChunk, string? speedAnimation)
    {
        var decoders = TestMediaHelper.CreateRegistry();
        var sound = new SourceSound
        {
            Source = { Value = SoundSource.Open(TestMediaHelper.CreateTestAudioFile(sampleRate: 8000, durationSeconds: 4)) },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2)) },
            Effect =
            {
                Value = new DelayEffect
                {
                    DelayTime = { Value = 500 },
                    Feedback = { Value = feedback },
                    DryMix = { Value = 0 },
                    WetMix = { Value = 100 },
                },
            },
        };
        if (speedAnimation == "expression")
        {
            new TestHierarchicalRoot().HierarchicalChildren.Add(sound);
            // The larger warm-up block starts while the source is playing, then crosses into the frozen segment.
            sound.Speed.Expression = Expression.Create<float>("Time < 0.15 ? 100 : 0");
        }
        else if (speedAnimation == "keyframes")
        {
            var speed = new KeyFrameAnimation<float>();
            speed.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 50f } });
            speed.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.FromSeconds(0.65) },
                Value = { Value = 200f },
                Easing = { Value = new LinearEasing() },
            });
            sound.Speed.SetKeyFrameAnimation(speed);
        }

        MediaTime delta = MediaTime.FromSeconds(0.25);
        // Narrow slots expose differences between per-slot and batched variable-speed resampling.
        int chunkCount = speedAnimation == "keyframes" ? 200 : 20;
        int[] requested = speedAnimation == "keyframes" ? [60, 61, 123, 161] : [6, 7, 12, 16];
        var preview = new List<WaveformChunk>();
        await foreach (var chunk in sound.GetPreviewWaveformChunksAsync(
                           chunkCount, samplesPerChunk, requested, decoders: decoders, offsetDelta: delta))
        {
            preview.Add(chunk);
        }

        sound.OffsetPosition.Value += delta;
        var committed = new List<WaveformChunk>();
        await foreach (var chunk in sound.GetWaveformChunksAsync(chunkCount, samplesPerChunk, null, decoders: decoders))
        {
            if (requested.Contains(chunk.Index))
                committed.Add(chunk);
        }

        Assert.That(preview.Select(chunk => chunk.Index), Is.EqualTo(requested));
        Assert.That(committed.Select(chunk => chunk.Index), Is.EqualTo(requested));
        Assert.That(committed[0].MaxValue, samplesPerChunk >= 800 ? Is.GreaterThan(0.1f) : Is.Zero);
        for (int i = 0; i < requested.Length; i++)
        {
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"Chunk {requested[i]} preview {preview[i].MinValue:F4} {preview[i].MaxValue:F4} committed {committed[i].MinValue:F4} {committed[i].MaxValue:F4}"));
        }

        Assert.Multiple(() =>
        {
            for (int i = 0; i < requested.Length; i++)
            {
                Assert.That(preview[i].MinValue, Is.EqualTo(committed[i].MinValue).Within(1e-5), $"Minimum of chunk {requested[i]}");
                Assert.That(preview[i].MaxValue, Is.EqualTo(committed[i].MaxValue).Within(1e-5), $"Maximum of chunk {requested[i]}");
            }
        });
    }
}
