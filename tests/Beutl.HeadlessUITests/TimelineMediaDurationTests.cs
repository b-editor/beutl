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
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
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
    private static readonly TimeSpan OneFrameAt30 = TimeSpan.FromTicks(333334);

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
            Assert.That(model.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(model.Model.Length, Is.EqualTo(ExpectedLength(expectedSeconds)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(offsetSeconds)),
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
            Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(slower.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(faster.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
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
        Point release = border.TranslatePoint(new Point(TimeSpan.FromSeconds(4).TimeToPixel(scale), y), view.Window)!.Value;

        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"source-{sourceSeconds?.ToString() ?? "unknown"}-offset-{offsetSeconds}-clamp-{clamp}");

            Assert.Multiple(() =>
            {
                Assert.That(model.Width.Value,
                    Is.EqualTo(ExpectedLength(expectedSeconds).TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(1)),
                    "Dragging previews geometry without writing the element.");
                Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(offsetSeconds)));
            });
        });

        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(model.Model.Length, Is.EqualTo(ExpectedLength(expectedSeconds)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(offsetSeconds)),
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
            TimeSpan.FromSeconds(requestedStartSeconds).TimeToPixel(scale), element.Bounds.Height / 2), view.Window)!.Value;
        TimeSpan expectedStart = TimeSpan.FromSeconds(expectedStartSeconds);
        TimeSpan end = TimeSpan.FromSeconds(7);

        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"left-source-{sourceSeconds}-clamp-{clamp}");

            Assert.Multiple(() =>
            {
                Assert.That(model.BorderMargin.Value.Left, Is.EqualTo(expectedStart.TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Width.Value, Is.EqualTo((end - expectedStart).TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.BorderMargin.Value.Left + model.Width.Value, Is.EqualTo(end.TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            });
        });

        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start, Is.EqualTo(expectedStart));
            Assert.That(model.Model.Range.End, Is.EqualTo(end), "The untouched right edge must not move on commit.");
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)),
                "Normal left-edge resize remains geometry-only.");
        });
    }

    [AvaloniaTest]
    public async Task RollDrag_LocalDeceleratingBack_ClampsPreviewAndCommitToSourceEnd()
    {
        using var configuration = new EditorConfigScope(clamp: true);
        SceneSound sound = CreateSound(9, 0, 300);
        var speed = new KeyFrameAnimation<float> { UseGlobalClock = false };
        speed.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 300,
            Easing = new LinearEasing()
        });
        speed.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(4),
            Value = 100,
            Easing = new LinearEasing()
        });
        sound.Speed.Animation = speed;
        ElementViewModel back = await OpenElement(sound, startSeconds: 4, lengthSeconds: 4);
        TimelineTabViewModel timeline = back.Timeline;
        var adder = (IElementAdder)timeline.EditorContext.GetService(typeof(IElementAdder))!;
        ElementAddResult added = await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.Zero, Length: TimeSpan.FromSeconds(4), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);
        HeadlessTestHelpers.Settle();
        ElementViewModel front = timeline.GetViewModelFor(back.Scene.Children.Single(e => e != back.Model))!;
        front.Model.Name = "Roll front";
        back.Model.Name = "Local speed 300% to 100%";
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
            Point release = press.WithX(press.X + TimeSpan.FromSeconds(2).TimeToPixel(scale));
            double previewDeltaSeconds = 0;

            view.Drag(press, release, RawInputModifiers.Alt, () =>
            {
                CapturePreview(view.Window, "roll-local-decelerating-back");
                previewDeltaSeconds = back.BorderMargin.Value.Left.PixelToTimeSpan(scale).TotalSeconds - 4;

                // F(t) = 3t - t²/4. After rolling by d the local clock restarts, so
                // F(d) + F(4-d) = 8 + 2d - d²/2 must stay <= 9: d <= 2-sqrt(2).
                Assert.Multiple(() =>
                {
                    Assert.That(previewDeltaSeconds, Is.EqualTo(2 - Math.Sqrt(2)).Within(0.001));
                    Assert.That(front.Width.Value, Is.EqualTo(back.BorderMargin.Value.Left).Within(0.0001));
                    Assert.That(back.BorderMargin.Value.Left + back.Width.Value,
                        Is.EqualTo(TimeSpan.FromSeconds(8).TimeToPixel(scale)).Within(0.0001));
                    Assert.That(front.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(4)));
                    Assert.That(back.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(4)));
                    Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
                });
            });

            using var integrator = new SpeedIntegrator(44100);
            integrator.EnsureCache(speed);
            TimeSpan sourceEnd = sound.OffsetPosition.CurrentValue + integrator.Integrate(back.Model.Length, speed);
            Assert.Multiple(() =>
            {
                Assert.That((back.Model.Start - TimeSpan.FromSeconds(4)).TotalSeconds,
                    Is.EqualTo(previewDeltaSeconds).Within(0.000001), "The committed cut must match the drag preview.");
                Assert.That(front.Model.Range.End, Is.EqualTo(back.Model.Start));
                Assert.That(back.Model.Range.End, Is.EqualTo(TimeSpan.FromSeconds(8)));
                Assert.That(sound.OffsetPosition.CurrentValue, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(sourceEnd, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(9)),
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
        Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [AvaloniaTest]
    public async Task OriginalDuration_ExtensionProvider_RemainsAvailable()
    {
        using var configuration = new EditorConfigScope();
        ElementViewModel model = await OpenElement(new DurationProviderObject());
        Assert.That(model.HasOriginalDuration(), Is.True);
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);
        Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [SuppressResourceClassGeneration]
    private sealed class DurationProviderObject : EngineObject, IOriginalDurationProvider
    {
        public bool HasOriginalDuration() => true;

        public bool TryGetOriginalDuration(out TimeSpan duration)
        {
            duration = TimeSpan.FromSeconds(3);
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
            Speed = { CurrentValue = 50 },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(4) },
            Loop = { CurrentValue = true }
        });
        var adder = (IElementAdder)model.Timeline.EditorContext.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.FromSeconds(6.5), Length: TimeSpan.FromSeconds(1), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None)).IsSuccess, Is.True);
        HeadlessTestHelpers.Settle();
        model.ChangeToOriginalDuration.Execute();
        HeadlessTestHelpers.Settle(4);
        Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(1)),
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
            Speed = { CurrentValue = 50 },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(leftEdge ? 4 : 10) },
            Loop = { CurrentValue = true }
        });
        var timeline = model.Timeline;
        timeline.ClearSelected();
        timeline.SelectElement(model);
        using var view = OpenTimelineView(model, width: 1600);
        Border border = view.Border;
        float scale = view.Scale;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(TimeSpan.FromSeconds(leftEdge ? -1.8 : 1.6).TimeToPixel(scale), y), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, leftEdge ? "later-safe-left-loop-phase" : "later-safe-loop-phase");
            Assert.That(model.Width.Value, Is.EqualTo(TimeSpan.FromSeconds(leftEdge ? 2 : 1.6).TimeToPixel(scale)).Within(0.0001));
        });
        Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(leftEdge ? 2 : 1.6)));
        Assert.That(model.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(leftEdge ? 4 : 5)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EdgeDrag_PortalBecomesActive_PreviewsAndCommitsSourceBounds(bool leftEdge)
    {
        using var configuration = new EditorConfigScope(clamp: true, snap: false);
        ElementViewModel model = await OpenElement(new DrawableTimeController { Speed = { CurrentValue = 50 } },
            startSeconds: leftEdge ? 4 : 1, lengthSeconds: leftEdge ? 1 : 0.5);
        model.Model.Objects.Insert(0, new PortalObject { Count = { CurrentValue = 1 } });
        var timeline = model.Timeline;
        var adder = (IElementAdder)timeline.EditorContext.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([new ElementDescription(
            Start: TimeSpan.FromSeconds(leftEdge ? 0 : 2), Length: TimeSpan.FromSeconds(leftEdge ? 3 : 4), Layer: 1,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None)).IsSuccess, Is.True);
        Element owner = model.Scene.Children.Single(e => e != model.Model);
        owner.Objects.Clear();
        owner.Objects.Add(CreateDurationVideo());
        owner.Name = "Portal video provider";
        model.Model.Name = "Portal consumer";
        timeline.ClearSelected();
        timeline.SelectElement(model);
        HeadlessTestHelpers.Settle();
        using var view = OpenTimelineView(model, width: 1600);
        Border border = view.Border;
        float scale = view.Scale;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(TimeSpan.FromSeconds(leftEdge ? -4 : 3).TimeToPixel(scale), y), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"portal-new-overlap-left-{leftEdge}");
            Assert.That(model.Width.Value, Is.EqualTo(TimeSpan.FromSeconds(leftEdge ? 4 : 2).TimeToPixel(scale)).Within(0.0001));
        });
        Assert.That(model.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(leftEdge ? 4 : 2)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EdgeDrag_DisabledFlowController_PreservesSafeBoundsForReenabling(bool leftEdge)
    {
        using var configuration = new EditorConfigScope(clamp: true, snap: false);
        ElementViewModel model = await OpenElement(CreateDurationVideo(), startSeconds: leftEdge ? 2 : 1, lengthSeconds: 0.5);
        var controller = new DrawableTimeController { IsEnabled = false, OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(0.2) } };
        model.Model.Objects.Add(controller);
        var timeline = model.Timeline;
        timeline.ClearSelected();
        timeline.SelectElement(model);
        using var view = OpenTimelineView(model, width: 1600);
        Border border = view.Border;
        float scale = view.Scale;
        double y = border.Bounds.Height / 2;
        Point press = border.TranslatePoint(new Point(leftEdge ? 2 : border.Bounds.Width - 2, y), view.Window)!.Value;
        Point release = border.TranslatePoint(new Point(TimeSpan.FromSeconds(leftEdge ? -1 : 1.5).TimeToPixel(scale), y), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"disabled-flow-controller-left-{leftEdge}");
            Assert.Multiple(() =>
            {
                Assert.That(model.Width.Value, Is.EqualTo(TimeSpan.FromSeconds(0.8).TimeToPixel(scale)).Within(0.0001));
                Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(0.5)), "Preview must not mutate the element.");
            });
        });
        controller.IsEnabled = true;
        using var compositor = new SceneCompositor(model.Scene) { DisableResourceShare = true, ForceOriginalSource = true };
        var resource = compositor.EvaluateGraphics(model.Model.Range.End - OneFrameAt30).Objects.OfType<DrawableTimeController.Resource>().Single();
        var video = (SourceVideo.Resource)resource.Target!;
        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(leftEdge ? 1.7 : 1)));
            Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(0.8)));
            Assert.That(video.RequestedPosition + video.OffsetPosition, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    private static SourceVideo CreateDurationVideo()
    {
        DecoderRegistry.Register(new DurationVideoDecoder());
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"duration-{Guid.NewGuid():N}.trim-duration-video");
        File.WriteAllBytes(path, []);
        var source = new VideoSource();
        source.ReadFrom(new Uri(path));
        return new SourceVideo { Source = { CurrentValue = source } };
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

    private static TimeSpan ExpectedLength(double seconds)
        => seconds == 0 ? OneFrameAt30 : TimeSpan.FromSeconds(seconds);

    private static SceneSound CreateSound(double? sourceSeconds, double offsetSeconds, float speed)
        => new()
        {
            ReferencedScene =
            {
                CurrentValue = sourceSeconds is { } seconds ? new Scene { Duration = TimeSpan.FromSeconds(seconds) } : null
            },
            OffsetPosition = { CurrentValue = TimeSpan.FromSeconds(offsetSeconds) },
            Speed = { CurrentValue = speed }
        };

    [AvaloniaTest]
    public async Task Move_GlobalSpeed_PreservesPreviewWidthOnRelease()
    {
        using var configuration = new EditorConfigScope(clamp: true, snap: false);
        SceneSound sound = CreateSound(3, 0, 100);
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = true };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(10), Value = 200 });
        sound.Speed.Animation = animation;
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
        Point release = press + new Avalonia.Vector(TimeSpan.FromSeconds(6).TimeToPixel(scale), 0);
        view.Drag(press, release, RawInputModifiers.None, () =>
        {
            CapturePreview(view.Window, "move-global-speed");
            Assert.That(model.Width.Value, Is.EqualTo(width).Within(0.0001));
        });
        Assert.Multiple(() =>
        {
            Assert.That(model.Model.Start, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(model.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(sound.OffsetPosition.CurrentValue, Is.EqualTo(TimeSpan.Zero));
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
        video.Speed.CurrentValue = 200;
        ElementViewModel owner = await OpenElement(video, lengthSeconds: 0.4);
        owner.Model.ZIndex = 1;
        TimelineTabViewModel timeline = owner.Timeline;
        var adder = (IElementAdder)timeline.EditorContext.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([new ElementDescription(
            Start: owner.Model.Start, Length: owner.Model.Length, Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None)).IsSuccess, Is.True);
        Element peer = owner.Scene.Children.Single(e => e != owner.Model);
        Assert.That(peer.Range, Is.EqualTo(owner.Model.Range));
        if (subFrameStarts)
        {
            TimeSpan end = owner.Model.Range.End;
            owner.Model.Start = TimeSpan.FromTicks(10050000);
            owner.Model.Length = end - owner.Model.Start;
            peer.Start = TimeSpan.FromTicks(10200000);
            peer.Length = end - peer.Start;
        }
        peer.Objects.Clear();
        peer.Objects.Add(new PortalObject { Count = { CurrentValue = 1 } });
        peer.Objects.Add(new DrawableTimeController { Reverse = { CurrentValue = true } });
        peer.Name = "Shared reversed video";
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
        double releaseX = TimeSpan.FromSeconds(leftEdge ? -0.35 : 0.75).TimeToPixel(scale);
        Point release = border.TranslatePoint(new Point(releaseX, border.Bounds.Height / 2), view.Window)!.Value;
        view.Drag(press, release, RawInputModifiers.Alt, () =>
        {
            CapturePreview(view.Window, $"grouped-shared-clock-left-{leftEdge}-subframe-{subFrameStarts}");
            Assert.Multiple(() =>
            {
                Assert.That(owner.Width.Value, Is.EqualTo(TimeSpan.FromSeconds(0.5).TimeToPixel(scale)).Within(0.0001));
                Assert.That(presented.BorderMargin.Value.Left + presented.Width.Value,
                    Is.EqualTo(owner.BorderMargin.Value.Left + owner.Width.Value).Within(0.0001));
            });
        });
        Assert.Multiple(() =>
        {
            Assert.That(owner.Model.Length, Is.EqualTo(TimeSpan.FromSeconds(subFrameStarts ? 0.495 : 0.5)));
            Assert.That(owner.Model.Start, Is.EqualTo(subFrameStarts ? TimeSpan.FromTicks(10050000) : TimeSpan.FromSeconds(leftEdge ? 0.9 : 1)));
            Assert.That(peer.Start, Is.EqualTo(subFrameStarts ? TimeSpan.FromTicks(10200000) : TimeSpan.FromSeconds(leftEdge ? 0.9 : 1)));
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
                Target = { CurrentValue = video, Expression = Expression.Create<Drawable?>("null") }
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
            Start: TimeSpan.FromSeconds(startSeconds), Length: TimeSpan.FromSeconds(lengthSeconds), Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Assert.That(added.IsSuccess, Is.True);

        // Keep the referenced fixture scene in memory; this test does not exercise serialization.
        Element element = scene.Children.Single();
        element.Objects.Clear();
        element.Objects.Add(media);
        element.Name = "Speed-aware media duration";
        scene.Duration = TimeSpan.FromSeconds(10);
        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        HeadlessTestHelpers.Settle();
        return timeline.GetViewModelFor(element)!;
    }

    // Turns ripple editing off and applies the given clamp and snap settings. Dispose restores all three.
    private sealed class EditorConfigScope : IDisposable
    {
        private readonly bool _ripple = GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled;
        private readonly bool _clamp = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        private readonly bool _snap = GlobalConfiguration.Instance.EditorConfig.IsTimelineSnapEnabled;

        public EditorConfigScope(bool? clamp = null, bool? snap = null)
        {
            EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
            config.IsRippleEnabled = false;
            if (clamp is { } clampValue)
                config.ClampResizeToOriginalLength = clampValue;
            if (snap is { } snapValue)
                config.IsTimelineSnapEnabled = snapValue;
        }

        public void Dispose()
        {
            EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
            config.IsRippleEnabled = _ripple;
            config.ClampResizeToOriginalLength = _clamp;
            config.IsTimelineSnapEnabled = _snap;
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
