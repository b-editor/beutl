using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
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
public class TimelineEditModeThumbnailPreviewTests
{
    [AvaloniaTest]
    public async Task RightEdgeResize_LocksContentWidthWithoutStretching_AndRestoresOnCancel()
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 6);
        using var view = new TimelineViewScope(clip, width: 1200);
        await WaitForWaveformReady(clip, view.Waveform);

        double initialViewportWidth = view.Waveform.Bounds.Width;
        int initialChunkCount = clip.WaveformChunkCount.Value;
        float scale = view.Scale;
        double y = view.Border.Bounds.Height / 2;
        Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width - 2, y), view.Window)!.Value;
        Point dragTo = view.Border.TranslatePoint(new Point(MediaTime.FromSeconds(3).TimeToPixel(scale), y), view.Window)!.Value;

        view.Window.MouseMove(press, RawInputModifiers.Alt);
        view.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.Alt);
        try
        {
            view.Window.MouseMove(dragTo, RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(5);

            Assert.Multiple(() =>
            {
                Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                Assert.That(clip.ThumbnailContentWidth.Value, Is.EqualTo(initialViewportWidth).Within(0.01),
                    "Waveform scale must stay anchored to its pre-drag width instead of stretching.");
                Assert.That(clip.ThumbnailContentOffsetX.Value, Is.Zero);
                Assert.That(clip.WaveformChunkCount.Value, Is.EqualTo(initialChunkCount),
                    "Chunk count must not be cleared mid-drag.");
            });

            // Wait for newly exposed chunks past initialChunkCount to be populated on demand.
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                view.Waveform.GetMissingIndices(initialChunkCount, initialChunkCount + 10).Count == 0);

            // Cancel with Escape: preview clears and out-of-range chunks are pruned while base chunks remain.
            view.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            HeadlessTestHelpers.Settle(4);

            Assert.Multiple(() =>
            {
                Assert.That(clip.IsThumbnailPreviewActive.Value, Is.False);
                Assert.That(double.IsNaN(clip.ThumbnailContentWidth.Value), Is.True);
                Assert.That(clip.ThumbnailContentOffsetX.Value, Is.Zero);
                Assert.That(view.Waveform.GetMissingIndices(0, initialChunkCount - 1), Is.Empty,
                    "Cancelling must keep the existing base waveform intact.");
                Assert.That(view.Waveform.GetMissingIndices(initialChunkCount, initialChunkCount + 5), Has.Count.EqualTo(6),
                    "Out-of-range preview chunks must be pruned on cancel.");
            });
        }
        finally
        {
            view.Window.MouseUp(dragTo, MouseButton.Left, RawInputModifiers.Alt);
            HeadlessTestHelpers.Settle(4);
        }
    }

    [AvaloniaTest]
    public async Task RollEdit_KeepsFrontAnchoredAndOffsetsBackInTimelineSpace()
    {
        using var config = new PreviewConfigScope();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("roll-thumbnail-preview");
        scene.Duration.Value = MediaTime.FromSeconds(12);
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([
            new ElementDescription(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(MediaTime.FromSeconds(3), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new RectShape()))
        ], CancellationToken.None)).IsSuccess, Is.True);

        Element frontModel = scene.Children[0];
        Element backModel = scene.Children[1];
        AttachAudio(frontModel, offsetSeconds: 1, sourceSeconds: 8);
        AttachAudio(backModel, offsetSeconds: 2, sourceSeconds: 8);

        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        HeadlessTestHelpers.Settle();

        ElementViewModel front = timeline.GetViewModelFor(frontModel)!;
        ElementViewModel back = timeline.GetViewModelFor(backModel)!;
        timeline.ClearSelected();
        timeline.SelectElement(front);
        timeline.IsRollMode.Value = true;
        try
        {
            using var view = new TimelineViewScope(front, width: 1200);
            WaveformControl frontWave = view.GetWaveformFor(front);
            WaveformControl backWave = view.GetWaveformFor(back);
            await WaitForWaveformReady(front, frontWave);
            await WaitForWaveformReady(back, backWave);

            double frontBaseWidth = frontWave.Bounds.Width;
            double backBaseWidth = backWave.Bounds.Width;
            float scale = view.Scale;
            double deltaPixels = MediaTime.FromSeconds(-0.5).TimeToPixel(scale);
            Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width - 2, view.Border.Bounds.Height / 2), view.Window)!.Value;
            Point release = press.WithX(press.X + deltaPixels);

            await view.DragAsync(press, release, RawInputModifiers.Alt, async () =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(front.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(front.ThumbnailContentWidth.Value, Is.EqualTo(frontBaseWidth).Within(0.01));
                    Assert.That(front.ThumbnailContentOffsetX.Value, Is.Zero);

                    Assert.That(back.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(back.ThumbnailContentWidth.Value, Is.EqualTo(backBaseWidth).Within(0.01));
                    // Moving the cut left by 0.5s exposes 0.5s on the left of Back -> positive ContentOffsetX = -deltaPixels.
                    Assert.That(back.ThumbnailContentOffsetX.Value, Is.EqualTo(-deltaPixels).Within(0.5));
                });

                // Wait for newly exposed negative chunks on Back to load while dragging.
                await HeadlessTestHelpers.WaitUntilAsync(() => backWave.GetMissingIndices(-10, -1).Count == 0);
            });

            // After commit, both clips refresh and clear preview transforms.
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                double.IsNaN(front.ThumbnailContentWidth.Value)
                && double.IsNaN(back.ThumbnailContentWidth.Value));
            Assert.Multiple(() =>
            {
                Assert.That(front.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
                Assert.That(back.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)));
                Assert.That(back.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)));
                Assert.That(front.ThumbnailContentOffsetX.Value, Is.Zero);
                Assert.That(back.ThumbnailContentOffsetX.Value, Is.Zero);
            });
        }
        finally
        {
            timeline.IsRollMode.Value = false;
        }
    }

    [AvaloniaTest]
    public async Task SlideEdit_PreviewsFrontAndBackWithoutModifyingMiddleThumbnails()
    {
        using var config = new PreviewConfigScope();
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("slide-thumbnail-preview");
        scene.Duration.Value = MediaTime.FromSeconds(14);
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([
            new ElementDescription(MediaTime.FromSeconds(0), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(MediaTime.FromSeconds(2), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new RectShape())),
            new ElementDescription(MediaTime.FromSeconds(4), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new RectShape()))
        ], CancellationToken.None)).IsSuccess, Is.True);

        AttachAudio(scene.Children[0], offsetSeconds: 1, sourceSeconds: 8);
        AttachAudio(scene.Children[1], offsetSeconds: 1, sourceSeconds: 8);
        AttachAudio(scene.Children[2], offsetSeconds: 2, sourceSeconds: 8);

        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        HeadlessTestHelpers.Settle();

        ElementViewModel front = timeline.GetViewModelFor(scene.Children[0])!;
        ElementViewModel middle = timeline.GetViewModelFor(scene.Children[1])!;
        ElementViewModel back = timeline.GetViewModelFor(scene.Children[2])!;
        timeline.ClearSelected();
        timeline.SelectElement(middle);
        timeline.IsSlideMode.Value = true;
        try
        {
            using var view = new TimelineViewScope(middle, width: 1400);
            await WaitForWaveformReady(front, view.GetWaveformFor(front));
            await WaitForWaveformReady(middle, view.GetWaveformFor(middle));
            await WaitForWaveformReady(back, view.GetWaveformFor(back));

            float scale = view.Scale;
            double deltaPixels = MediaTime.FromSeconds(0.5).TimeToPixel(scale);
            Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width / 2, view.Border.Bounds.Height / 2), view.Window)!.Value;
            Point release = press.WithX(press.X + deltaPixels);

            view.Drag(press, release, RawInputModifiers.Alt, () =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(front.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(front.ThumbnailContentOffsetX.Value, Is.Zero);
                    Assert.That(middle.IsThumbnailPreviewActive.Value, Is.False);
                    Assert.That(double.IsNaN(middle.ThumbnailContentWidth.Value), Is.True);
                    Assert.That(back.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(back.ThumbnailContentOffsetX.Value, Is.EqualTo(-deltaPixels).Within(0.5));
                });
            });

            await HeadlessTestHelpers.WaitUntilAsync(() =>
                double.IsNaN(front.ThumbnailContentWidth.Value)
                && double.IsNaN(back.ThumbnailContentWidth.Value));
            Assert.That(middle.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)));
        }
        finally
        {
            timeline.IsSlideMode.Value = false;
        }
    }

    [AvaloniaTest]
    public async Task SlipEdit_SlidesThumbnailContentClampedToSourceBounds_AndLoadsRevealedChunks()
    {
        using var config = new PreviewConfigScope();
        // 4s source, 2s clip, initial offset 1s -> headroom is [-1s, +1s].
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 4);
        var sound = clip.Model.Objects.OfType<SourceSound>().Single();
        clip.Timeline.ClearSelected();
        clip.Timeline.SelectElement(clip);
        clip.Timeline.IsSlipMode.Value = true;
        try
        {
            using var view = new TimelineViewScope(clip, width: 1200);
            await WaitForWaveformReady(clip, view.Waveform);

            double initialWidth = clip.Width.Value;
            double initialLeft = clip.BorderMargin.Value.Left;
            int initialChunkCount = clip.WaveformChunkCount.Value;
            float scale = view.Scale;
            Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width / 2, view.Border.Bounds.Height / 2), view.Window)!.Value;
            // Drag right by 3s — must clamp at +1s (source end), so ContentOffsetX == -1s in pixels.
            Point release = press.WithX(press.X + MediaTime.FromSeconds(3).TimeToPixel(scale));
            double expectedClampedOffsetX = -MediaTime.FromSeconds(1).TimeToPixel(scale);

            await view.DragAsync(press, release, RawInputModifiers.Alt, async () =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(clip.Width.Value, Is.EqualTo(initialWidth).Within(0.01));
                    Assert.That(clip.BorderMargin.Value.Left, Is.EqualTo(initialLeft).Within(0.01));
                    Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(clip.ThumbnailContentOffsetX.Value, Is.EqualTo(expectedClampedOffsetX).Within(0.5),
                        "Slip preview must clamp its slide offset to the usable source bounds.");
                    Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)),
                        "Preview must not mutate the model during drag.");
                });

                // Newly revealed right-hand chunks (indices >= initialChunkCount) must load during slip drag.
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                    view.Waveform.GetMissingIndices(initialChunkCount, initialChunkCount + 10).Count == 0);
            });

            await HeadlessTestHelpers.WaitUntilAsync(() => double.IsNaN(clip.ThumbnailContentWidth.Value));
            Assert.Multiple(() =>
            {
                Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
                Assert.That(clip.ThumbnailContentOffsetX.Value, Is.Zero);
            });
        }
        finally
        {
            clip.Timeline.IsSlipMode.Value = false;
        }
    }

    private static async Task WaitForWaveformReady(ElementViewModel clip, WaveformControl waveform)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() =>
            clip.WaveformChunkCount.Value > 0
            && waveform.GetMissingIndices(0, clip.WaveformChunkCount.Value - 1).Count == 0);
        HeadlessTestHelpers.Render(3);
    }

    private static async Task<ElementViewModel> OpenAudioClip(
        double startSeconds, double lengthSeconds, double offsetSeconds, double sourceSeconds)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync("edit-mode-thumbnail-preview");
        scene.Duration.Value = MediaTime.FromSeconds(10);
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        Assert.That((await adder.AddAsync([new ElementDescription(
            Start: MediaTime.FromSeconds(startSeconds),
            Length: MediaTime.FromSeconds(lengthSeconds),
            Layer: 0,
            Source: new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None)).IsSuccess, Is.True);

        Element element = scene.Children.Single();
        AttachAudio(element, offsetSeconds, sourceSeconds);

        var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1, Offset = System.Numerics.Vector2.Zero };
        HeadlessTestHelpers.Settle();
        return timeline.GetViewModelFor(element)!;
    }

    private static void AttachAudio(Element element, double offsetSeconds, double sourceSeconds)
    {
        TestShell.EnsureDecoder<ToneAudioDecoder>();
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"tone-{sourceSeconds:F1}-{Guid.NewGuid():N}.preview-tone");
        File.WriteAllText(path, sourceSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        element.Objects.Clear();
        element.Objects.Add(new SourceSound
        {
            Source = { Value = source },
            OffsetPosition = { Value = MediaTime.FromSeconds(offsetSeconds) },
        });
    }

    private sealed class ToneAudioDecoder : IDecoderInfo
    {
        public string Name => "Timeline preview tone decoder";
        public IEnumerable<string> VideoExtensions() => [];
        public IEnumerable<string> AudioExtensions() => [".preview-tone"];

        public MediaReader? Open(string file, MediaOptions options)
        {
            if (Path.GetExtension(file) != ".preview-tone") return null;
            double seconds = double.Parse(File.ReadAllText(file), System.Globalization.CultureInfo.InvariantCulture);
            return new ToneAudioReader(44100, 2, MediaTime.FromSeconds(seconds));
        }
    }

    private sealed class ToneAudioReader(int sampleRate, int channels, MediaTime duration) : MediaReader
    {
        private readonly long _totalSamples = duration.ToSamples(sampleRate, MidpointRounding.ToZero);

        public override VideoStreamInfo VideoInfo => throw new NotSupportedException();
        public override AudioStreamInfo AudioInfo { get; } = new("pcm", new Rational((long)Math.Round(duration.ToSeconds() * 1000), 1000), sampleRate, channels);
        public override bool HasVideo => false;
        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (start < 0 || start >= _totalSamples || length <= 0)
            {
                sound = null;
                return false;
            }

            int actualLength = (int)Math.Min(length, _totalSamples - start);
            var buffer = new AudioBuffer(sampleRate, channels, actualLength);
            for (int ch = 0; ch < channels; ch++)
            {
                Span<float> data = buffer.GetChannelData(ch);
                for (int i = 0; i < actualLength; i++)
                {
                    data[i] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * (start + i) / sampleRate);
                }
            }

            sound = buffer;
            return true;
        }
    }

    private sealed class PreviewConfigScope : IDisposable
    {
        private readonly bool _showThumbnails = TestShell.Configuration.EditorConfig.ShowTimelineThumbnailsByDefault.Value;
        private readonly bool _ripple = TestShell.Configuration.EditorConfig.IsRippleEnabled.Value;
        private readonly bool _clamp = TestShell.Configuration.EditorConfig.ClampResizeToOriginalLength.Value;
        private readonly bool _snap = TestShell.Configuration.EditorConfig.IsTimelineSnapEnabled.Value;

        public PreviewConfigScope()
        {
            EditorConfig config = TestShell.Configuration.EditorConfig;
            config.ShowTimelineThumbnailsByDefault.Value = true;
            config.IsRippleEnabled.Value = false;
            config.ClampResizeToOriginalLength.Value = true;
            config.IsTimelineSnapEnabled.Value = false;
        }

        public void Dispose()
        {
            EditorConfig config = TestShell.Configuration.EditorConfig;
            config.ShowTimelineThumbnailsByDefault.Value = _showThumbnails;
            config.IsRippleEnabled.Value = _ripple;
            config.ClampResizeToOriginalLength.Value = _clamp;
            config.IsTimelineSnapEnabled.Value = _snap;
        }
    }

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
                Waveform = Element.FindControl<WaveformControl>("waveformControl")!;
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
        public WaveformControl Waveform { get; }
        public float Scale { get; }

        public WaveformControl GetWaveformFor(ElementViewModel vm)
            => _view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(v.DataContext, vm))
                .FindControl<WaveformControl>("waveformControl")!;

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

        public async Task DragAsync(Point press, Point release, RawInputModifiers modifiers, Func<Task> whilePressed)
        {
            Window.MouseMove(press, modifiers);
            Window.MouseDown(press, MouseButton.Left, modifiers);
            try
            {
                Window.MouseMove(release, modifiers | RawInputModifiers.LeftMouseButton);
                HeadlessTestHelpers.Render(5);
                await whilePressed();
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
}
