using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class SilenceSplitTests
{
    private static MediaTime Seconds(double value) => MediaTime.FromSeconds(value);
    private static TimeRange Range(double start, double end) => TimeRange.FromRange(Seconds(start), Seconds(end));

    [TestCase(0, 100, false, true)]
    [TestCase(0, 100, true, true)]
    [TestCase(28, 100, false, true)]
    [TestCase(28, 200, false, false)]
    [TestCase(30, 100, true, false)]
    [TestCase(40, 100, false, true)]
    public async Task Analysis_ChecksOnlyTheReferencedWindow(double offset, float speed, bool inGroup, bool readable)
    {
        TestMediaHelper.RegisterTestDecoder();
        var source = new SoundSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 40)));
        using var h = new SceneHistoryHarness("silence-reference-window");
        var nested = new Scene(100, 100, "trimmed") { Start = { Value = Seconds(5) }, Duration = { Value = Seconds(40) } };
        var good = new SourceSound { Source = { Value = source }, Gain = { Value = 0 }, TimeRange = { Value = Range(5, 35) } };
        var missing = new SourceSound { Source = { Value = new SoundSource() }, TimeRange = { Value = Range(35, 45) } };
        if (inGroup)
        {
            var group = new SoundGroup();
            group.Children.Add(good);
            group.Children.Add(missing);
            var child = new Element { Start = { Value = Seconds(5) }, Length = { Value = Seconds(40) } };
            child.Objects.Add(group);
            nested.Children.Add(child);
        }
        else
        {
            foreach (var sound in new[] { good, missing })
            {
                var child = new Element { Start = { Value = sound.TimeRange.Value.Start }, Length = { Value = sound.TimeRange.Value.Duration } };
                child.Objects.Add(sound);
                nested.Children.Add(child);
            }
        }
        var element = h.AddElement(Seconds(7), Seconds(2));
        element.Objects.Add(new SceneSound { ReferencedScene = { Value = nested }, OffsetPosition = { Value = Seconds(offset) }, Speed = { Value = speed } });
        using var model = new SilenceSplitViewModel(h.Scene, element, h.History);
        await model.AnalyzeAsync();
        Assert.That(model.CanApply.Value, Is.EqualTo(readable), model.Status.Value);
        Assert.That(model.Ranges.Count, Is.EqualTo(readable ? 1 : 0));
    }

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(true, false)]
    public async Task Analysis_DistinguishesFailedReadsFromEndOfStream(bool nested, bool fail)
    {
        using var h = new SceneHistoryHarness("silence-decode");
        string path = Path.Combine(h.BasePath, "audio.silence-test");
        File.WriteAllBytes(path, []);
        var decoder = new ReadFailureDecoder(path, fail);
        DecoderRegistry.Register(decoder);
        try
        {
            var source = new SoundSource();
            source.ReadFrom(new Uri(path));
            var element = h.AddElement(Seconds(0), Seconds(3));
            var sound = new SourceSound { Source = { Value = source }, TimeRange = { Value = Range(0, 3) } };
            if (nested)
            {
                var scene = new Scene(100, 100, "nested") { Duration = { Value = Seconds(3) } };
                var child = new Element { Length = { Value = Seconds(3) } };
                var group = new SoundGroup();
                group.Children.Add(sound);
                child.Objects.Add(group);
                scene.Children.Add(child);
                element.Objects.Add(new SceneSound { ReferencedScene = { Value = scene } });
            }
            else element.Objects.Add(sound);
            var video = h.AddElement(Seconds(0), Seconds(3), 1);
            h.Scene.Groups.Add([element.Id.Value, video.Id.Value]);
            using var model = new SilenceSplitViewModel(h.Scene, element, h.History);
            await model.AnalyzeAsync();
            Assert.That(decoder.SuccessfulReads, Is.GreaterThan(0));
            Assert.That(decoder.FailedReads > 0, Is.EqualTo(fail));
            Assert.That(model.CanApply.Value, Is.EqualTo(!fail), model.Status.Value);
            Assert.That(model.Ranges.Count, Is.EqualTo(fail ? 0 : 1));
            if (fail)
            {
                model.ModeIndex.Value = (int)SilenceSplitMode.DeleteSilence;
                await model.ApplyAsync();
                Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { element, video }));
            }

            // Normal playback still uses its silent fallback, even after analysis failed.
            using var compositor = new SceneCompositor(h.Scene);
            using var composer = new Composer();
            using var output = composer.Compose(Range(1, 2), compositor.EvaluateAudio(Range(1, 2)));
            Assert.That(output, Is.Not.Null);
            Assert.That(output!.GetChannelData(0).ToArray(), Is.All.Zero);
        }
        finally { DecoderRegistry.Unregister(decoder); }
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public async Task Analysis_DistinguishesOfflineReferencedAudioFromDisabledOrMutedAudio(bool inGroup, bool disabled, bool muted)
    {
        using var h = new SceneHistoryHarness("silence-offline");
        var nested = new Scene(100, 100, "offline") { Duration = { Value = Seconds(2) } };
        var child = new Element { Length = { Value = Seconds(2) } };
        var source = new SoundSource();
        source.ReadFrom(new Uri(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.wav")));
        var missing = new SourceSound { Source = { Value = source }, IsEnabled = { Value = !disabled }, TimeRange = { Value = Range(0, 2) } };
        if (inGroup)
        {
            var group = new SoundGroup();
            group.Children.Add(missing);
            child.Objects.Add(group);
        }
        else child.Objects.Add(missing);
        nested.Children.Add(child);
        nested.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsAudioMuted = { Value = muted } });
        var element = h.AddElement(Seconds(0), Seconds(2));
        element.Objects.Add(new SceneSound { ReferencedScene = { Value = nested } });
        var video = h.AddElement(Seconds(0), Seconds(2), 1);
        h.Scene.Groups.Add([element.Id.Value, video.Id.Value]);
        using var model = new SilenceSplitViewModel(h.Scene, element, h.History);
        await model.AnalyzeAsync();
        Assert.That(model.CanApply.Value, Is.EqualTo(disabled || muted));
        Assert.That(model.Ranges.Count, Is.EqualTo(disabled || muted ? 1 : 0));
    }

    [Test]
    public void Preview_TenHourRecordingCanZoomAndPanWithoutChangingDetection()
    {
        using var h = new SceneHistoryHarness("silence-long");
        var element = h.AddElement(Seconds(0), Seconds(36000));
        using var model = new SilenceSplitViewModel(h.Scene, element, h.History);
        var chunks = Enumerable.Range(0, 1800000).Select(i => new WaveformChunk(i, -0.5f, 0.5f)).ToArray();
        chunks[^1] = new(chunks.Length - 1, -0.8f, 0.9f);
        model.SetWaveform(chunks);
        Assert.That(model.PreviewChunks, Has.Count.EqualTo(1200));
        Assert.That(model.PreviewChunks[^1].MaxValue, Is.EqualTo(0.9f));
        Assert.That(model.Ranges, Is.Empty);
        var ranges = model.Ranges;
        while (model.CanZoomIn) model.ZoomPreview(true);
        model.PanPreview(36000);
        Assert.That(model.PreviewRange, Is.EqualTo(Range(35999, 36000)));
        Assert.That(model.PreviewChunks, Has.Count.EqualTo(50));
        Assert.That(model.PreviewChunks[^1].MaxValue, Is.EqualTo(0.9f));
        model.ZoomPreview(false);
        Assert.That(model.PreviewRange, Is.EqualTo(Range(35998, 36000)));
        model.PanPreview(-1);
        Assert.That(model.PreviewRange, Is.EqualTo(Range(0, 2)));
        model.ResetPreview();
        Assert.That(model.PreviewRange, Is.EqualTo(Range(0, 36000)));
        Assert.That(model.PreviewChunks[^1].MaxValue, Is.EqualTo(0.9f));
        Assert.That(model.Ranges, Is.SameAs(ranges), "navigation does not change the full-clip cut plan");
    }

    [Test]
    public async Task Analysis_ReportsProgressWithoutAllowingPartialApplication()
    {
        using var h = new SceneHistoryHarness("silence-progress");
        var element = h.AddElement(Seconds(0), Seconds(3));
        element.Objects.Add(new SceneSound { ReferencedScene = { Value = new Scene(100, 100, "empty") } });
        using var model = new SilenceSplitViewModel(h.Scene, element, h.History);
        var progress = new List<int>();
        using var subscription = model.Progress.Subscribe(value =>
        {
            progress.Add(value);
            Assert.That(model.CanApply.Value, Is.False);
        });
        await model.AnalyzeAsync();
        Assert.That(progress, Is.Ordered);
        Assert.That(progress, Has.Some.InRange(1, 99));
        Assert.That(progress[^1], Is.EqualTo(100));
        Assert.That(model.CanApply.Value, Is.True);
    }

    [TestCase(4.01, true, true)]
    [TestCase(4.04, true, false)]
    [TestCase(4, true, false)]
    [TestCase(4.01, false, false)]
    public void Apply_ReportsUnsplittableLinkedBoundaryWithoutPartialEdits(double videoEnd, bool linked, bool unsupported)
    {
        using var h = new SceneHistoryHarness("silence-linked-boundary");
        var audio = h.AddElement(Seconds(0), Seconds(10));
        var video = h.AddElement(Seconds(0), Seconds(videoEnd), 1);
        h.Scene.Groups.Add([audio.Id.Value, video.Id.Value]);
        h.History.Commit("Setup");
        int before = h.History.UndoCount;
        var service = new SilenceSplitService(h.History);
        if (unsupported)
        {
            Assert.Throws<InvalidOperationException>(() => service.Apply(h.Scene, audio, [Range(3, 4)], SilenceSplitMode.RippleDelete, linked));
            Assert.That(h.History.UndoCount, Is.EqualTo(before));
            Assert.That(h.History.HasPendingOperations, Is.False);
            Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { audio, video }));
            Assert.That(audio.Length.Value, Is.EqualTo(Seconds(10)));
            Assert.That(video.Length.Value, Is.EqualTo(Seconds(videoEnd)));
        }
        else
        {
            service.Apply(h.Scene, audio, [Range(3, 4)], SilenceSplitMode.RippleDelete, linked);
            Assert.That(h.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(video.Length.Value, Is.EqualTo(Seconds(linked ? 3 : videoEnd)));
        }
    }

    [TestCase(1, 1000, true, false)]
    [TestCase(100, 0, true, true)]
    [TestCase(100, 0, false, false)]
    public async Task Analysis_UsesTheElementsComposedAudio(float gain, float groupGain, bool groupEnabled, bool silent)
    {
        TestMediaHelper.RegisterTestDecoder();
        string path = TestMediaHelper.CreateTestAudioFile();
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        using var h = new SceneHistoryHarness("silence-flow");
        var element = h.AddElement(Seconds(0), Seconds(2));
        element.Objects.Add(new SourceSound { Source = { Value = source }, Gain = { Value = gain } });
        element.Objects.Add(new SoundGroup { Gain = { Value = groupGain }, IsEnabled = { Value = groupEnabled } });
        h.AddElement(Seconds(0), Seconds(2), 1).Objects.Add(new SourceSound { Source = { Value = source } });
        using var model = new SilenceSplitViewModel(h.Scene, element, h.History);
        await model.AnalyzeAsync();
        Assert.That(model.Ranges.Count, Is.EqualTo(silent ? 1 : 0));
    }

    [TestCase(3.5)]
    [TestCase(6)]
    public void Ripple_KeepsStaggeredLinkedElementsInSync(double videoStart)
    {
        using var h = new SceneHistoryHarness("silence-ripple-sync");
        var audio = h.AddElement(Seconds(0), Seconds(10));
        var video = h.AddElement(Seconds(videoStart), Seconds(10 - videoStart), 1);
        h.Scene.Groups.Add([audio.Id.Value, video.Id.Value]);
        h.History.Commit("Setup");
        new SilenceSplitService(h.History).Apply(h.Scene, audio,
            SilenceSplitService.Plan(h.Scene, audio, [Range(3, 4)]), SilenceSplitMode.RippleDelete, true);
        var lastAudio = h.Scene.Children.Where(e => e.ZIndex.Value == 0).OrderBy(e => e.Start.Value).Last();
        var lastVideo = h.Scene.Children.Where(e => e.ZIndex.Value == 1).OrderBy(e => e.Start.Value).Last();
        Assert.That(lastVideo.Range.End, Is.EqualTo(lastAudio.Range.End));
        h.History.Undo();
        Assert.That(video.Start.Value, Is.EqualTo(Seconds(videoStart)));
        Assert.That(video.Range.End, Is.EqualTo(Seconds(10)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Ripple_PreservesOtherContentAndUsesTheSameLockBoundaryAcrossLayers(bool locked)
    {
        using var h = new SceneHistoryHarness("silence-ripple-content");
        var audio = h.AddElement(Seconds(0), Seconds(10));
        var video = h.AddElement(Seconds(3.5), Seconds(6.5), 1);
        var other = h.AddElement(Seconds(3.2), Seconds(0.3), 1);
        other.IsLocked.Value = locked;
        h.Scene.Groups.Add([audio.Id.Value, video.Id.Value]);
        h.History.Commit("Setup");
        new SilenceSplitService(h.History).Apply(h.Scene, audio, [Range(3, 4)], SilenceSplitMode.RippleDelete, true);
        var lastAudio = h.Scene.Children.Where(e => e.ZIndex.Value == 0).OrderBy(e => e.Start.Value).Last();
        var lastVideo = h.Scene.Children.Where(e => e.ZIndex.Value == 1).OrderBy(e => e.Start.Value).Last();
        Assert.That(lastVideo.Range.End, Is.EqualTo(lastAudio.Range.End));
        Assert.That(lastVideo.Start.Value, Is.EqualTo(other.Range.End));
        Assert.That(other.Start.Value, Is.EqualTo(Seconds(locked ? 3.2 : 3)));
        h.History.Undo();
        Assert.That(other.Start.Value, Is.EqualTo(Seconds(3.2)));
        Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { audio, video, other }));
    }

    [Test]
    public void Detection_ProtectsSpeechAndClipEdges_AndTreatsMissingBinsAsUnknown()
    {
        float[] peaks = [0, 0, 0.5f, 0, 0, 0, 0, 0.5f, 0, 0];
        var chunks = peaks.Select((peak, i) => new WaveformChunk(i, -peak, peak)).ToArray();
        Assert.That(SilenceDetector.Detect(chunks, Seconds(1), -40, Seconds(0.2), Seconds(0.1)),
            Is.EqualTo(new[] { Range(0, 0.1), Range(0.4, 0.6), Range(0.9, 1) }));
        chunks[4] = new WaveformChunk(4, float.NaN, float.NaN);
        Assert.That(SilenceDetector.Detect(chunks, Seconds(1), -40, Seconds(0.3), Seconds(0)), Is.Empty);
    }

    [Test]
    public void Detection_UsesDecibelsAndMinimumBeforePadding_AndRetainsBriefPeaks()
    {
        var chunks = Enumerable.Range(0, 50).Select(i => new WaveformChunk(i, -0.005f, 0.005f)).ToArray();
        Assert.That(SilenceDetector.Detect(chunks, Seconds(1), -50, Seconds(0.3), Seconds(0.1)), Is.Empty);
        Assert.That(SilenceDetector.Detect(chunks, Seconds(1), -40, Seconds(0.3), Seconds(0.1)), Is.EqualTo(new[] { Range(0, 1) }));
        chunks[25] = new WaveformChunk(25, 0, 0.1f);
        Assert.That(SilenceDetector.Detect(chunks, Seconds(1), -40, Seconds(0.3), Seconds(0.1)),
            Is.EqualTo(new[] { Range(0, 0.4), Range(0.62, 1) }));
    }

    [Test]
    public void ThirtyMinuteEnvelope_RecalculatesWithoutReadingAudio()
    {
        var chunks = Enumerable.Range(0, 90000).Select(i => new WaveformChunk(i, 0, i % 500 < 100 ? 0 : 0.5f)).ToArray();
        var watch = Stopwatch.StartNew();
        var result = SilenceDetector.Detect(chunks, Seconds(1800), -40, Seconds(0.3), Seconds(0.1));
        watch.Stop();
        TestContext.Out.WriteLine($"30 minute envelope: {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.That(result, Has.Count.EqualTo(180));
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
    }

    [TestCase(SilenceSplitMode.SplitOnly, true, 10)]
    [TestCase(SilenceSplitMode.DeleteSilence, true, 6)]
    [TestCase(SilenceSplitMode.RippleDelete, true, 6)]
    [TestCase(SilenceSplitMode.RippleDelete, false, 4)]
    public void Apply_CutsLinkedClipsAtAbsoluteTimes_PreservesOffsets_AndUndoesOnce(SilenceSplitMode mode, bool linked, int parts)
    {
        using var h = new SceneHistoryHarness("silence", duration: Seconds(60));
        Element audio = h.AddElement(Seconds(10), Seconds(10));
        audio.Objects.Add(new SourceSound());
        Element video = h.AddElement(Seconds(10), Seconds(10), 1);
        video.Objects.Add(new SceneSound { ReferencedScene = { Value = new Scene(100, 100, "nested") { Duration = { Value = Seconds(60) } } } });
        var group = System.Collections.Immutable.ImmutableHashSet.Create(audio.Id.Value, video.Id.Value);
        h.Scene.Groups.Add(group);
        Element follower = h.AddElement(Seconds(22), Seconds(2));
        Element otherLayer = h.AddElement(Seconds(22), Seconds(2), 2);
        h.History.Commit("Setup");
        int before = h.History.UndoCount;
        var service = new SilenceSplitService(h.History);
        var ranges = SilenceSplitService.Plan(h.Scene, audio, [Range(2, 3), Range(6, 8)]);
        service.Apply(h.Scene, audio, ranges, mode, linked);
        Assert.Multiple(() =>
        {
            Assert.That(h.Scene.Children.Count, Is.EqualTo(parts + 2));
            Assert.That(h.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(follower.Start.Value, Is.EqualTo(Seconds(mode == SilenceSplitMode.RippleDelete ? 19 : 22)));
            Assert.That(otherLayer.Start.Value, Is.EqualTo(Seconds(22)));
            Assert.That(h.Scene.Children.Where(e => e.ZIndex.Value == 0 && e != follower)
                .OrderBy(e => e.Start.Value).Last().Objects.OfType<SourceSound>().Single().OffsetPosition.Value, Is.EqualTo(Seconds(8)));
            if (linked)
                Assert.That(h.Scene.Children.Where(e => e.ZIndex.Value == 1).OrderBy(e => e.Start.Value).Last()
                    .Objects.OfType<SceneSound>().Single().OffsetPosition.Value, Is.EqualTo(Seconds(8)));
            else Assert.That(video.Length.Value, Is.EqualTo(Seconds(10)));
        });
        h.History.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { audio, video, follower, otherLayer }));
            Assert.That(audio.Length.Value, Is.EqualTo(Seconds(10)));
            Assert.That(video.Length.Value, Is.EqualTo(Seconds(10)));
            Assert.That(follower.Start.Value, Is.EqualTo(Seconds(22)));
            Assert.That(h.Scene.Groups.Single(), Is.EquivalentTo(group));
        });
        h.History.Redo();
        Assert.That(h.Scene.Children.Count, Is.EqualTo(parts + 2));
    }

    [Test]
    public void Plan_RoundsInwardAtFractionalFrameRate_AndPreservesOffGridClipEdges()
    {
        using var h = new SceneHistoryHarness("silence-frames");
        var project = new Project { FrameRate = { Value = new Rational(30000, 1001) } };
        project.Items.Add(h.Scene);
        var e = h.AddElement(Seconds(1.001), Seconds(4.002));
        var plan = SilenceSplitService.Plan(h.Scene, e, [Range(0, 0.5), Range(1.01, 1.02), Range(2, 4.002)]);
        Assert.That(plan, Has.Count.EqualTo(2));
        Assert.That(plan[0].Start, Is.EqualTo(e.Start.Value));
        Assert.That(plan[0].End, Is.LessThanOrEqualTo(Seconds(1.501)));
        Assert.That(plan[1].Start, Is.GreaterThanOrEqualTo(Seconds(3.001)));
        Assert.That(plan[1].End, Is.EqualTo(e.Range.End));
    }

    [Test]
    public void Apply_WholeSilenceRemovesClip_EmptyPlanAddsNoHistory_AndLockedLinkDoesNotPartiallyEdit()
    {
        using var h = new SceneHistoryHarness("silence-lock");
        var e = h.AddElement(Seconds(0), Seconds(5));
        var linked = h.AddElement(Seconds(0), Seconds(5), 1);
        h.Scene.Groups.Add([e.Id.Value, linked.Id.Value]);
        linked.IsLocked.Value = true;
        h.History.Commit("Setup");
        int before = h.History.UndoCount;
        var service = new SilenceSplitService(h.History);
        service.Apply(h.Scene, e, [], SilenceSplitMode.SplitOnly, true);
        Assert.That(h.History.UndoCount, Is.EqualTo(before));
        Assert.Throws<InvalidOperationException>(() => service.Apply(h.Scene, e, [Range(1, 2)], SilenceSplitMode.DeleteSilence, true));
        Assert.That(h.Scene.Children, Has.Count.EqualTo(2));
        service.Apply(h.Scene, e, [Range(0, 5)], SilenceSplitMode.DeleteSilence, false);
        Assert.That(h.Scene.Children, Is.EqualTo(new[] { linked }));
        h.History.Undo();
        Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { e, linked }));
    }

    [Test]
    public void Apply_FailedLinkedSplitRollsBackAllEarlierCuts()
    {
        using var h = new SceneHistoryHarness("silence-failure");
        var e = h.AddElement(Seconds(0), Seconds(5));
        var failing = new ElementStructureServiceTests.FailingSplitElement { Length = { Value = Seconds(5) }, ZIndex = { Value = 1 } };
        h.Scene.Children.Add(failing);
        h.Scene.Groups.Add([e.Id.Value, failing.Id.Value]);
        h.History.Commit("Setup");
        int before = h.History.UndoCount;
        failing.FailSerialization = true;
        Assert.Throws<IOException>(() => new SilenceSplitService(h.History).Apply(h.Scene, e, [Range(1, 2)], SilenceSplitMode.RippleDelete, true));
        Assert.That(h.Scene.Children, Has.Count.EqualTo(2));
        Assert.That(e.Length.Value, Is.EqualTo(Seconds(5)));
        Assert.That(h.History.UndoCount, Is.EqualTo(before));
        Assert.That(h.History.HasPendingOperations, Is.False);
    }

    private sealed class ReadFailureDecoder(string path, bool fail) : IDecoderInfo
    {
        public int SuccessfulReads { get; private set; }
        public int FailedReads { get; private set; }
        public string Name => "Silence read failure test";
        public IEnumerable<string> VideoExtensions() => [];
        public IEnumerable<string> AudioExtensions() => [".silence-test"];
        public MediaReader? Open(string file, MediaOptions options) => file == path ? new Reader(this, fail) : null;

        private sealed class Reader(ReadFailureDecoder owner, bool fail) : MediaReader
        {
            public override VideoStreamInfo VideoInfo => throw new NotSupportedException();
            public override AudioStreamInfo AudioInfo { get; } = new("test", fail ? new Rational(3) : new Rational(3, 2), 44100, 2);
            public override bool HasVideo => false;
            public override bool HasAudio => true;
            public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image) { image = null; return false; }
            public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
            {
                if (fail && start >= AudioInfo.SampleRate)
                {
                    owner.FailedReads++;
                    sound = null;
                    return false;
                }
                owner.SuccessfulReads++;
                int remaining = (int)(AudioInfo.Duration.ToDouble() * AudioInfo.SampleRate) - start;
                sound = new AudioBuffer(AudioInfo.SampleRate, 2, Math.Clamp(remaining, 0, length));
                return true;
            }
        }
    }
}
