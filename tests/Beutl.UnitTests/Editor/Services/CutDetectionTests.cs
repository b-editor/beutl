using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class CutDetectionTests
{
    private DecoderRegistry _decoders = null!;
    private CutTestDecoderInfo _decoder = null!;

    [SetUp]
    public void SetUp()
    {
        _decoders = TestMediaHelper.CreateRegistry();
        _decoder = new CutTestDecoderInfo();
        _decoders.Register(_decoder);
    }

    [TearDown]
    public void TearDown() => _decoders.Unregister(_decoder);

    private static MediaTime Seconds(double value) => MediaTime.FromSeconds(value);
    private static TimeRange Range(double start, double end) => TimeRange.FromRange(Seconds(start), Seconds(end));

    [Test]
    public async Task Detect_ThreeSolidShotsAtThirtyFps_ReportsCutsAtOneAndTwoSeconds()
    {
        using var h = new SceneHistoryHarness("cut-detection");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);

        CutDetectionResult result = await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);
        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));

        Assert.That(cuts, Has.Count.EqualTo(2));
        Assert.That(cuts[0].ToSeconds(), Is.EqualTo(1.0).Within(1.0 / 30));
        Assert.That(cuts[1].ToSeconds(), Is.EqualTo(2.0).Within(1.0 / 30));
    }

    [Test]
    public async Task Detect_TrimmedClipAtSceneSecondFive_MovesCutsToSceneTime()
    {
        using var h = new SceneHistoryHarness("cut-detection-trim");
        Element element = AddShots(h, Seconds(5), Seconds(2.5), Seconds(0.5));
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);

        CutDetectionResult result = await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);
        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));

        Assert.That(cuts, Has.Count.EqualTo(2));
        Assert.That(cuts[0].ToSeconds(), Is.EqualTo(5.5).Within(1.0 / 30));
        Assert.That(cuts[1].ToSeconds(), Is.EqualTo(6.5).Within(1.0 / 30));
    }

    [Test]
    public async Task Detect_DoubleSpeed_MapsCutsToHalfSceneTime()
    {
        using var h = new SceneHistoryHarness("cut-detection-speed");
        Element element = AddShots(h, Seconds(0), Seconds(1.5), Seconds(0), speed: 200);
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);

        CutDetectionResult result = await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);
        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));

        Assert.That(cuts, Is.EqualTo(new[] { Seconds(0.5), Seconds(1) }));
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void CanAnalyze_WithAnEnabledTimeController_IsFalse(bool nested)
    {
        using var h = new SceneHistoryHarness("cut-detection-controller");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        var controller = new DrawableTimeController();
        if (nested)
        {
            var group = new DrawableGroup();
            group.Children.Add(controller);
            element.Objects.Add(group);
        }
        else
        {
            element.Objects.Add(controller);
        }

        Assert.That(CutDetectionService.CanAnalyze(h.Scene, element), Is.False);
    }

    [Test]
    public void CanAnalyze_WithADisabledTimeController_IsTrue()
    {
        using var h = new SceneHistoryHarness("cut-detection-controller-disabled");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        element.Objects.Add(new DrawableTimeController { IsEnabled = { Value = false } });

        Assert.That(CutDetectionService.CanAnalyze(h.Scene, element), Is.True);
    }

    [Test]
    public void GetCuts_StrongScoreAtClipEnd_DoesNotSuppressAValidCutInsideTheClip()
    {
        var result = new CutDetectionResult(
            [
                new CutFrameScore(Seconds(2.8), 0.6),
                new CutFrameScore(Seconds(3.0), 0.95),
            ],
            Range(0, 3),
            new Rational(30, 1));

        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));

        Assert.That(cuts, Is.EqualTo(new[] { Seconds(2.8) }));
    }

    [Test]
    public async Task Analyze_FrameReadFailure_ShowsErrorAndOffersNoCandidates()
    {
        using var h = new SceneHistoryHarness("cut-detection-read-failure");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        _decoder.ReaderFactory = () => new CutTestReader(failAtFrame: 10);
        using var model = new CutDetectionViewModel(h.Scene, element, h.History, new EditorConfig(), decoders: _decoders);

        await model.AnalyzeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(model.CanApply.Value, Is.False);
            Assert.That(model.Candidates, Is.Empty);
            Assert.That(model.Status.Value, Does.Contain(Strings.CutDetection_NoFrames));
        });
    }

    [Test]
    public void GetCuts_MinimumIntervalKeepsTheStrongerOfTwoNearbyCuts()
    {
        var result = new CutDetectionResult(
            [
                new CutFrameScore(Seconds(1.0), 0.9),
                new CutFrameScore(Seconds(1.2), 0.6),
                new CutFrameScore(Seconds(2.0), 0.8),
            ],
            Range(0, 3),
            new Rational(30, 1));

        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));

        Assert.That(cuts, Is.EqualTo(new[] { Seconds(1.0), Seconds(2.0) }));
    }

    [Test]
    public async Task ChangingTheSettings_ReFiltersTheStoredScoresWithoutDecodingAgain()
    {
        using var h = new SceneHistoryHarness("cut-detection-filter");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        using var model = new CutDetectionViewModel(h.Scene, element, h.History, new EditorConfig(), decoders: _decoders);
        await model.AnalyzeAsync();
        Assert.That(model.Candidates, Has.Count.EqualTo(2));
        int opens = _decoder.OpenCount;

        model.MinimumMilliseconds.Value = 1500m;

        Assert.Multiple(() =>
        {
            Assert.That(model.Candidates, Has.Count.EqualTo(1));
            Assert.That(_decoder.OpenCount, Is.EqualTo(opens));
        });
    }

    [Test]
    public async Task ApplySplit_CutsAtCandidates_AndUndoesAsOneStep()
    {
        using var h = new SceneHistoryHarness("cut-detection-apply");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);
        CutDetectionResult result = await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);
        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));
        int before = h.History.UndoCount;

        bool applied = service.ApplySplit(h.Scene, element, cuts, includeLinked: false);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(h.Scene.Children, Has.Count.EqualTo(3));
            Assert.That(h.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(h.Scene.Children.OrderBy(e => e.Start.Value).Select(e => e.Start.Value),
                Is.EqualTo(new[] { Seconds(0), Seconds(1), Seconds(2) }));
        });

        h.History.Undo();

        Assert.Multiple(() =>
        {
            Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { element }));
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(0)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
        });
    }

    [Test]
    public async Task Analyze_WhenCancelledMidway_LeavesTheSceneUnchanged()
    {
        using var h = new SceneHistoryHarness("cut-detection-analyze-cancel");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        using var cancellation = new CancellationTokenSource();
        _decoder.ReaderFactory = () => new CutTestReader(cancellation);
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await service.AnalyzeAsync(h.Scene, element, cancellation.Token));

        Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { element }));
    }

    [Test]
    public async Task ApplySplit_WithACancelledToken_LeavesTheSceneUnchanged()
    {
        using var h = new SceneHistoryHarness("cut-detection-cancel");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);
        CutDetectionResult result = await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);
        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(() => service.ApplySplit(h.Scene, element, cuts, includeLinked: false, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(h.Scene.Children, Is.EquivalentTo(new[] { element }));
    }

    [Test]
    public async Task AddMarkers_AddsOneMarkerPerCut_AndUndoesAsOneStep()
    {
        using var h = new SceneHistoryHarness("cut-detection-markers");
        Element element = AddShots(h, Seconds(0), Seconds(3), Seconds(0));
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);
        CutDetectionResult result = await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);
        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));
        h.History.Commit("Setup");
        int before = h.History.UndoCount;

        int added = service.AddMarkers(h.Scene, cuts);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.EqualTo(2));
            Assert.That(h.Scene.Markers.Select(marker => marker.Time.Value),
                Is.EqualTo(new[] { Seconds(1), Seconds(2) }));
            Assert.That(h.History.UndoCount, Is.EqualTo(before + 1));
        });

        h.History.Undo();
        Assert.That(h.Scene.Markers, Is.Empty);
    }

    [Test]
    public async Task Detect_LongClipWithAStride_PlacesCutsOnTheExactFrame()
    {
        // 36,000 frames sample every 5th frame; none of these boundaries is a multiple of 5.
        int[] boundaries = [10_003, 20_007, 30_001];
        _decoder.ReaderFactory = () => new CutTestReader(frameCount: 36_000, shotStarts: boundaries);
        using var h = new SceneHistoryHarness("cut-detection-long");
        Element element = AddShots(h, Seconds(0), Seconds(1200), Seconds(0));
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);

        CutDetectionResult result = await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);
        IReadOnlyList<MediaTime> cuts = result.GetCuts(0.3, Seconds(0.5));

        Assert.That(cuts.Select(cut => cut.ToSeconds()),
            Is.EqualTo(boundaries.Select(frame => frame / 30.0)).Within(1.0 / 30));
    }

    [Test]
    public async Task Analyze_WithADisabledAndAnEnabledVideo_AnalyzesTheEnabledOne()
    {
        using var h = new SceneHistoryHarness("cut-detection-disabled-video");
        Element element = h.AddElement(Seconds(0), Seconds(3));
        // Created in reverse so a decoder open during setup cannot leave the expected file last.
        SourceVideo shown = CreateVideo(h, "shots.testcuts", MediaTime.Zero, 100);
        element.Objects.Add(CreateVideo(h, "hidden.testcuts", MediaTime.Zero, 100, enabled: false));
        element.Objects.Add(shown);
        h.History.Commit("Setup");
        var service = new CutDetectionService(h.History, new EditorConfig(), _decoders);

        await service.AnalyzeAsync(h.Scene, element, CancellationToken.None);

        Assert.That(Path.GetFileName(_decoder.LastFile), Is.EqualTo("shots.testcuts"));
    }

    [Test]
    public void CanAnalyze_WithOnlyADisabledVideo_IsFalse()
    {
        using var h = new SceneHistoryHarness("cut-detection-only-disabled");
        Element element = h.AddElement(Seconds(0), Seconds(3));
        element.Objects.Add(CreateVideo(h, "shots.testcuts", MediaTime.Zero, 100, enabled: false));
        h.History.Commit("Setup");

        Assert.That(CutDetectionService.CanAnalyze(h.Scene, element), Is.False);
    }

    private static Element AddShots(SceneHistoryHarness h, MediaTime start, MediaTime length, MediaTime offset,
        float speed = 100)
    {
        Element element = h.AddElement(start, length);
        element.Objects.Add(CreateVideo(h, "shots.testcuts", offset, speed));
        h.History.Commit("Setup");
        return element;
    }

    private static SourceVideo CreateVideo(SceneHistoryHarness h, string fileName, MediaTime offset, float speed,
        bool enabled = true)
    {
        string path = Path.Combine(h.BasePath, fileName);
        File.WriteAllBytes(path, []);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        return new SourceVideo
        {
            IsEnabled = { Value = enabled },
            Source = { Value = source },
            OffsetPosition = { Value = offset },
            Speed = { Value = speed },
        };
    }

    private sealed class CutTestDecoderInfo : IDecoderInfo
    {
        public string Name => "Cut Test Decoder";
        public int OpenCount { get; private set; }
        public string? LastFile { get; private set; }
        public Func<MediaReader>? ReaderFactory { get; set; }

        public MediaReader? Open(string file, MediaOptions options)
        {
            if (!Path.GetExtension(file).Equals(".testcuts", StringComparison.OrdinalIgnoreCase)) return null;
            OpenCount++;
            LastFile = file;
            return ReaderFactory?.Invoke() ?? new CutTestReader();
        }

        public IEnumerable<string> VideoExtensions() => [".testcuts"];

        public IEnumerable<string> AudioExtensions() => [];
    }

    // By default three one-second solid shots at 30 fps, so a correct detector reports the two boundaries.
    private sealed class CutTestReader : MediaReader
    {
        private static readonly (byte R, byte G, byte B)[] s_colors =
        [
            (255, 0, 0),
            (0, 255, 0),
            (0, 0, 255),
            (255, 255, 255),
        ];

        private readonly CancellationTokenSource? _cancelAfterFirstFrame;
        private readonly int? _failAtFrame;
        private readonly int[] _shotStarts;
        private readonly VideoStreamInfo _videoInfo;
        private readonly AudioStreamInfo _audioInfo = new("cut-test", Rational.Zero, 44100, 2);
        private bool _firstFrame = true;

        public CutTestReader(CancellationTokenSource? cancelAfterFirstFrame = null, int? failAtFrame = null,
            int frameCount = 90, int[]? shotStarts = null)
        {
            _cancelAfterFirstFrame = cancelAfterFirstFrame;
            _failAtFrame = failAtFrame;
            _shotStarts = shotStarts ?? [30, 60];
            _videoInfo = new VideoStreamInfo("cut-test", frameCount, new PixelSize(16, 9), new Rational(30, 1));
        }

        public override VideoStreamInfo VideoInfo => _videoInfo;
        public override AudioStreamInfo AudioInfo => _audioInfo;
        public override bool HasVideo => true;
        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            if (frame < 0 || frame >= _videoInfo.NumFrames || frame == _failAtFrame)
            {
                image = null;
                return false;
            }

            (byte r, byte g, byte b) = s_colors[_shotStarts.Count(start => frame >= start)];
            var bitmap = new Bitmap(_videoInfo.FrameSize.Width, _videoInfo.FrameSize.Height);
            bitmap.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(r, g, b, 255));
            image = Ref<Bitmap>.Create(bitmap);
            if (_firstFrame && _cancelAfterFirstFrame != null)
            {
                _firstFrame = false;
                _cancelAfterFirstFrame.Cancel();
            }

            return true;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            sound = null;
            return false;
        }
    }
}
