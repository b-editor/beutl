using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.Threading;
using Point = Avalonia.Point;
using WriteableBitmap = Avalonia.Media.Imaging.WriteableBitmap;

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
    [TestCase(true)]
    [TestCase(false)]
    public async Task Resize_PreviewsAnimatedGainAtTheNewRange(bool leftEdge)
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 6);
        var sound = clip.Model.Objects.OfType<SourceSound>().Single();
        if (leftEdge)
            Animate(sound.Gain, 0, 100, clip.Model.Start.Value, global: true);
        else
            sound.Gain.Expression = Expression.Create<float>("(float)(100 * (1 - Progress))");
        using var view = new TimelineViewScope(clip, width: 1200);
        await WaitForWaveformReady(clip, view.Waveform);

        int baseChunkCount = clip.WaveformChunkCount.Value;
        int index = leftEdge ? baseChunkCount / 4 : baseChunkCount * 3 / 4;
        Assert.That(view.Waveform.TryGetChunk(index, out var original), Is.True);
        double y = view.Border.Bounds.Height / 2;
        double pressX = leftEdge ? 2 : view.Border.Bounds.Width - 2;
        double releaseX = MediaTime.FromSeconds(leftEdge ? 0.5 : 3).TimeToPixel(view.Scale);
        Point press = view.Border.TranslatePoint(new Point(pressX, y), view.Window)!.Value;
        Point release = view.Border.TranslatePoint(new Point(releaseX, y), view.Window)!.Value;
        MediaTime expectedLength = MediaTime.FromSeconds(leftEdge ? 1.5 : 3);
        WaveformChunk preview = default;

        await view.DragAsync(press, release, RawInputModifiers.Alt, async () =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                Assert.That(clip.Width.Value, Is.EqualTo(expectedLength.TimeToPixel(view.Scale)).Within(0.5));
                Assert.That(clip.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            });
            await HeadlessTestHelpers.WaitUntilAsync(() => view.Waveform.TryGetChunk(index, out preview) && preview != original);
            Assert.That(preview.MaxValue, Is.GreaterThan(original.MaxValue + 0.015f));
            if (!leftEdge)
                await HeadlessTestHelpers.WaitUntilAsync(() =>
                    view.Waveform.TryGetChunk(baseChunkCount, out var revealed) && revealed.MaxValue > 0f);
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests),
                leftEdge ? "left-resize-global-gain" : "right-resize-duration-gain");
        });

        await HeadlessTestHelpers.WaitUntilAsync(() => clip.WaveformChunkCount.Value == (int)expectedLength.TimeToPixel(view.Scale));
        await WaitForWaveformReady(clip, view.Waveform);
        Assert.Multiple(() =>
        {
            Assert.That(clip.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(leftEdge ? 1.5 : 1)));
            Assert.That(clip.Model.Length.Value, Is.EqualTo(expectedLength));
            Assert.That(view.Waveform.TryGetChunk(index, out var moved), Is.True);
            Assert.That(moved.MaxValue, Is.EqualTo(preview.MaxValue).Within(0.01f));
        });
    }

    [AvaloniaTest]
    [TestCase(false, "none")]
    [TestCase(true, "none")]
    [TestCase(false, "speed")]
    [TestCase(true, "speed")]
    [TestCase(false, "gain")]
    [TestCase(true, "gain")]
    [TestCase(true, "middle gain")]
    [TestCase(false, "expression")]
    [TestCase(true, "front duration")]
    public async Task TrimEdit_AnchorsFrontAndBackContent(bool slide, string animation)
    {
        using var config = new PreviewConfigScope();
        var (_, timeline) = await TimelineTestHost.OpenTimelineAsync("trim-thumbnail-preview",
            Enumerable.Range(0, slide ? 3 : 2)
                .Select(i => ClipDescription(1 + i * 2, 2, () => CreateAudio(offsetSeconds: 2, sourceSeconds: 10)))
                .ToArray());
        List<ElementViewModel> clips = timeline.AllElements();
        ElementViewModel front = clips[0];
        ElementViewModel back = clips[^1];
        ElementViewModel dragged = slide ? clips[1] : front;
        bool resampleFront = animation == "front duration";
        bool resampleMiddle = animation == "middle gain";
        bool resampleBack = animation is "speed" or "gain" or "expression";
        ElementViewModel waveformClip = resampleFront ? front : resampleMiddle ? dragged : back;
        var sound = waveformClip.Model.Objects.OfType<SourceSound>().Single();
        if (animation == "speed")
            Animate(sound.Speed, 100, 200);
        else if (animation == "gain")
        {
            Animate(sound.Gain, 0, 100);
            Animate(sound.Pan, -100, 100, back.Model.Start.Value, global: true);
        }
        else if (resampleMiddle)
        {
            Animate(sound.Gain, 0, 100, dragged.Model.Start.Value, global: true);
            sound.Gain.Expression = Expression.Create<float>("Value + (float)(Duration * Time * 50)");
        }
        else if (animation == "expression")
        {
            sound.Gain.Expression = Expression.Create<float>("(float)(Time * 30)");
        }
        else if (resampleFront)
        {
            sound.Gain.Expression = Expression.Create<float>("(float)(Duration * 30)");
        }

        timeline.ClearSelected();
        timeline.SelectElement(dragged);
        timeline.IsRollMode.Value = !slide;
        timeline.IsSlideMode.Value = slide;

        WaveformChunk? observedChunk = null;
        WaveformChunk? previewChunk = null;
        int probeIndex = -1;
        waveformClip.WaveformChunkReady += ObserveWaveform;
        try
        {
            using var view = new TimelineViewScope(dragged, width: 1400);
            WaveformControl frontWave = view.GetWaveformFor(front);
            WaveformControl backWave = view.GetWaveformFor(back);
            await WaitForWaveformReady(front, frontWave);
            await WaitForWaveformReady(back, backWave);
            if (resampleMiddle)
                await WaitForWaveformReady(dragged, view.Waveform);

            double frontWidth = frontWave.Bounds.Width;
            double backWidth = backWave.Bounds.Width;
            int baseCount = waveformClip.WaveformChunkCount.Value;
            probeIndex = baseCount / 4;
            WaveformChunk original = default;
            if (resampleFront || resampleMiddle)
                Assert.That(view.GetWaveformFor(waveformClip).TryGetChunk(probeIndex, out original), Is.True);
            MediaTime delta = MediaTime.FromSeconds(slide ? 0.5 : -0.5);
            double deltaPixels = delta.TimeToPixel(view.Scale);
            double pressX = slide ? view.Border.Bounds.Width / 2 : view.Border.Bounds.Width - 2;
            Point press = view.Border.TranslatePoint(new Point(pressX, view.Border.Bounds.Height / 2), view.Window)!.Value;
            await view.DragAsync(press, press.WithX(press.X + deltaPixels), RawInputModifiers.Alt, async () =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(front.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(front.ThumbnailContentWidth.Value, Is.EqualTo(frontWidth));
                    Assert.That(front.ThumbnailContentOffsetX.Value, Is.Zero);
                    Assert.That(back.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(back.ThumbnailContentWidth.Value, Is.EqualTo(backWidth));
                    Assert.That(back.ThumbnailContentOffsetX.Value, Is.EqualTo(resampleBack ? 0 : -deltaPixels).Within(0.5));
                    if (slide)
                        Assert.That(dragged.IsThumbnailPreviewActive.Value, Is.EqualTo(resampleMiddle));
                });

                if (resampleFront || resampleMiddle || resampleBack)
                {
                    await HeadlessTestHelpers.WaitUntilAsync(() => observedChunk is { } chunk && chunk != original);
                    if (resampleBack && !slide)
                        await HeadlessTestHelpers.WaitUntilAsync(() => backWave.GetMissingIndices(baseCount, baseCount + 5).Count == 0);
                    previewChunk = observedChunk;
                    observedChunk = null;
                    if (resampleMiddle)
                    {
                        HeadlessTestHelpers.Render(3);
                        HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), "slide-middle-preview");
                    }
                }
                else if (!slide)
                {
                    await HeadlessTestHelpers.WaitUntilAsync(() => backWave.GetMissingIndices(-10, -1).Count == 0);
                }
            });

            await HeadlessTestHelpers.WaitUntilAsync(() =>
                double.IsNaN(front.ThumbnailContentWidth.Value) && double.IsNaN(back.ThumbnailContentWidth.Value)
                && (!resampleMiddle || double.IsNaN(dragged.ThumbnailContentWidth.Value)));
            if (previewChunk is { } sample)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => observedChunk.HasValue);
                Assert.That(observedChunk!.Value.MaxValue, Is.EqualTo(sample.MaxValue).Within(animation == "speed" ? 0.05f : 0.01f));
            }
            Assert.Multiple(() =>
            {
                Assert.That(front.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2) + delta));
                Assert.That(back.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(slide ? 5 : 3) + delta));
                Assert.That(back.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2) - delta));
                if (slide)
                    Assert.That(dragged.Model.Start.Value, Is.EqualTo(MediaTime.FromSeconds(3) + delta));
            });
        }
        finally
        {
            timeline.IsRollMode.Value = false;
            timeline.IsSlideMode.Value = false;
            waveformClip.WaveformChunkReady -= ObserveWaveform;
        }

        void ObserveWaveform(WaveformChunk chunk)
        {
            if (chunk.Index == probeIndex)
                observedChunk = chunk;
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task VideoResize_PausesPendingStripRebuildUntilDragEnds(bool reverse)
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenVideoClip();
        TimelineTabViewModel timeline = clip.Timeline;
        var video = clip.Model.Objects.OfType<SourceVideo>().Single();
        if (reverse)
        {
            // The speed curve overshoots below zero before reaching its second keyframe.
            var animation = new KeyFrameAnimation<float>();
            animation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 0 } });
            animation.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.FromSeconds(4) },
                Value = { Value = 100 },
                Easing = { Value = new BackEaseIn() },
            });
            video.Speed.SetKeyFrameAnimation(animation);
            TestShell.Configuration.EditorConfig.ClampResizeToOriginalLength.Value = false;
        }
        string captureName = reverse ? "reverse-video" : "video";
        using var view = new TimelineViewScope(clip, width: 1200);
        var strip = view.Element.FindControl<ThumbnailStripControl>("thumbnailStrip")!;
        await HeadlessTestHelpers.WaitUntilAsync(() => clip.VideoThumbnailCount.Value > 0
            && strip.GetMissingIndices(0, clip.VideoThumbnailCount.Value - 1).Count == 0);
        int baseCount = clip.VideoThumbnailCount.Value;

        // Queue a rebuild for the zoomed layout behind a render-thread operation.
        await using var pause = new DispatcherPause(RenderThread.Dispatcher);
        await pause.Entered;
        timeline.Options.Value = timeline.Options.Value with { Scale = 1.5f };
        HeadlessTestHelpers.Render(3);
        clip.InvalidateAndUpdateThumbnails();
        double baseWidth = strip.Bounds.Width;
        HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), $"{captureName}-before");

        Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width - 2, view.Border.Bounds.Height / 2), view.Window)!.Value;
        Point release = press.WithX(press.X + MediaTime.FromSeconds(0.75).TimeToPixel(1.5f));
        await view.DragAsync(press, release, RawInputModifiers.Alt, async () =>
        {
            pause.Release();
            WriteableBitmap? thumbnail = null;
            await HeadlessTestHelpers.WaitUntilAsync(() => strip.TryGetThumbnail(baseCount, out thumbnail) && thumbnail != null);
            Assert.Multiple(() =>
            {
                Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                Assert.That(clip.VideoThumbnailCount.Value, Is.EqualTo(baseCount));
                Assert.That(clip.ThumbnailContentWidth.Value, Is.EqualTo(baseWidth));
            });
            if (reverse)
            {
                // The first added slot samples the original clip end at the frozen slot spacing.
                using var playback = video.ToResource(new CompositionContext(clip.Model.Range.End) { Decoders = TestShell.Decoders });
                var source = playback.Source!;
                MediaTime sourceTime = playback.RequestedPosition + playback.OffsetPosition;
                Assert.That(sourceTime, Is.GreaterThan(source.Duration - MediaTime.FromSeconds(1)));
                Bgra8888 expected = PreviewVideoReader.FrameColor(source.GetFrameIndex(sourceTime));
                var center = new Avalonia.PixelPoint(thumbnail!.PixelSize.Width / 2, thumbnail.PixelSize.Height / 2);
                Assert.That(HeadlessTestHelpers.ReadPixel(thumbnail, center), Is.EqualTo((expected.R, expected.G, expected.B)).Within(1));
            }
            if (HeadlessCapture.IsRequested)
            {
                var (_, end) = strip.GetVisibleRange();
                await HeadlessTestHelpers.WaitUntilAsync(() => strip.GetMissingIndices(baseCount, end).Count == 0);
            }
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), $"{captureName}-drag");
            view.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        });

        await HeadlessTestHelpers.WaitUntilAsync(() => clip.VideoThumbnailCount.Value > baseCount);
        Assert.That(clip.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [AvaloniaTest]
    public async Task SlipEdit_EscapeAppliesTheZoomChangeWhileKeepingTheClipRealized()
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenVideoClip();
        clip.Timeline.IsSlipMode.Value = true;
        using var view = new TimelineViewScope(clip, width: 1200);
        var strip = view.Element.FindControl<ThumbnailStripControl>("thumbnailStrip")!;
        await HeadlessTestHelpers.WaitUntilAsync(() => clip.VideoThumbnailCount.Value > 0
            && strip.GetMissingIndices(0, clip.VideoThumbnailCount.Value - 1).Count == 0);
        await RenderThread.Dispatcher.InvokeAsync(static () => { }, DispatchPriority.Low);
        HeadlessTestHelpers.Settle();
        Assert.That(view.Element.Focus(), Is.True);
        Point press = HeadlessTestHelpers.Center(view.Border, view.Window);
        Point move = press.WithX(press.X + MediaTime.FromSeconds(0.5).TimeToPixel(view.Scale));

        try
        {
            await view.DragAsync(press, move, RawInputModifiers.Alt, () =>
            {
                view.Window.MouseWheel(move, new Avalonia.Vector(0, -16), TimelineTestHost.CommandModifier);
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), "slip-zoom-preview");
                Assert.Multiple(() =>
                {
                    Assert.That(clip.Width.Value, Is.LessThan(ElementViewModel.MinimumThumbnailWidth));
                    Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(clip.VideoThumbnailCount.Value, Is.Positive);
                });

                view.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), "slip-zoom-cancelled");
                Assert.Multiple(() =>
                {
                    Assert.That(view.Element.DataContext, Is.SameAs(clip));
                    Assert.That(clip.VideoThumbnailCount.Value, Is.Zero);
                    Assert.That(clip.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
                });
                return Task.CompletedTask;
            });
        }
        finally
        {
            clip.Timeline.IsSlipMode.Value = false;
        }
    }

    [AvaloniaTest]
    public async Task SlipEdit_CancellingTheNextDragRestoresTheCommittedWaveform()
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 6);
        var sound = clip.Model.Objects.OfType<SourceSound>().Single();
        Animate(sound.Gain, 0, 100);
        clip.Timeline.IsSlipMode.Value = true;
        using var view = new TimelineViewScope(clip, width: 1200);
        await WaitForWaveformReady(clip, view.Waveform);

        int index = clip.WaveformChunkCount.Value / 4;
        Assert.That(view.Waveform.TryGetChunk(index, out var original), Is.True);
        int lastIndex = clip.WaveformChunkCount.Value - 1;
        Assert.That(view.Waveform.TryGetChunk(lastIndex, out var originalEnd), Is.True);
        Point press = HeadlessTestHelpers.Center(view.Border, view.Window);
        double slipPixels = MediaTime.FromSeconds(0.5).TimeToPixel(view.Scale);
        Point move = press.WithX(press.X + slipPixels);
        DispatcherPause? pending = null;
        WaveformChunk firstPreview = default;
        try
        {
            await view.DragAsync(press, move, RawInputModifiers.Alt, async () =>
            {
                firstPreview = await WaitForChangedChunk(index, original);
                await WaitForChangedChunk(lastIndex, originalEnd);
                pending = new DispatcherPause(ComposeThread.Dispatcher);
                await pending.Entered;
            });
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), "slip-chain-committed");

            Point secondPress = press.WithX(press.X + 30);
            Point secondMove = secondPress.WithX(secondPress.X + slipPixels);
            await view.DragAsync(secondPress, secondMove, RawInputModifiers.Alt, async () =>
            {
                pending!.Release();
                await WaitForChangedChunk(index, firstPreview);
                await using var cancelled = new DispatcherPause(ComposeThread.Dispatcher);
                await cancelled.Entered;
                view.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                HeadlessTestHelpers.Render(3);
                Assert.Multiple(() =>
                {
                    Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
                    Assert.That(view.Waveform.TryGetChunk(index, out var restored), Is.True);
                    Assert.That(restored, Is.EqualTo(firstPreview));
                });
                HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), "slip-chain-cancelled");
            });
        }
        finally
        {
            if (pending != null)
                await pending.DisposeAsync();
            clip.Timeline.IsSlipMode.Value = false;
        }

        async Task<WaveformChunk> WaitForChangedChunk(int slot, WaveformChunk previous)
        {
            WaveformChunk chunk = default;
            await HeadlessTestHelpers.WaitUntilAsync(() => view.Waveform.TryGetChunk(slot, out chunk) && chunk != previous);
            return chunk;
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task SlipEdit_ClampsToSourceBoundsAndPreservesTheGainEnvelope(bool animatedGain)
    {
        using var config = new PreviewConfigScope();
        // 4s source, 2s clip, initial offset 1s -> headroom is [-1s, +1s].
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 4);
        var sound = clip.Model.Objects.OfType<SourceSound>().Single();
        if (animatedGain)
            Animate(sound.Gain, 0, 100);

        clip.Timeline.ClearSelected();
        clip.Timeline.SelectElement(clip);
        clip.Timeline.IsSlipMode.Value = true;
        float? observedPeak = null;
        bool endChunkReady = false;
        string captureName = animatedGain ? "slip-gain" : "slip";
        if (animatedGain)
            clip.WaveformChunkReady += ObserveWaveform;
        try
        {
            using var view = new TimelineViewScope(clip, width: 1200);
            await WaitForWaveformReady(clip, view.Waveform);

            float? originalPeak = observedPeak;
            observedPeak = null;
            endChunkReady = false;
            HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), $"{captureName}-before");
            double initialWidth = clip.Width.Value;
            double initialLeft = clip.BorderMargin.Value.Left;
            int initialChunkCount = clip.WaveformChunkCount.Value;
            float scale = view.Scale;
            Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width / 2, view.Border.Bounds.Height / 2), view.Window)!.Value;
            // Drag right by 3s. Both previews must clamp at +1s, where the clip reaches the source end.
            Point release = press.WithX(press.X + MediaTime.FromSeconds(3).TimeToPixel(scale));
            double expectedClampedOffsetX = animatedGain ? 0 : -MediaTime.FromSeconds(1).TimeToPixel(scale);
            float? previewPeak = null;

            await view.DragAsync(press, release, RawInputModifiers.Alt, async () =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(clip.Width.Value, Is.EqualTo(initialWidth).Within(0.01));
                    Assert.That(clip.BorderMargin.Value.Left, Is.EqualTo(initialLeft).Within(0.01));
                    Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(clip.ThumbnailContentOffsetX.Value, Is.EqualTo(expectedClampedOffsetX).Within(0.5));
                    Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)),
                        "Preview must not mutate the model during drag.");
                    Assert.That(view.Overlay.DragReadout?.Change, Is.EqualTo("+" + FormatReadoutTime(clip, MediaTime.FromSeconds(1))));
                    Assert.That(view.Overlay.DragReadout?.Details, Is.EqualTo(string.Format(Strings.TimelineDrag_MediaOffset,
                        FormatReadoutTime(clip, MediaTime.FromSeconds(2)))));
                });

                if (animatedGain)
                {
                    await HeadlessTestHelpers.WaitUntilAsync(() => observedPeak.HasValue);
                    previewPeak = observedPeak;
                    Assert.That(Math.Abs(previewPeak!.Value - originalPeak!.Value), Is.GreaterThan(0.08f));
                    if (HeadlessCapture.IsRequested)
                        await HeadlessTestHelpers.WaitUntilAsync(() => endChunkReady);
                    observedPeak = null;
                }
                else
                {
                    var (_, end) = view.Waveform.GetVisibleRange();
                    await HeadlessTestHelpers.WaitUntilAsync(() =>
                        view.Waveform.GetMissingIndices(initialChunkCount, end).Count == 0);
                }
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), $"{captureName}-drag");
            });

            await HeadlessTestHelpers.WaitUntilAsync(() => double.IsNaN(clip.ThumbnailContentWidth.Value));
            if (previewPeak is { } peak)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => observedPeak.HasValue);
                Assert.That(observedPeak!.Value, Is.EqualTo(peak).Within(0.01f));
            }
            Assert.Multiple(() =>
            {
                Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
                Assert.That(clip.ThumbnailContentOffsetX.Value, Is.Zero);
                Assert.That(view.Overlay.DragReadout, Is.Null);
            });
        }
        finally
        {
            clip.WaveformChunkReady -= ObserveWaveform;
            clip.Timeline.IsSlipMode.Value = false;
        }

        void ObserveWaveform(WaveformChunk chunk)
        {
            if (chunk.Index == clip.WaveformChunkCount.Value / 4)
                observedPeak = chunk.MaxValue;
            if (chunk.Index == clip.WaveformChunkCount.Value - 1)
                endChunkReady = true;
        }
    }

    [AvaloniaTest]
    public async Task SlipEdit_AnimatedSpeed_ResamplesInPlaceWithoutTranslatingStrip()
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 10);
        var sound = clip.Model.Objects.OfType<SourceSound>().Single();
        Animate(sound.Speed, 100, 200);
        clip.Timeline.ClearSelected();
        clip.Timeline.SelectElement(clip);
        clip.Timeline.IsSlipMode.Value = true;
        try
        {
            using var view = new TimelineViewScope(clip, width: 1200);
            await WaitForWaveformReady(clip, view.Waveform);
            HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), "slip-speed-before");

            float scale = view.Scale;
            Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width / 2, view.Border.Bounds.Height / 2), view.Window)!.Value;
            Point release = press.WithX(press.X + MediaTime.FromSeconds(1).TimeToPixel(scale));
            string? offsetReadout = null;

            await view.DragAsync(press, release, RawInputModifiers.Alt, async () =>
            {
                offsetReadout = view.Overlay.DragReadout?.Details;
                Assert.Multiple(() =>
                {
                    Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                    Assert.That(clip.ThumbnailContentOffsetX.Value, Is.Zero,
                        "Variable-speed slips must resample with the source-offset delta rather than translating the old strip.");
                    Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
                });
                bool waveformArrived = false;
                bool endChunkReady = false;
                clip.WaveformChunkReady += ObserveWaveform;
                try
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(10);
                    var elapsed = Stopwatch.StartNew();
                    // Keep sending different source deltas more often than the preview sampling interval.
                    for (int i = 0; !waveformArrived && elapsed.Elapsed < timeout; i++)
                    {
                        view.Window.MouseMove(release.WithX(release.X + (i % 8) * 5),
                            RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
                        HeadlessTestHelpers.Settle();
                        await Task.Delay(5);
                    }

                    Assert.That(waveformArrived, Is.True, "Preview must stream while the pointer is still moving.");

                    if (HeadlessCapture.IsRequested)
                    {
                        await HeadlessTestHelpers.WaitUntilAsync(() => endChunkReady);
                        HeadlessTestHelpers.Render(3);
                        HeadlessCapture.SaveIfRequested(view.Window, nameof(TimelineEditModeThumbnailPreviewTests), "slip-speed-streaming");
                    }
                }
                finally
                {
                    clip.WaveformChunkReady -= ObserveWaveform;
                }

                void ObserveWaveform(WaveformChunk chunk)
                {
                    waveformArrived = true;
                    if (chunk.Index == clip.WaveformChunkCount.Value - 1)
                        endChunkReady = true;
                }
            });

            await HeadlessTestHelpers.WaitUntilAsync(() => double.IsNaN(clip.ThumbnailContentWidth.Value));
            Assert.That(sound.OffsetPosition.Value, Is.GreaterThan(MediaTime.FromSeconds(1)));
            Assert.That(offsetReadout, Is.EqualTo(string.Format(Strings.TimelineDrag_MediaOffset,
                FormatReadoutTime(clip, sound.OffsetPosition.Value))));
        }
        finally
        {
            clip.Timeline.IsSlipMode.Value = false;
        }
    }

    [AvaloniaTest]
    [TestCase("cancel")]
    [TestCase("same position")]
    [TestCase("different position")]
    public async Task SlipEdit_RestoresOrKeepsWaveformWhileReplacementIsBlocked(string completion)
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 6);
        var sound = clip.Model.Objects.OfType<SourceSound>().Single();
        Animate(sound.Gain, 0, 100);
        clip.Timeline.IsSlipMode.Value = true;
        using var view = new TimelineViewScope(clip, width: 1200);
        await WaitForWaveformReady(clip, view.Waveform);

        int index = clip.WaveformChunkCount.Value / 4;
        Assert.That(view.Waveform.TryGetChunk(index, out var original), Is.True);
        Point press = view.Border.TranslatePoint(new Point(view.Border.Bounds.Width / 2, view.Border.Bounds.Height / 2), view.Window)!.Value;
        Point move = press.WithX(press.X + MediaTime.FromSeconds(0.5).TimeToPixel(view.Scale));
        double releaseDelta = completion == "different position" ? 1 : 0.5;
        Point release = press.WithX(press.X + MediaTime.FromSeconds(releaseDelta).TimeToPixel(view.Scale));
        bool cancelled = completion == "cancel";

        view.Window.MouseMove(press, RawInputModifiers.Alt);
        view.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.Alt);
        try
        {
            view.Window.MouseMove(move, RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
            HeadlessTestHelpers.Render(3);
            WaveformChunk preview = default;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                view.Waveform.TryGetChunk(index, out preview) && preview != original);

            await using var pause = new DispatcherPause(ComposeThread.Dispatcher);
            await pause.Entered;
            if (cancelled)
                view.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            else
                view.Window.MouseUp(release, MouseButton.Left, RawInputModifiers.Alt);
            HeadlessTestHelpers.Render(3);

            Assert.Multiple(() =>
            {
                Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(cancelled ? 1 : 1 + releaseDelta)));
                Assert.That(clip.IsThumbnailPreviewActive.Value, Is.False);
                Assert.That(view.Waveform.TryGetChunk(index, out var displayed), Is.True);
                Assert.That(displayed, Is.EqualTo(completion == "same position" ? preview : original));
            });
        }
        finally
        {
            view.Window.MouseUp(release, MouseButton.Left, RawInputModifiers.Alt);
            clip.Timeline.IsSlipMode.Value = false;
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task SecondDragKeepsItsPreviewWhenThePreviousResizeSettles(bool interruptRefresh)
    {
        using var config = new PreviewConfigScope();
        ElementViewModel clip = await OpenAudioClip(startSeconds: 1, lengthSeconds: 2, offsetSeconds: 1, sourceSeconds: 6);
        using var view = new TimelineViewScope(clip, width: 1200);
        await WaitForWaveformReady(clip, view.Waveform);

        int baseCount = clip.WaveformChunkCount.Value;
        await using var pause = new DispatcherPause(ComposeThread.Dispatcher);
        await pause.Entered;

        var settle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clip.AnimationRequested = (_, _) => settle.Task;
        clip.UpdateMediaPreview(0);
        clip.Width.Value += MediaTime.FromSeconds(1).TimeToPixel(view.Scale);
        Task resize = clip.SubmitViewModelChanges();
        int probeIndex = baseCount + 10;
        WaveformChunk previewChunk = default;
        try
        {
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(clip.Model.Length.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
                Assert.That(double.IsNaN(clip.ThumbnailContentWidth.Value), Is.False);
                Assert.That(view.Waveform.GetMissingIndices(0, baseCount - 1), Is.Empty,
                    "The original waveform stays visible until the first replacement chunk arrives.");
            });
            if (!interruptRefresh)
            {
                pause.Release();
                await HeadlessTestHelpers.WaitUntilAsync(() => clip.WaveformChunkCount.Value > baseCount);
                await WaitForWaveformReady(clip, view.Waveform);
            }

            clip.UpdateMediaPreview(-10);
            double width = clip.ThumbnailContentWidth.Value;
            Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
            if (interruptRefresh)
            {
                pause.Release();
                await HeadlessTestHelpers.WaitUntilAsync(() => view.Waveform.TryGetChunk(probeIndex, out previewChunk));
                Assert.That(clip.WaveformChunkCount.Value, Is.EqualTo(baseCount));
            }

            settle.SetResult();
            await resize;
            Assert.Multiple(() =>
            {
                Assert.That(clip.IsThumbnailPreviewActive.Value, Is.True);
                Assert.That(clip.ThumbnailContentWidth.Value, Is.EqualTo(width));
                Assert.That(clip.ThumbnailContentOffsetX.Value, Is.EqualTo(-10));
            });

            clip.EndMediaPreview(committed: false);
            if (interruptRefresh)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => clip.WaveformChunkCount.Value > baseCount);
                await WaitForWaveformReady(clip, view.Waveform);
                Assert.That(view.Waveform.TryGetChunk(probeIndex, out var committedChunk), Is.True);
                Assert.That(committedChunk.MaxValue, Is.EqualTo(previewChunk.MaxValue).Within(0.01f));
            }
        }
        finally
        {
            settle.TrySetResult();
            await resize;
            clip.EndMediaPreview(committed: false);
        }
    }

    private static void Animate(IProperty<float> property, float start, float end, MediaTime startTime = default, bool global = false)
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = global } };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = startTime },
            Value = { Value = start },
            Easing = { Value = new LinearEasing() }
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = startTime + MediaTime.FromSeconds(2) },
            Value = { Value = end },
            Easing = { Value = new LinearEasing() }
        });
        property.SetKeyFrameAnimation(animation);
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
        var (_, timeline) = await TimelineTestHost.OpenTimelineAsync("edit-mode-thumbnail-preview",
            [ClipDescription(startSeconds, lengthSeconds, () => CreateAudio(offsetSeconds, sourceSeconds))]);
        return timeline.AllElements().Single();
    }

    private static async Task<ElementViewModel> OpenVideoClip()
    {
        TestGpu.RequireAnyDevice();
        TestShell.EnsureDecoder<PreviewMediaDecoder>();
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{Guid.NewGuid():N}.preview-video");
        File.WriteAllBytes(path, []);
        var (_, timeline) = await TimelineTestHost.OpenTimelineAsync("video-thumbnail-preview",
            [ClipDescription(1, 2, () => new SourceVideo { Source = { Value = VideoSource.Open(path) } })]);
        return timeline.AllElements().Single();
    }

    private static ElementDescription ClipDescription(double start, double length, Func<EngineObject> create)
        => new(MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), 0, new ElementSource.EngineObject(create));

    private static SourceSound CreateAudio(double offsetSeconds, double sourceSeconds)
    {
        TestShell.EnsureDecoder<PreviewMediaDecoder>();
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"{Guid.NewGuid():N}.preview-tone");
        File.WriteAllText(path, sourceSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new SourceSound
        {
            Source = { Value = SoundSource.Open(path) },
            OffsetPosition = { Value = MediaTime.FromSeconds(offsetSeconds) },
        };
    }

    private sealed class PreviewMediaDecoder : IDecoderInfo
    {
        public string Name => "Timeline preview test decoder";
        public IEnumerable<string> VideoExtensions() => [".preview-video"];
        public IEnumerable<string> AudioExtensions() => [".preview-tone"];

        public MediaReader? Open(string file, MediaOptions options)
            => Path.GetExtension(file) switch
            {
                ".preview-video" => new PreviewVideoReader(),
                ".preview-tone" => new ToneAudioReader(44100, 2, MediaTime.FromSeconds(
                    double.Parse(File.ReadAllText(file), System.Globalization.CultureInfo.InvariantCulture))),
                _ => null
            };
    }

    private sealed class PreviewVideoReader : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new("test", 180, new Beutl.Media.PixelSize(64, 36), new Rational(30, 1));
        public override AudioStreamInfo AudioInfo => throw new NotSupportedException();
        public override bool HasVideo => true;
        public override bool HasAudio => false;

        public static Bgra8888 FrameColor(int frame)
            => new((byte)(frame * 5), (byte)(80 + frame * 2), (byte)(180 - frame), 255);

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            var bitmap = new Bitmap(64, 36);
            bitmap.GetPixelSpan<Bgra8888>().Fill(FrameColor(frame));
            image = Ref<Bitmap>.Create(bitmap);
            return true;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            sound = null;
            return false;
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
                    float time = (start + i) / (float)sampleRate;
                    float amplitude = 0.5f + 0.4f * MathF.Sin(2f * MathF.PI * time * time / 3);
                    data[i] = amplitude * MathF.Sin(2f * MathF.PI * 440f * time);
                }
            }

            sound = buffer;
            return true;
        }
    }

    private sealed class DispatcherPause : IAsyncDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly Task _blocker;

        public DispatcherPause(Dispatcher dispatcher)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Entered = entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            _blocker = dispatcher.InvokeAsync(() =>
            {
                entered.SetResult();
                if (!_release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release the dispatcher.");
            });
        }

        public Task Entered { get; }

        public void Release() => _release.Set();

        public async ValueTask DisposeAsync()
        {
            Release();
            try
            {
                await _blocker;
            }
            finally
            {
                _release.Dispose();
            }
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

    private static string FormatReadoutTime(ElementViewModel clip, MediaTime time)
    {
        TimeDisplayFormat format = clip.Timeline.TimeDisplayFormat.Value;
        string text = TimeDisplayFormatter.Format(time, clip.Timeline.FrameRate, format);
        return format == TimeDisplayFormat.Frames ? text + "f" : text;
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
        public TimelineOverlay Overlay => _view.FindControl<TimelineOverlay>("overlay")!;
        public WaveformControl Waveform { get; }
        public float Scale { get; }

        public WaveformControl GetWaveformFor(ElementViewModel vm)
            => _view.GetVisualDescendants().OfType<ElementView>()
                .Single(v => ReferenceEquals(v.DataContext, vm))
                .FindControl<WaveformControl>("waveformControl")!;

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
