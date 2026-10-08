using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
[NonParallelizable]
public class TrimDisabledFlowControllerTests
{
    private SceneHistoryHarness _harness = null!;
    private bool _originalClamp;

    [OneTimeSetUp]
    public void RegisterDecoder() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void SetUp()
    {
        _originalClamp = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = true;
        _harness = new SceneHistoryHarness("beutl_disabled_flow_trim", duration: Seconds(30));
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = _originalClamp;
    }

    [Test]
    public void Slip_ReenablingController_CannotOverrunTheSource()
    {
        var video = CreateVideo(5);
        Element element = AddElement(0, 4, video);
        var controller = AddDisabledController(element, offset: 1);
        int history = _harness.History.UndoCount;

        bool applied = new ElementSlipService(_harness.History).Slip(_harness.Scene, [element], Seconds(1));
        controller.IsEnabled = true;
        using var compositor = CreateCompositor();
        double position = Position(Videos(compositor.EvaluateGraphics(Seconds(3.9)).Objects.Single()).Single());

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(position, Is.EqualTo(4.9).Within(0.000001));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history));
        });
    }

    [TestCase(200, 0, 2, 5, 3, 1)]
    [TestCase(100, 0.5, 3, 5, 3, 1.5)]
    [TestCase(50, 0, 2, 4, 5, 2)]
    [TestCase(0, 1, 2, 4, 5, 2)]
    public void Slip_DisabledController_AddsBoundsWithoutChangingTheOffsetClock(
        float controllerSpeed, double controllerOffset, double length, int sourceLength, double requested, double expected)
    {
        var video = CreateVideo(sourceLength);
        Element element = AddElement(0, length, video);
        var controller = AddDisabledController(element, controllerSpeed, controllerOffset);
        TimeSpan preview = SlippableMedia.ClampSharedDelta(SlippableMedia.Collect(element), Seconds(requested));
        bool applied = new ElementSlipService(_harness.History).Slip(_harness.Scene, [element], Seconds(requested));
        using var compositor = CreateCompositor();
        double plainPosition = Position(Videos(compositor.EvaluateGraphics(Seconds(length - 0.1)).Objects.Single()).Single());
        controller.IsEnabled = true;
        double controlledPosition = Position(Videos(compositor.EvaluateGraphics(Seconds(length - 0.1)).Objects.Single()).Single());

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(preview.TotalSeconds, Is.EqualTo(expected).Within(0.000001));
            Assert.That(video.OffsetPosition.CurrentValue.TotalSeconds, Is.EqualTo(expected).Within(0.000001));
            Assert.That(plainPosition, Is.LessThanOrEqualTo(sourceLength));
            Assert.That(controlledPosition, Is.LessThanOrEqualTo(sourceLength));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Resize_ReenablingController_UsesTheSameSafePreviewAndCommit(bool leftEdge)
    {
        var video = CreateVideo(5);
        Element element = AddElement(leftEdge ? 1 : 0, 3, video);
        var controller = AddDisabledController(element, offset: 1);
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        TimeSpan preview = leftEdge ? constraints.ClampStart(Seconds(-0.5)) : constraints.ClampLength(Seconds(4.5));
        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(element, Seconds(leftEdge ? -0.5 : 0), Seconds(4.5), 0)]);
        controller.IsEnabled = true;
        using var compositor = CreateCompositor();
        var resource = Videos(compositor.EvaluateGraphics(element.Range.End - Seconds(0.1)).Objects.Single()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(preview, Is.EqualTo(Seconds(leftEdge ? 0 : 4)));
            Assert.That(element.Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(element.Length, Is.EqualTo(Seconds(4)));
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(Position(resource), Is.EqualTo(4.9).Within(0.000001));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trim_FrontDisabledController_ConstrainsRollAndSlideGrowth(bool slide)
    {
        var video = CreateVideo(5);
        Element front = AddElement(0, 3, video);
        var controller = AddDisabledController(front, offset: 1);
        Element? middle = slide ? _harness.AddElement(Seconds(3), Seconds(1)) : null;
        Element back = AddElement(slide ? 4 : 3, 4, CreateVideo(20));
        var service = new ElementResizeService(_harness.History);
        var pairs = new[] { new ElementTrimPair(front, back) };
        TimeSpan preview = ElementResizeService.CreateTrimConstraints(_harness.Scene, pairs, middle == null ? null : [middle]).Clamp(Seconds(2));
        bool applied = slide
            ? service.Slide(_harness.Scene, [new(front, [middle!], back)], Seconds(2))
            : service.Roll(_harness.Scene, pairs, Seconds(2));
        controller.IsEnabled = true;
        using var compositor = CreateCompositor();
        var resource = Videos(compositor.EvaluateGraphics(Seconds(3.9)).Objects.Single()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(preview, Is.EqualTo(Seconds(1)));
            Assert.That(front.Length, Is.EqualTo(Seconds(4)));
            Assert.That(back.Start, Is.EqualTo(Seconds(slide ? 5 : 4)));
            Assert.That(Position(resource), Is.EqualTo(4.9).Within(0.000001));
        });
    }

    [Test]
    public void Slip_DisabledUnconsumedExpression_DoesNotRejectLaterVideo()
    {
        var controller = new DrawableTimeController { IsEnabled = false };
        controller.Speed.Expression = Expression.Create<float>("200");
        Element element = AddElement(0, 2, controller);
        var video = CreateVideo(10);
        element.Objects.Add(video);

        Assert.That(new ElementSlipService(_harness.History).Slip(_harness.Scene, [element], Seconds(1)), Is.True);
        Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
    }

    [Test]
    public void Slip_ReenabledControllerChangesLaterFlowSelection_ValidatesTheActuallyConsumedVideo()
    {
        var first = CreateVideo(20);
        var second = CreateVideo(5);
        Element element = AddElement(0, 2, first);
        element.Objects.Add(second);
        var disabled = AddDisabledController(element, speed: 50);
        element.Objects.Add(new DrawableTimeController { Speed = { CurrentValue = 200 } });

        // While disabled, the final controller consumes the first video. Enabling
        // the earlier controller moves that first input to the end of Flow, so
        // the final controller consumes the second video instead.
        TimeSpan preview = SlippableMedia.ClampSharedDelta(SlippableMedia.Collect(element), Seconds(2));
        bool applied = new ElementSlipService(_harness.History).Slip(_harness.Scene, [element], Seconds(2));
        disabled.IsEnabled = true;
        using var compositor = CreateCompositor();
        var frames = compositor.EvaluateGraphics(Seconds(1.9)).Objects.SelectMany(Videos).ToArray();
        var secondFrame = frames.Single(resource => ReferenceEquals(resource.RequireOriginal(), second));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(preview, Is.EqualTo(Seconds(1)));
            Assert.That(first.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(2)), "Its active 200% clock still owns the offset write.");
            Assert.That(second.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
            Assert.That(Position(secondFrame), Is.EqualTo(4.8).Within(0.000001));
        });
    }

    [Test]
    public void Slip_MultipleDisabledControllers_ValidatesCombinedReenabling()
    {
        var video = CreateVideo(5);
        Element element = AddElement(0, 3, video);
        var first = AddDisabledController(element, offset: 0.5);
        var second = AddDisabledController(element, offset: 0.5);

        Assert.That(new ElementSlipService(_harness.History).Slip(_harness.Scene, [element], Seconds(2)), Is.True);
        first.IsEnabled = true;
        second.IsEnabled = true;
        using var compositor = CreateCompositor();
        var resource = Videos(compositor.EvaluateGraphics(Seconds(2.9)).Objects.Single()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
            Assert.That(Position(resource), Is.EqualTo(4.9).Within(0.000001));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_SharedPresenterAndDisabledController_PreservesTheActiveOffsetClock(bool presenterFirst)
    {
        var video = CreateVideo(5);
        var presenter = new DrawablePresenter { Target = { CurrentValue = video } };
        Element element = AddElement(0, 2, presenterFirst ? presenter : video);
        element.Objects.Add(presenterFirst ? video : presenter);
        var controller = AddDisabledController(element, speed: 200);

        Assert.That(new ElementSlipService(_harness.History).Slip(_harness.Scene, [element], Seconds(2)), Is.True);
        controller.IsEnabled = true;
        using var compositor = CreateCompositor();
        var resources = compositor.EvaluateGraphics(Seconds(1.9)).Objects.SelectMany(Videos).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
            Assert.That(resources, Has.Length.EqualTo(2));
            Assert.That(resources.Select(Position).Max(), Is.EqualTo(4.8).Within(0.000001));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_LinkedAudio_UsesOneTimelineDeltaAndRestoresOffsetsWithHistory(bool frozenVideo)
    {
        var video = CreateVideo(5);
        if (frozenVideo) video.Speed.CurrentValue = 0;
        Element element = AddElement(0, 2, video);
        AddDisabledController(element, speed: 200);
        var source = new SoundSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 3)));
        var sound = new SourceSound { Source = { CurrentValue = source }, Speed = { CurrentValue = 200 } };
        Element audio = _harness.AddElement(Seconds(5), Seconds(1));
        audio.Objects.Add(sound);
        int history = _harness.History.UndoCount;

        Assert.That(new ElementSlipService(_harness.History).Slip(_harness.Scene, [element, audio], Seconds(2)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(frozenVideo ? 0 : 0.5)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history + 1));
        });
        Assert.That(_harness.History.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
        });
        Assert.That(_harness.History.Redo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(frozenVideo ? 0 : 0.5)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(1)));
        });
    }

    [Test]
    public void Slip_DisabledControllerWithUnknownMapping_RejectsTheWholeLinkedEdit()
    {
        var video = CreateVideo(20);
        Element element = AddElement(0, 2, video);
        var controller = AddDisabledController(element);
        controller.Speed.Expression = Expression.Create<float>("200");
        var linked = CreateVideo(20);
        Element linkedElement = AddElement(5, 2, linked);
        int history = _harness.History.UndoCount;

        Assert.That(new ElementSlipService(_harness.History).Slip(_harness.Scene, [linkedElement, element], Seconds(1)), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(linked.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_ReenableStateBudget_RejectsAtomicallyOnlyWhenControllersHaveIncomingFlow(bool incomingFlow)
    {
        var video = CreateVideo(20);
        Element element = _harness.AddElement(TimeSpan.Zero, Seconds(2));
        if (incomingFlow) element.Objects.Add(video);
        for (int i = 0; i < 8; i++) AddDisabledController(element, offset: 0.1);
        if (!incomingFlow) element.Objects.Add(video);
        int history = _harness.History.UndoCount;

        bool applied = new ElementSlipService(_harness.History).Slip(_harness.Scene, [element], Seconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(!incomingFlow));
            Assert.That(video.OffsetPosition.CurrentValue, Is.EqualTo(Seconds(incomingFlow ? 0 : 1)));
            Assert.That(_harness.History.UndoCount, Is.EqualTo(history + (incomingFlow ? 0 : 1)));
        });
    }

    [Test]
    public void Collect_ReenableStatesOpenEachVideoAndAudioSourceOnlyOnce()
    {
        var decoder = new CountingDurationDecoder();
        DecoderRegistry.Register(decoder);
        try
        {
            Uri videoUri = CreateCountingSourceFile(decoder, 10);
            Uri audioUri = CreateCountingSourceFile(decoder, 8);
            var videoSource = new VideoSource();
            videoSource.ReadFrom(videoUri);
            var audioSource = new SoundSource();
            audioSource.ReadFrom(audioUri);
            Element element = AddElement(0, 2, new SourceVideo { Source = { CurrentValue = videoSource } });
            element.Objects.Add(new SourceSound { Source = { CurrentValue = audioSource } });
            for (int i = 0; i < 7; i++) AddDisabledController(element, offset: 0.1);

            var targets = SlippableMedia.Collect(element);
            TimeSpan clamped = SlippableMedia.ClampSharedDelta(targets, Seconds(15));
            TestContext.WriteLine($"Reader opens across 128 Flow states: video={decoder.Opens[videoUri.LocalPath]}, audio={decoder.Opens[audioUri.LocalPath]}.");

            Assert.Multiple(() =>
            {
                Assert.That(targets, Has.Count.EqualTo(2));
                Assert.That(targets.Select(target => target.Total), Is.EquivalentTo(new[] { Seconds(10), Seconds(8) }));
                Assert.That(clamped, Is.EqualTo(Seconds(6)));
                Assert.That(decoder.Opens[videoUri.LocalPath], Is.EqualTo(1));
                Assert.That(decoder.Opens[audioUri.LocalPath], Is.EqualTo(1));
                Assert.That(decoder.Disposals, Is.EqualTo(2));
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    [Test]
    public void Collect_DurationCacheIsLocalToTheOperationAndObservesSourceUriChanges()
    {
        var decoder = new CountingDurationDecoder();
        DecoderRegistry.Register(decoder);
        try
        {
            Uri firstUri = CreateCountingSourceFile(decoder, 10);
            Uri secondUri = CreateCountingSourceFile(decoder, 20);
            var source = new VideoSource();
            source.ReadFrom(firstUri);
            Element element = AddElement(0, 2, new SourceVideo { Source = { CurrentValue = source } });
            AddDisabledController(element);

            TimeSpan? first = SlippableMedia.Collect(element).Single().Total;
            TimeSpan? repeated = SlippableMedia.Collect(element).Single().Total;
            source.ReadFrom(secondUri);
            TimeSpan? changed = SlippableMedia.Collect(element).Single().Total;

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.EqualTo(Seconds(10)));
                Assert.That(repeated, Is.EqualTo(Seconds(10)));
                Assert.That(changed, Is.EqualTo(Seconds(20)));
                Assert.That(decoder.Opens[firstUri.LocalPath], Is.EqualTo(2));
                Assert.That(decoder.Opens[secondUri.LocalPath], Is.EqualTo(1));
                Assert.That(decoder.Disposals, Is.EqualTo(3));
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    private Uri CreateCountingSourceFile(CountingDurationDecoder decoder, double seconds)
    {
        string path = Path.Combine(_harness.BasePath, $"{Guid.NewGuid():N}.trim-duration-count");
        File.WriteAllBytes(path, []);
        decoder.Durations[path] = seconds;
        return new Uri(path);
    }

    private sealed class CountingDurationDecoder : IDecoderInfo
    {
        public string Name => "Trim duration counting decoder";
        public Dictionary<string, double> Durations { get; } = new();
        public Dictionary<string, int> Opens { get; } = new();
        public int Disposals { get; private set; }
        public IEnumerable<string> VideoExtensions() => [".trim-duration-count"];
        public IEnumerable<string> AudioExtensions() => [".trim-duration-count"];

        public MediaReader? Open(string file, MediaOptions options)
        {
            if (!Durations.TryGetValue(file, out double seconds)) return null;
            Opens[file] = Opens.GetValueOrDefault(file) + 1;
            return new CountingDurationReader(seconds, this);
        }

        private sealed class CountingDurationReader(double seconds, CountingDurationDecoder owner) : MediaReader
        {
            public override VideoStreamInfo VideoInfo { get; } = new("test", (int)(seconds * 30), new PixelSize(100, 100), new Rational(30, 1));
            public override AudioStreamInfo AudioInfo { get; } = new("test", new Rational((int)seconds, 1), 44100, 2);
            public override bool HasVideo => true;
            public override bool HasAudio => true;
            public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image) { image = null; return false; }
            public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound) { sound = null; return false; }
            protected override void Dispose(bool disposing) => owner.Disposals++;
        }
    }

    private DrawableTimeController AddDisabledController(Element element, float speed = 100, double offset = 0)
    {
        var controller = new DrawableTimeController
        {
            IsEnabled = false,
            Speed = { CurrentValue = speed },
            OffsetPosition = { CurrentValue = Seconds(offset) }
        };
        element.Objects.Add(controller);
        return controller;
    }

    private SceneCompositor CreateCompositor() => new(_harness.Scene) { DisableResourceShare = true, ForceOriginalSource = true };

    private Element AddElement(double start, double length, Drawable drawable)
    {
        Element element = _harness.AddElement(Seconds(start), Seconds(length));
        element.Objects.Add(drawable);
        return element;
    }

    private static SourceVideo CreateVideo(int duration)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), duration * 30)));
        return new SourceVideo { Source = { CurrentValue = source } };
    }

    private static IEnumerable<SourceVideo.Resource> Videos(EngineObject.Resource resource)
    {
        if (resource is SourceVideo.Resource video) yield return video;
        else if (resource is DrawableTimeController.Resource controller && controller.Target != null)
            foreach (var target in Videos(controller.Target)) yield return target;
        else if (resource is DrawableGroup.Resource group)
            foreach (var child in group.Children)
                foreach (var target in Videos(child)) yield return target;
        else if (resource is DrawablePresenter.Resource presenter && presenter.Target != null)
            foreach (var target in Videos(presenter.Target)) yield return target;
    }

    private static double Position(SourceVideo.Resource resource) => (resource.RequestedPosition + resource.OffsetPosition).TotalSeconds;

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
