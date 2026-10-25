using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Point = Avalonia.Point;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineMediaDurationTests
{
    private static readonly MediaTime OneFrameAt30 = MediaTime.FromFrames(1, new Rational(30));

    [AvaloniaTest]
    [TestCase(5d, 1d, 200f, 2d)]
    [TestCase(5.05d, 1d, 200f, 2d)]
    [TestCase(5d, 5d, 200f, 0d)]
    [TestCase(0.01d, 0d, 100f, 0d)]
    [TestCase(null, 1d, 200f, 1d)]
    public async Task OriginalDuration_UsesRemainingTimelineTimeAndKeepsMinimumFrame(
        double? sourceSeconds, double offsetSeconds, float speed, double expectedSeconds)
    {
        using var configuration = new EditorConfigScope();
        SceneSound sound = CreateSound(sourceSeconds, offsetSeconds, speed);
        ElementViewModel model = await OpenElement(sound);
        var editor = (EditViewModel)model.Timeline.EditorContext;
        int undoCount = editor.HistoryManager.UndoCount;

        // A known exhausted source is still actionable; an unresolved source is not.
        Assert.That(model.HasOriginalDuration(), Is.EqualTo(sourceSeconds.HasValue));
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);

        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(model.Model.Length.Value, Is.EqualTo(ExpectedLength(expectedSeconds)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(offsetSeconds)),
                "Changing the duration must not trim the source in-point.");
            if (!sourceSeconds.HasValue)
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
        });
    }

    [AvaloniaTest]
    public async Task OriginalDuration_UsesTightestNestedStream()
    {
        using var configuration = new EditorConfigScope();
        SceneSound slower = CreateSound(5, 0, 100);
        SceneSound faster = CreateSound(5, 1, 200);
        var group = new SoundGroup();
        group.Children.Add(slower);
        group.Children.Add(faster);
        ElementViewModel model = await OpenElement(group);

        Assert.That(model.HasOriginalDuration(), Is.True);
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);

        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(slower.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(faster.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [AvaloniaTest]
    [TestCase(5d, 1d, 200f, true, 2d)]
    [TestCase(5d, 1d, 200f, false, 4d)]
    [TestCase(5d, 5d, 200f, true, 0d)]
    [TestCase(0.01d, 0d, 100f, true, 0d)]
    [TestCase(null, 1d, 200f, true, 4d)]
    public async Task RightEdgeDrag_PreviewsAndCommitsMediaBoundWithoutChangingOffset(
        double? sourceSeconds, double offsetSeconds, float speed, bool clamp, double expectedSeconds)
    {
        using var configuration = new EditorConfigScope(clamp: clamp);
        SceneSound sound = CreateSound(sourceSeconds, offsetSeconds, speed);
        ElementViewModel model = await OpenElement(sound);
        TimelineTabViewModel timeline = model.Timeline;
        timeline.ClearSelected();
        timeline.SelectElement(model);
        using var view = OpenTimelineView(model, width: 1200);
        Border border = view.Border;
        Assert.That(border.Bounds.Width, Is.GreaterThan(40), "The resize edge must be laid out and hit-testable.");
        float scale = view.Scale;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(MediaTime.FromSeconds(4).TimeToPixel(scale), y), view.Window)!.Value;

        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"source-{sourceSeconds?.ToString() ?? "unknown"}-offset-{offsetSeconds}-clamp-{clamp}");

            Assert.Multiple(() =>
            {
                Assert.That(model.Width.Value,
                    Is.EqualTo(ExpectedLength(expectedSeconds).TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1)),
                    "Dragging previews geometry without writing the element.");
                Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(offsetSeconds)));
            });
        });

        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(model.Model.Length.Value, Is.EqualTo(ExpectedLength(expectedSeconds)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(offsetSeconds)),
                "Normal edge resize remains geometry-only.");
        });
    }

    [AvaloniaTest]
    [TestCase(5d, 4d, 5d, true)]
    [TestCase(7d, 3d, 4d, true)]
    [TestCase(5d, 4d, 4d, false)]
    public async Task LeftEdgeDrag_ClampsStartAndPreservesEnd(
        double sourceSeconds, double requestedStartSeconds, double expectedStartSeconds, bool clamp)
    {
        using var configuration = new EditorConfigScope(clamp: clamp);
        SceneSound sound = CreateSound(sourceSeconds, 1, 200);
        ElementViewModel model = await OpenElement(sound, startSeconds: 5, lengthSeconds: 2);
        TimelineTabViewModel timeline = model.Timeline;
        timeline.ClearSelected();
        timeline.SelectElement(model);
        using var view = OpenTimelineView(model, width: 1600);
        ElementView element = view.Element;
        Border border = view.Border;
        float scale = view.Scale;
        Point press = border.TranslatePoint(new Point(2, border.Bounds.Height / 2), view.Window)!.Value;
        Point release = element.TranslatePoint(new Point(
            MediaTime.FromSeconds(requestedStartSeconds).TimeToPixel(scale), element.Bounds.Height / 2), view.Window)!.Value;
        MediaTime expectedStart = MediaTime.FromSeconds(expectedStartSeconds);
        MediaTime end = MediaTime.FromSeconds(7);

        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"left-source-{sourceSeconds}-clamp-{clamp}");

            Assert.Multiple(() =>
            {
                Assert.That(model.BorderMargin.Value.Left, Is.EqualTo(expectedStart.TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Width.Value, Is.EqualTo((end - expectedStart).TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.BorderMargin.Value.Left + model.Width.Value, Is.EqualTo(end.TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
                Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
                Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            });
        });

        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start.Value, Is.EqualTo(expectedStart));
            Assert.That(model.Model.Range.End, Is.EqualTo(end), "The untouched right edge must not move on commit.");
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)),
                "Normal left-edge resize remains geometry-only.");
        });
    }

    [AvaloniaTest]
    public async Task RollDrag_LocalDeceleratingBack_ClampsPreviewAndCommitToSourceEnd()
    {
        using var configuration = new EditorConfigScope(clamp: true);
        SceneSound sound = CreateSound(9, 0, 300);
        var speed = new KeyFrameAnimation<float> { UseGlobalClock = { Value = false } };
        speed.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.Zero },
            Value = { Value = 300 },
            Easing = { Value = new LinearEasing() }
        });
        speed.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(4) },
            Value = { Value = 100 },
            Easing = { Value = new LinearEasing() }
        });
        sound.Speed.SetKeyFrameAnimation(speed);
        ElementViewModel back = await OpenElement(sound, startSeconds: 4, lengthSeconds: 4);
        TimelineTabViewModel timeline = back.Timeline;
        var adder = (IElementAdder)timeline.EditorContext.GetService(typeof(IElementAdder))!;
        ElementAddResult added = await adder.AddAsync([new ElementDescription(
            Start: MediaTime.Zero, Length: MediaTime.FromSeconds(4), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);
        HeadlessTestHelpers.Settle();
        ElementViewModel front = timeline.GetViewModelFor(back.Scene.Children.Single(e => e != back.Model))!;
        front.Model.Name.Value = "Roll front";
        back.Model.Name.Value = "Local speed 300% to 100%";
        timeline.ClearSelected();
        timeline.SelectElement(front);
        timeline.IsRollMode.Value = true;
        try
        {
            using var view = OpenTimelineView(front, width: 1600);
            Border border = view.Border;
            float scale = view.Scale;
            Point press = border.TranslatePoint(
                new Point(border.Bounds.Width - 2, border.Bounds.Height / 2), view.Window)!.Value;
            Point release = press.WithX(press.X + MediaTime.FromSeconds(2).TimeToPixel(scale));
            double previewDeltaSeconds = 0;

            view.Drag(press, release, RawInputModifiers.Alt, () =>
            {
                CapturePreview(view.Window, "roll-local-decelerating-back");
                previewDeltaSeconds = back.BorderMargin.Value.Left.PixelToTime(scale).ToSeconds() - 4;

                // F(t) = 3t - t²/4. After rolling by d the local clock restarts, so
                // F(d) + F(4-d) = 8 + 2d - d²/2 must stay <= 9: d <= 2-sqrt(2).
                Assert.Multiple(() =>
                {
                    Assert.That(previewDeltaSeconds, Is.EqualTo(2 - Math.Sqrt(2)).Within(0.001));
                    Assert.That(front.Width.Value, Is.EqualTo(back.BorderMargin.Value.Left).Within(0.0001));
                    Assert.That(back.BorderMargin.Value.Left + back.Width.Value,
                        Is.EqualTo(MediaTime.FromSeconds(8).TimeToPixel(scale)).Within(0.0001));
                    Assert.That(front.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
                    Assert.That(back.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
                    Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
                });
            });

            using var integrator = new SpeedIntegrator(44100);
            integrator.EnsureCache(speed);
            MediaTime sourceEnd = sound.OffsetPosition.Value + integrator.Integrate(back.Model.Length.Value, speed);
            Assert.Multiple(() =>
            {
                Assert.That((back.Model.Start.Value - MediaTime.FromSeconds(4)).ToSeconds(),
                    Is.EqualTo(previewDeltaSeconds).Within(0.000001), "The committed cut must match the drag preview.");
                Assert.That(front.Model.Range.End, Is.EqualTo(back.Model.Start.Value));
                Assert.That(back.Model.Range.End, Is.EqualTo(MediaTime.FromSeconds(8)));
                Assert.That(sound.OffsetPosition.Value, Is.GreaterThan(MediaTime.Zero));
                Assert.That(sourceEnd, Is.LessThanOrEqualTo(MediaTime.FromSeconds(9)),
                    "The applied offset plus the restarted local speed integral must fit the source.");
            });
        }
        finally
        {
            timeline.IsRollMode.Value = false;
        }
    }

    [AvaloniaTest]
    public async Task OriginalDuration_FrozenMedia_RemainsAvailable()
    {
        using var configuration = new EditorConfigScope();
        ElementViewModel model = await OpenElement(CreateSound(5, 0, 0));
        Assert.That(model.HasOriginalDuration(), Is.True);
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);
        Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(5)));
    }

    [AvaloniaTest]
    public async Task OriginalDuration_ExtensionProvider_RemainsAvailable()
    {
        using var configuration = new EditorConfigScope();
        ElementViewModel model = await OpenElement(new DurationProviderObject());
        Assert.That(model.HasOriginalDuration(), Is.True);
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);
        Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
    }

    [SuppressResourceClassGeneration]
    private sealed class DurationProviderObject : EngineObject, IOriginalDurationProvider
    {
        public bool HasOriginalDuration() => true;

        public bool TryGetOriginalDuration(out MediaTime duration)
        {
            duration = MediaTime.FromSeconds(3);
            return true;
        }
    }

    [AvaloniaTest]
    public async Task OriginalDuration_NeighbourCap_RemainsSafeWhenEdgeClampingIsDisabled()
    {
        using var configuration = new EditorConfigScope(clamp: false);
        ElementViewModel model = await OpenElement(CreateDurationVideo(), startSeconds: 5);
        model.Model.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            OffsetPosition = { Value = MediaTime.FromSeconds(4) },
            Loop = { Value = true }
        });
        var adder = (IElementAdder)model.Timeline.EditorContext.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([new ElementDescription(
            Start: MediaTime.FromSeconds(6.5), Length: MediaTime.FromSeconds(1), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None)).IsSuccess, Is.True);
        HeadlessTestHelpers.Settle();
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);
        Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1)),
            "The neighbour's 1.5s limit is an unsafe loop period and must be revalidated.");
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EdgeDrag_LaterSafeLoopPhase_IsNotLimitedByAnEarlierFailedProbe(bool leftEdge)
    {
        using var configuration = new EditorConfigScope(clamp: true);
        ElementViewModel model = await OpenElement(CreateDurationVideo(), startSeconds: 5);
        model.Model.Objects.Add(new DrawableTimeController
        {
            Speed = { Value = 50 },
            OffsetPosition = { Value = MediaTime.FromSeconds(leftEdge ? 4 : 10) },
            Loop = { Value = true }
        });
        var timeline = model.Timeline;
        timeline.ClearSelected();
        timeline.SelectElement(model);
        using var view = OpenTimelineView(model, width: 1600);
        Border border = view.Border;
        float scale = view.Scale;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(MediaTime.FromSeconds(leftEdge ? -1.8 : 1.6).TimeToPixel(scale), y), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, leftEdge ? "later-safe-left-loop-phase" : "later-safe-loop-phase");
            Assert.That(model.Width.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 2 : 1.6).TimeToPixel(scale)).Within(0.0001));
        });
        Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 2 : 1.6)));
        Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 4 : 5)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EdgeDrag_PortalBecomesActive_KeepsRequestedDuration(bool leftEdge)
    {
        using var configuration = new EditorConfigScope(clamp: true, snap: false);
        ElementViewModel model = await OpenElement(new DrawableTimeController { Speed = { Value = 50 } },
            startSeconds: leftEdge ? 4 : 1, lengthSeconds: leftEdge ? 1 : 0.5);
        model.Model.Objects.Insert(0, new PortalObject { Count = { Value = 1 } });
        var timeline = model.Timeline;
        var adder = (IElementAdder)timeline.EditorContext.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([new ElementDescription(
            Start: MediaTime.FromSeconds(leftEdge ? 0 : 2), Length: MediaTime.FromSeconds(leftEdge ? 3 : 4), Layer: 1,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None)).IsSuccess, Is.True);
        Element owner = model.Scene.Children.Single(e => e != model.Model);
        owner.Objects.Clear();
        owner.Objects.Add(CreateDurationVideo());
        owner.Name.Value = "Portal video provider";
        model.Model.Name.Value = "Portal consumer";
        timeline.ClearSelected();
        timeline.SelectElement(model);
        HeadlessTestHelpers.Settle();
        using var view = OpenTimelineView(model, width: 1600);
        Border border = view.Border;
        float scale = view.Scale;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(MediaTime.FromSeconds(leftEdge ? -4 : 3).TimeToPixel(scale), y), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"portal-new-overlap-left-{leftEdge}");
            Assert.That(model.Width.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 5 : 3).TimeToPixel(scale)).Within(0.0001));
        });
        Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 0 : 1)));
        Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 5 : 3)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EdgeDrag_DisabledFlowController_UsesCurrentPlaybackBounds(bool leftEdge)
    {
        using var configuration = new EditorConfigScope(clamp: true, snap: false);
        ElementViewModel model = await OpenElement(CreateDurationVideo(), startSeconds: leftEdge ? 2 : 1, lengthSeconds: 0.5);
        var controller = new DrawableTimeController { IsEnabled = { Value = false }, OffsetPosition = { Value = MediaTime.FromSeconds(0.2) } };
        model.Model.Objects.Add(controller);
        var timeline = model.Timeline;
        timeline.ClearSelected();
        timeline.SelectElement(model);
        using var view = OpenTimelineView(model, width: 1600);
        Border border = view.Border;
        float scale = view.Scale;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(MediaTime.FromSeconds(leftEdge ? -1 : 1.5).TimeToPixel(scale), y), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"disabled-flow-controller-left-{leftEdge}");
            Assert.Multiple(() =>
            {
                Assert.That(model.Width.Value, Is.EqualTo(MediaTime.FromSeconds(1).TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)), "Preview must not mutate the element.");
            });
        });
        using var compositor = new SceneCompositor(model.Scene) { DisableResourceShare = true, ForceOriginalSource = true };
        var video = compositor.EvaluateGraphics(model.Model.Range.End - OneFrameAt30).Objects.OfType<SourceVideo.Resource>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 1.5 : 1)));
            Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(video.RequestedPosition + video.OffsetPosition, Is.LessThanOrEqualTo(MediaTime.FromSeconds(1)));
        });
        controller.IsEnabled.Value = true;
        Assert.That(SlippableMedia.GetOriginalDuration(model.Model), Is.EqualTo(MediaTime.FromSeconds(0.8)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EdgeDrag_CountTenPortal_PreviewsCommitsAndUndoesRequestedGeometry(bool leftEdge)
    {
        using var configuration = new EditorConfigScope(clamp: true, snap: false);
        ElementViewModel model = await OpenElement(new PortalObject { Count = { Value = 10 } }, 2, 3);
        var editor = (EditViewModel)model.Timeline.EditorContext;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementDescription[] providers = Enumerable.Range(1, 2).Select(layer => new ElementDescription(
            Start: MediaTime.FromSeconds(2), Length: MediaTime.FromSeconds(3), Layer: layer,
            Source: new ElementSource.EngineObject(() => new RectShape()))).ToArray();
        Assert.That((await adder.AddAsync(providers, CancellationToken.None)).IsSuccess, Is.True);
        model.Model.Name.Value = "Portal Count=10";
        editor.HistoryManager.Commit("Set up portal resize");
        model.Timeline.ClearSelected();
        model.Timeline.SelectElement(model);
        HeadlessTestHelpers.Settle();
        using var view = OpenTimelineView(model, width: 1600);
        Border border = view.Border;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(MediaTime.FromSeconds(leftEdge ? -1 : 5).TimeToPixel(view.Scale), y), view.Window)!.Value;
        MediaTime length = MediaTime.FromSeconds(leftEdge ? 4 : 5);
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"portal-count10-left-{leftEdge}");
            Assert.That(model.Width.Value, Is.EqualTo(length.TimeToPixel(view.Scale)).Within(0.0001));
            Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 1 : 2)));
            Assert.That(model.Model.Length.Value, Is.EqualTo(length));
            Assert.That(model.HasOriginalDuration(), Is.False);
            Assert.That(model.Scene.Children.Where(child => child != model.Model).Select(child => child.Length.Value),
                Is.All.EqualTo(MediaTime.FromSeconds(3)));
        });
        editor.HistoryManager.Undo();
        Assert.That(model.Model.Range, Is.EqualTo(new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(3))));
        int undoCount = editor.HistoryManager.UndoCount;
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle();
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undoCount));
    }

    private static SourceVideo CreateDurationVideo()
    {
        DecoderRegistry.Register(new DurationVideoDecoder());
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"duration-{Guid.NewGuid():N}.trim-duration-video");
        File.WriteAllBytes(path, []);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        return new SourceVideo { Source = { Value = source } };
    }

    private sealed class DurationVideoDecoder : IDecoderInfo
    {
        public string Name => "Trim duration test decoder";
        public IEnumerable<string> VideoExtensions() => [".trim-duration-video"];
        public IEnumerable<string> AudioExtensions() => [];
        public MediaReader? Open(string file, MediaOptions options)
            => Path.GetExtension(file) == ".trim-duration-video" ? new DurationVideoReader() : null;
    }

    private sealed class DurationVideoReader : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new("test", 30, new Beutl.Media.PixelSize(80, 80), new Rational(30, 1));
        public override AudioStreamInfo AudioInfo => throw new NotSupportedException();
        public override bool HasVideo => true;
        public override bool HasAudio => false;
        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Beutl.Media.Bitmap>? image) { image = null; return false; }
        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound) { sound = null; return false; }
    }

    private static MediaTime ExpectedLength(double seconds)
        => seconds == 0 ? OneFrameAt30 : MediaTime.FromSeconds(seconds);

    private static SceneSound CreateSound(double? sourceSeconds, double offsetSeconds, float speed)
        => new()
        {
            ReferencedScene =
            {
                Value = sourceSeconds is { } seconds ? new Scene { Duration = { Value = MediaTime.FromSeconds(seconds) } } : null
            },
            OffsetPosition = { Value = MediaTime.FromSeconds(offsetSeconds) },
            Speed = { Value = speed }
        };

    [AvaloniaTest]
    public async Task Move_GlobalSpeed_PreservesPreviewWidthOnRelease()
    {
        using var configuration = new EditorConfigScope(clamp: true, snap: false);
        SceneSound sound = CreateSound(3, 0, 100);
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = true } };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 100 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(10) }, Value = { Value = 200 } });
        sound.Speed.SetKeyFrameAnimation(animation);
        ElementViewModel model = await OpenElement(sound, lengthSeconds: 2);
        TimelineTabViewModel timeline = model.Timeline;
        timeline.Options.Value = timeline.Options.Value with { Scale = 0.5f };
        timeline.ClearSelected();
        timeline.SelectElement(model);
        using var view = OpenTimelineView(model, width: 1200);
        Border border = view.Border;
        float scale = view.Scale;
        double width = model.Width.Value;
        Point press = border.TranslatePoint(new Point(border.Bounds.Width / 2, border.Bounds.Height / 2), view.Window)!.Value;
        Point release = press + new Avalonia.Vector(MediaTime.FromSeconds(6).TimeToPixel(scale), 0);
        view.Drag(press, release, RawInputModifiers.None, () =>
        {
            CapturePreview(view.Window, "move-global-speed");
            Assert.That(model.Width.Value, Is.EqualTo(width).Within(0.0001));
        });
        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(model.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
        HeadlessTestHelpers.Render(5);
        CapturePreview(view.Window, "move-global-speed-committed");
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task GroupedResize_SharedTargetOwner_PreviewsAndCommitsTheSameEdge(bool leftEdge, bool subFrameStarts)
    {
        using var configuration = new EditorConfigScope(clamp: true);
        SourceVideo video = CreateDurationVideo();
        video.Speed.Value = 200;
        ElementViewModel owner = await OpenElement(video, lengthSeconds: 0.4);
        owner.Model.ZIndex.Value = 1;
        TimelineTabViewModel timeline = owner.Timeline;
        var adder = (IElementAdder)timeline.EditorContext.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([new ElementDescription(
            Start: owner.Model.Start.Value, Length: owner.Model.Length.Value, Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None)).IsSuccess, Is.True);
        Element peer = owner.Scene.Children.Single(e => e != owner.Model);
        Assert.That(peer.Range, Is.EqualTo(owner.Model.Range));
        if (subFrameStarts)
        {
            MediaTime end = owner.Model.Range.End;
            owner.Model.Start.Value = MediaTime.FromTimeSpan(TimeSpan.FromTicks(10050000));
            owner.Model.Length.Value = end - owner.Model.Start.Value;
            peer.Start.Value = MediaTime.FromTimeSpan(TimeSpan.FromTicks(10200000));
            peer.Length.Value = end - peer.Start.Value;
        }
        peer.Objects.Clear();
        peer.Objects.Add(new PortalObject { Count = { Value = 1 } });
        peer.Objects.Add(new DrawableTimeController { Reverse = { Value = true } });
        peer.Name.Value = "Shared reversed video";
        HeadlessTestHelpers.Settle();
        ElementViewModel presented = timeline.GetViewModelFor(peer)!;
        timeline.ClearSelected();
        timeline.SelectElement(owner);
        timeline.SelectElement(presented);
        owner.GroupSelectedElements.Execute();
        HeadlessTestHelpers.Settle();
        Assert.That(presented.GetGroupOrSelectedElements(), Has.Count.EqualTo(2));
        using var view = OpenTimelineView(presented, width: 1200);
        Border border = view.Border;
        float scale = view.Scale;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, border.Bounds.Height / 2), view.Window)!.Value;
        double releaseX = MediaTime.FromSeconds(leftEdge ? -0.35 : 0.75).TimeToPixel(scale);
        Point release = border.TranslatePoint(new Point(releaseX, border.Bounds.Height / 2), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"grouped-shared-clock-left-{leftEdge}-subframe-{subFrameStarts}");
            Assert.Multiple(() =>
            {
                Assert.That(owner.Width.Value, Is.EqualTo(MediaTime.FromSeconds(0.5).TimeToPixel(scale)).Within(0.0001));
                Assert.That(presented.BorderMargin.Value.Left + presented.Width.Value,
                    Is.EqualTo(owner.BorderMargin.Value.Left + owner.Width.Value).Within(0.0001));
            });
        });
        Assert.Multiple(() =>
        {
            Assert.That(owner.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(subFrameStarts ? 0.495 : 0.5)));
            Assert.That(owner.Model.Start.Value, Is.EqualTo(subFrameStarts ? MediaTime.FromTimeSpan(TimeSpan.FromTicks(10050000)) : MediaTime.FromSeconds(leftEdge ? 0.9 : 1)));
            Assert.That(peer.Start.Value, Is.EqualTo(subFrameStarts ? MediaTime.FromTimeSpan(TimeSpan.FromTicks(10200000)) : MediaTime.FromSeconds(leftEdge ? 0.9 : 1)));
            Assert.That(peer.Range.End, Is.EqualTo(owner.Model.Range.End));
        });
        HeadlessTestHelpers.Render(5);
        CapturePreview(view.Window, $"grouped-shared-clock-left-{leftEdge}-subframe-{subFrameStarts}-committed");
    }

    [AvaloniaTest]
    [TestCase("controller")]
    [TestCase("source")]
    [TestCase("presenter")]
    public async Task OriginalDuration_UnsupportedMapping_IsUnavailableAndPreservesGeometry(string kind)
    {
        using var configuration = new EditorConfigScope();
        SourceVideo video = CreateDurationVideo();
        EngineObject root = video;
        if (kind == "source") video.Source.Expression = Expression.Create<VideoSource?>("null");
        if (kind == "presenter")
            root = new DrawablePresenter
            {
                Target = { Value = video, Expression = Expression.Create<Drawable?>("null") }
            };
        ElementViewModel model = await OpenElement(root, lengthSeconds: 2);
        if (kind == "controller")
            model.Model.Objects.Add(new DrawableTimeController { Speed = { Expression = Expression.Create<float>("200") } });
        var editor = (EditViewModel)model.Timeline.EditorContext;
        editor.HistoryManager.Commit();
        int undo = editor.HistoryManager.UndoCount;
        TimeRange before = model.Model.Range;
        bool available = model.HasOriginalDuration();

        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);

        Assert.Multiple(() =>
        {
            Assert.That(available, Is.False);
            Assert.That(SlippableMedia.GetOriginalDuration(model.Model), Is.Null);
            Assert.That(model.Model.Range, Is.EqualTo(before));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(undo));
        });
    }

    private static async Task<ElementViewModel> OpenElement(EngineObject media, double startSeconds = 1, double lengthSeconds = 1)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("timeline-media-duration");
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        ElementAddResult added = await adder.AddAsync([new ElementDescription(
            Start: MediaTime.FromSeconds(startSeconds), Length: MediaTime.FromSeconds(lengthSeconds), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);

        // Keep the referenced fixture scene in memory; this test does not exercise serialization.
        Element element = scene.Children.Single();
        element.Objects.Clear();
        element.Objects.Add(media);
        element.Name.Value = "Speed-aware media duration";
        scene.Duration.Value = MediaTime.FromSeconds(10);
        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        HeadlessTestHelpers.Settle();
        return timeline.GetViewModelFor(element)!;
    }

    // Turns ripple editing off and applies the given clamp and snap settings. Dispose restores all three.
    private sealed class EditorConfigScope : IDisposable
    {
        private readonly bool _ripple = GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value;
        private readonly bool _clamp = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength.Value;
        private readonly bool _snap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled.Value;

        public EditorConfigScope(bool? clamp = null, bool? snap = null)
        {
            EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
            config.IsRippleEnabled.Value = false;
            if (clamp is { } clampValue)
                config.ClampResizeToOriginalLength.Value = clampValue;
            if (snap is { } snapValue)
                config.IsTimelineSnapEnabled.Value = snapValue;
        }

        public void Dispose()
        {
            EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
            config.IsRippleEnabled.Value = _ripple;
            config.ClampResizeToOriginalLength.Value = _clamp;
            config.IsTimelineSnapEnabled.Value = _snap;
        }
    }

    private static TimelineViewScope OpenTimelineView(ElementViewModel target, int width) => new(target, width);

    // The timeline in a shown window, with the ElementView and border of one element laid out.
    private sealed class TimelineViewScope : IDisposable
    {
        private readonly TimelineTabView _view;

        public TimelineViewScope(ElementViewModel target, int width)
        {
            _view = new TimelineTabView { DataContext = target.Timeline };
            Window = new Window { Content = _view, Width = width, Height = 420 };
            try
            {
                Window.Show();
                HeadlessTestHelpers.Render(5);
                Element = _view.GetVisualDescendants().OfType<ElementView>()
                    .Single(v => ReferenceEquals(v.DataContext, target));
                Border = Element.FindControl<Border>("border")!;
                Scale = target.Timeline.Options.Value.Scale;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Window Window { get; }

        public ElementView Element { get; }

        public Border Border { get; }

        public float Scale { get; }

        // Hovering first establishes the resize handle under the press; Alt bypasses timeline snapping.
        public void Drag(Point press, Point release, RawInputModifiers modifiers, Action whilePressed)
        {
            Window.MouseMove(press, modifiers);
            Window.MouseDown(press, MouseButton.Left, modifiers);
            try
            {
                Window.MouseMove(release, modifiers | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render(5);
                whilePressed();
            }
            finally
            {
                Window.MouseUp(release, MouseButton.Left, modifiers);
                HeadlessTestHelpers.Settle(4);
            }
        }

        public void Dispose()
        {
            _view.DataContext = null;
            Window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static void CapturePreview(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_TRIM_DURATION_CAPTURE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null, "The real headless renderer must produce a frame.");
        frame!.Save(Path.Combine(directory, $"{name}-preview.png"), PngBitmapEncoderOptions.Default);
    }
}
