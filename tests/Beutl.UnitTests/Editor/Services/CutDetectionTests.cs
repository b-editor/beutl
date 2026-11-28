using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics;
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

    private static Element AddShots(SceneHistoryHarness h, MediaTime start, MediaTime length, MediaTime offset,
        float speed = 100)
    {
        string path = Path.Combine(h.BasePath, "shots.testcuts");
        File.WriteAllBytes(path, []);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        var video = new SourceVideo
        {
            Source = { Value = source },
            OffsetPosition = { Value = offset },
            Speed = { Value = speed },
        };
        Element element = h.AddElement(start, length);
        element.Objects.Add(video);
        h.History.Commit("Setup");
        return element;
    }

    private sealed class CutTestDecoderInfo : IDecoderInfo
    {
        public string Name => "Cut Test Decoder";
        public int OpenCount { get; private set; }
        public Func<MediaReader>? ReaderFactory { get; set; }

        public MediaReader? Open(string file, MediaOptions options)
        {
            if (!Path.GetExtension(file).Equals(".testcuts", StringComparison.OrdinalIgnoreCase)) return null;
            OpenCount++;
            return ReaderFactory?.Invoke() ?? new CutTestReader();
        }

        public IEnumerable<string> VideoExtensions() => [".testcuts"];

        public IEnumerable<string> AudioExtensions() => [];
    }

    // Three one-second solid shots at 30 fps, so a correct detector reports the two boundaries.
    private sealed class CutTestReader : MediaReader
    {
        private const int FramesPerShot = 30;
        private static readonly (byte R, byte G, byte B)[] s_colors =
        [
            (255, 0, 0),
            (0, 255, 0),
            (0, 0, 255),
        ];

        private readonly CancellationTokenSource? _cancelAfterFirstFrame;
        private readonly VideoStreamInfo _videoInfo = new(
            "cut-test", FramesPerShot * s_colors.Length, new PixelSize(64, 36), new Rational(30, 1));
        private readonly AudioStreamInfo _audioInfo = new("cut-test", Rational.Zero, 44100, 2);
        private bool _firstFrame = true;

        public CutTestReader(CancellationTokenSource? cancelAfterFirstFrame = null)
        {
            _cancelAfterFirstFrame = cancelAfterFirstFrame;
        }

        public override VideoStreamInfo VideoInfo => _videoInfo;
        public override AudioStreamInfo AudioInfo => _audioInfo;
        public override bool HasVideo => true;
        public override bool HasAudio => false;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            if (frame < 0 || frame >= _videoInfo.NumFrames)
            {
                image = null;
                return false;
            }

            (byte r, byte g, byte b) = s_colors[frame / FramesPerShot];
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
