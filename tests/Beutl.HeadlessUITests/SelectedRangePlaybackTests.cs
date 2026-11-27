using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Models;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using Point = Avalonia.Point;
using TextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public sealed class SelectedRangePlaybackTests
{
    private const int Rate = 10;
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(15);

    [AvaloniaTest]
    [TestCase(0, 4)]
    [TestCase(6, 6)]
    [TestCase(9, 4)]
    public async Task Plays_from_the_selection_or_playhead_and_shows_both_boundary_frames(int playhead, int first)
    {
        EditViewModel editor = await OpenEditor(18);
        bool capture = playhead == 0 && HeadlessCapture.IsRequested;
        if (capture)
        {
            await AddCard(editor, "Before the range", "#3D4451", 0, 4);
            await AddCard(editor, "First selected frame", "#264EA6", 4, 5);
            await AddCard(editor, "Selected frames", "#304475", 5, 8);
            await AddCard(editor, "Last selected frame", "#995015", 8, 9);
            await AddCard(editor, "After the range", "#3D4451", 9, 18);
        }

        using var ui = new PlaybackWindow(editor, capture);
        ui.Select(4, 9);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(playhead));
        RenderThreadDrain.Drain(s_timeout);

        var shown = new List<int>();
        var firstShown = new TaskCompletionSource<MediaTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable clean = editor.Player.AcquireCleanPreview();
        using IDisposable cleanSubscription = editor.Player.CleanPreviewImage.Skip(1).Subscribe(_ =>
        {
            var frame = editor.Player.GetCleanPreviewFrame();
            using (frame.Image)
            {
                if (frame.PlaybackId != 0 && frame.Image != null && editor.Player.IsPlaying.Value)
                    firstShown.TrySetResult(frame.Time);
            }
        });
        using IDisposable clockSubscription = clock.CurrentTime.Skip(1).Subscribe(time => shown.Add(Frame(time)));
        editor.Player.IsLoopEnabled.Value = true;

        try
        {
            editor.Player.Play();
            Assert.That(await firstShown.Task.WaitAsync(s_timeout), Is.EqualTo(Time(first)));
            if (capture)
            {
                await ui.CaptureAsync(clock, 8, "range-end");
                await ui.CaptureAsync(clock, 4, "range-start-after-wrap");
            }

            await HeadlessTestHelpers.WaitUntilAsync(() => HasWrap(shown, 8, 4), s_timeout, pump: false);
            Assert.Multiple(() =>
            {
                Assert.That(shown, Has.All.InRange(4, 8));
                Assert.That(editor.Player.IsPlaying.Value, Is.True);
            });
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    public async Task With_no_selection_the_loop_rewinds_to_the_scene_start()
    {
        EditViewModel editor = await OpenEditor(8);
        editor.Scene.Start.Value = Time(2);
        editor.Scene.Duration.Value = Time(6);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(4));
        editor.Player.IsLoopEnabled.Value = true;

        var shown = new List<int>();
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(time => shown.Add(Frame(time)));
        try
        {
            editor.Player.Play();
            await HeadlessTestHelpers.WaitUntilAsync(() => shown.Count(x => x == 2) >= 2, s_timeout, pump: false);
            Assert.That(shown, Has.All.InRange(2, 7));
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Changing_or_clearing_the_selection_uses_the_new_range_at_the_next_wrap(bool clear)
    {
        EditViewModel editor = await OpenEditor(19);
        using var ui = new PlaybackWindow(editor);
        ui.Select(2, 11);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(0));
        editor.Player.IsLoopEnabled.Value = true;

        var shown = new List<int>();
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(time => shown.Add(Frame(time)));
        try
        {
            editor.Player.Play();
            await HeadlessTestHelpers.WaitUntilAsync(() => shown.Count >= 2, s_timeout, pump: false);
            ui.ChangeSelection(clear ? null : new FrameSelectionRange(Time(13), Time(17)));

            int nextStart = clear ? 0 : 13;
            await HeadlessTestHelpers.WaitUntilAsync(() => shown.Contains(nextStart), s_timeout, pump: false);
            Assert.That(shown[shown.IndexOf(nextStart) - 1], Is.EqualTo(10));
            Assert.That(shown.Take(shown.IndexOf(nextStart)), Has.All.InRange(2, 10));
            Assert.That(editor.Player.IsPlaying.Value, Is.True);
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    public async Task Selecting_during_whole_scene_playback_applies_on_the_next_rewind()
    {
        EditViewModel editor = await OpenEditor(15);
        using var ui = new PlaybackWindow(editor);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(2));
        editor.Player.IsLoopEnabled.Value = true;

        var shown = new List<int>();
        var playing = new List<bool>();
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(time => shown.Add(Frame(time)));
        using IDisposable playingSubscription = editor.Player.IsPlaying.Skip(1).Subscribe(playing.Add);
        try
        {
            editor.Player.Play();
            await HeadlessTestHelpers.WaitUntilAsync(() => shown.Count > 0, s_timeout, pump: false);
            ui.ChangeSelection(new FrameSelectionRange(Time(4), Time(8)));
            await HeadlessTestHelpers.WaitUntilAsync(() => HasWrap(shown, 7, 4), s_timeout, pump: false);
            int lastSceneFrame = shown.LastIndexOf(14);
            Assert.That(lastSceneFrame, Is.GreaterThan(0), "the scene must reach its last frame before selected looping starts");
            Assert.That(shown[lastSceneFrame + 1], Is.EqualTo(4));
            Assert.That(shown.Skip(lastSceneFrame + 1), Has.All.InRange(4, 7));
            Assert.That(playing, Is.EqualTo(new[] { true }));
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    [TestCase(true, false, 9)]
    [TestCase(false, false, 2)]
    [TestCase(true, true, 6)]
    [TestCase(false, true, 6)]
    public async Task Shuttle_wraps_inside_the_selected_range(bool forward, bool fineGrain, int playhead)
    {
        const int first = 4;
        const int last = 7;
        EditViewModel editor = await OpenEditor(15);
        bool capture = forward && fineGrain && HeadlessCapture.IsRequested;
        if (capture)
        {
            await AddCard(editor, "First selected frame", "#264EA6", first, first + 1);
            await AddCard(editor, "Last selected frame", "#995015", last, last + 1);
        }

        using var ui = new PlaybackWindow(editor, capture);
        ui.Select(first, last + 1);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(playhead));
        var shown = new List<int>();
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(time => shown.Add(Frame(time)));
        editor.Player.IsLoopEnabled.Value = true;

        try
        {
            StartShuttle(editor.Player, forward, fineGrain);
            if (capture)
            {
                await ui.CaptureAsync(clock, last, "shuttle-range-end");
                await ui.CaptureAsync(clock, first, "shuttle-range-start-after-wrap");
            }

            await HeadlessTestHelpers.WaitUntilAsync(() => HasWrap(shown, forward ? last : first, forward ? first : last),
                s_timeout, pump: false);
            Assert.Multiple(() =>
            {
                Assert.That(shown, Has.All.InRange(first, last));
                if (playhead < first || playhead > last) Assert.That(shown[0], Is.EqualTo(forward ? first : last));
                Assert.That(editor.Player.IsPlaying.Value, Is.True);
            });
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task Shuttle_with_no_selection_wraps_the_whole_scene(bool forward)
    {
        EditViewModel editor = await OpenEditor(12);
        editor.Scene.Start.Value = Time(2);
        editor.Scene.Duration.Value = Time(8);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(6));
        var shown = new List<int>();
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(time => shown.Add(Frame(time)));
        editor.Player.IsLoopEnabled.Value = true;

        try
        {
            StartShuttle(editor.Player, forward, fineGrain: true);
            await HeadlessTestHelpers.WaitUntilAsync(() => HasWrap(shown, forward ? 9 : 2, forward ? 2 : 9),
                s_timeout, pump: false);
            Assert.That(shown, Has.All.InRange(2, 9));
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task Shuttle_uses_the_changed_selection_at_the_next_wrap(bool forward, bool clear)
    {
        EditViewModel editor = await OpenEditor(17);
        using var ui = new PlaybackWindow(editor);
        ui.Select(4, 8);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(6));
        var shown = new List<int>();
        using IDisposable subscription = clock.CurrentTime.Skip(1).Subscribe(time =>
        {
            shown.Add(Frame(time));
            if (shown.Count == 1) ui.ChangeSelection(clear ? null : new FrameSelectionRange(Time(10), Time(14)));
        });
        editor.Player.IsLoopEnabled.Value = true;

        try
        {
            StartShuttle(editor.Player, forward, fineGrain: true);
            int firstOutside = -1;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                (firstOutside = shown.FindIndex(frame => frame is < 4 or > 7)) >= 0, s_timeout, pump: false);

            Assert.That(firstOutside, Is.GreaterThan(0));
            Assert.That(shown[firstOutside - 1], Is.EqualTo(forward ? 7 : 4));
            Assert.That(shown[firstOutside], Is.EqualTo(clear ? 0 : forward ? 10 : 13));
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    public async Task Shuttle_ignores_the_selection_when_looping_is_off()
    {
        EditViewModel editor = await OpenEditor(12);
        using var ui = new PlaybackWindow(editor);
        ui.Select(4, 8);
        IEditorClock clock = editor.GetRequiredService<IEditorClock>();
        clock.Seek(Time(6));
        editor.Player.IsLoopEnabled.Value = false;

        try
        {
            editor.Player.ShuttleForward();
            await HeadlessTestHelpers.WaitUntilAsync(() => !editor.Player.IsPlaying.Value, s_timeout, pump: false);
            Assert.That(Frame(clock.CurrentTime.Value), Is.GreaterThanOrEqualTo(8));
        }
        finally
        {
            await editor.Player.Pause();
        }
    }

    [AvaloniaTest]
    public async Task Slow_frames_skip_completed_passes_and_keep_the_loop_history_bounded()
    {
        TestGpu.RequireAnyDevice();
        const int frameRate = 40;
        const int first = 4;
        const int framesPerPass = 2;
        var rate = new Rational(frameRate);
        MediaTime firstTime = MediaTime.FromFrames(first, rate);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("slow-selected-loop", 320, 240,
            frameRate: frameRate, sampleRate: 48000, duration: MediaTime.FromSeconds(1));
        PlayerViewModel player = editor.Player;
        editor.FindToolTab<TimelineTabViewModel>()!.FrameSelection.Value =
            new FrameSelectionRange(firstTime, MediaTime.FromFrames(first + framesPerPass, rate));
        RenderThreadDrain.Drain(s_timeout);

        using var stopDelay = new CancellationTokenSource();
        CancellationToken delayToken = stopDelay.Token;
        var frames = new List<(int VideoPosition, int LagInPasses)>();
        using IDisposable clean = player.AcquireCleanPreview();
        using IDisposable shown = player.CleanPreviewImage.Skip(1).Subscribe(_ =>
        {
            var frame = player.GetCleanPreviewFrame();
            using (frame.Image)
            {
                if (frame.Image == null || player.GetLoopPlaybackState() is not { } state) return;

                int videoPosition = (int)(frame.Time - firstTime).ToFrame(rate, MidpointRounding.AwayFromZero);
                int lag = state.AudioPassPosition / framesPerPass - videoPosition / framesPerPass;
                frames.Add((videoPosition, lag));
            }
        });

        try
        {
            int maxRetained = 0;
            // The producer queues one render job at a time. Interleave a delay at the same priority
            // so every frame is slow, even when the renderer can reuse a previously drawn frame.
            RenderThread.Dispatcher.Dispatch(DelayRendering, Threading.DispatchPriority.Medium, delayToken);
            player.IsLoopEnabled.Value = true;
            player.Play();
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                if (player.GetLoopPlaybackState() is not { } state) return false;

                maxRetained = Math.Max(maxRetained, state.RetainedPassCount);
                return state.AudioPassPosition >= 40 * framesPerPass && frames.Count >= 10
                    && frames[^1].VideoPosition / framesPerPass >= state.AudioPassPosition / framesPerPass - 1;
            }, s_timeout, message: "Audio and video did not advance through the short loop.", pump: false);

            Assert.Multiple(() =>
            {
                Assert.That(frames.Select(frame => frame.LagInPasses), Has.All.InRange(-1, 1));
                Assert.That(frames.Zip(frames.Skip(1)).Any(pair => pair.Second.VideoPosition > pair.First.VideoPosition + 1),
                    Is.True, "The slow renderer must skip completed work to keep up.");
                // Three 100 ms buffers of history at most, plus boundary overlap and the previous pass.
                Assert.That(maxRetained, Is.LessThanOrEqualTo(8));
            });
        }
        finally
        {
            stopDelay.Cancel();
            await player.Pause();
        }

        void DelayRendering()
        {
            Thread.Sleep(40);
            RenderThread.Dispatcher.Dispatch(DelayRendering, Threading.DispatchPriority.Medium, delayToken);
        }
    }

    private static void StartShuttle(PlayerViewModel player, bool forward, bool fineGrain)
    {
        if (forward) player.ShuttleForward(fineGrain);
        else player.ShuttleBackward(fineGrain);
    }

    private static MediaTime Time(int frame) => MediaTime.FromFrames(frame, new Rational(Rate));

    private static int Frame(MediaTime time) => (int)time.ToFrame(new Rational(Rate), MidpointRounding.AwayFromZero);

    private static bool HasWrap(List<int> frames, int last, int first)
    {
        for (int i = 1; i < frames.Count; i++)
        {
            if (frames[i - 1] == last && frames[i] == first) return true;
        }

        return false;
    }

    private static async Task<EditViewModel> OpenEditor(int frameCount)
    {
        TestGpu.RequireAnyDevice();
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("selected-loop", 640, 360,
            frameRate: Rate, sampleRate: 48000, duration: Time(frameCount));
        RenderThreadDrain.Drain(s_timeout);
        return editor;
    }

    private static async Task AddCard(EditViewModel editor, string label, string color, int first, int end)
    {
        await TestShell.AddElementAsync(editor, () => new RectShape
        {
            Name = { Value = label },
            Width = { Value = 640 },
            Height = { Value = 360 },
            Fill = { Value = new SolidColorBrush(Color.Parse(color)) }
        }, Time(first), Time(end - first), layer: 0);
        await TestShell.AddElementAsync(editor, () => new TextBlock
        {
            Name = { Value = label },
            Text = { Value = label },
            Size = { Value = 48 },
            Fill = { Value = new SolidColorBrush(Colors.White) }
        }, Time(first), Time(end - first), layer: 1);
    }

    private sealed class PlaybackWindow : IDisposable
    {
        private readonly Window _window;
        private readonly PlayerView? _player;
        private readonly TimelineTabView _timeline;
        private readonly TimelineTabViewModel _model;
        private readonly TimelineScale _ruler;

        public PlaybackWindow(EditViewModel editor, bool includePreview = false)
        {
            _model = editor.FindToolTab<TimelineTabViewModel>()!;
            _model.Options.Value = _model.Options.Value with { Scale = 2.5f, Offset = System.Numerics.Vector2.Zero };
            _timeline = new TimelineTabView { DataContext = _model };
            Control content = _timeline;
            if (includePreview)
            {
                _player = new PlayerView { DataContext = editor.Player };
                var layout = new Grid { RowDefinitions = new RowDefinitions("*,220"), Children = { _player, _timeline } };
                Grid.SetRow(_timeline, 1);
                content = layout;
            }

            _window = new Window
            {
                Width = 1100,
                Height = includePreview ? 680 : 220,
                Content = content,
                RequestedThemeVariant = ThemeVariant.Dark
            };
            _ruler = _timeline.FindControl<TimelineScale>("Scale")!;
            _window.Show();
            HeadlessTestHelpers.Render(3);
        }

        public void Select(int first, int end)
        {
            Point At(int frame) => _ruler.TranslatePoint(
                new Point(Time(frame).TimeToPixel(_model.Options.Value.Scale) - _ruler.Offset.X, 20), _window)!.Value;

            Point from = At(first);
            Point to = At(end);
            TimelineTestHost.WaitForHitTest(_window, _ruler, from);
            RawInputModifiers modifier = TimelineTestHost.CommandModifier;
            _window.MouseMove(from, modifier);
            _window.MouseDown(from, MouseButton.Left, modifier);
            _window.MouseMove(to, modifier | RawInputModifiers.LeftMouseButton);
            _window.MouseUp(to, MouseButton.Left, modifier);
            HeadlessTestHelpers.Render();

            FrameSelectionRange? expected = first == end ? null : new FrameSelectionRange(Time(first), Time(end));
            Assert.That(_model.FrameSelection.Value, Is.EqualTo(expected));
        }

        public void ChangeSelection(FrameSelectionRange? range)
        {
            // Headless pointer helpers drain the dispatcher while playback keeps posting work.
            // The initial selection already exercises the gesture. Commit the new range here.
            if (range == null) _model.ClearSelected();
            else _model.FrameSelection.Value = range;
        }

        public async Task CaptureAsync(IEditorClock clock, int frame, string name)
        {
            string root = Environment.GetEnvironmentVariable("BEUTL_HEADLESS_CAPTURE_DIR")!;
            string directory = Path.Combine(root, nameof(SelectedRangePlaybackTests));
            Directory.CreateDirectory(directory);
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                if (Frame(clock.CurrentTime.Value) != frame) return false;
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using WriteableBitmap? image = _window.GetLastRenderedFrame();
                if (image == null) return false;
                image.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
                return true;
            }, s_timeout, pump: false);
        }

        public void Dispose()
        {
            if (_player != null) _player.DataContext = null;
            _timeline.DataContext = null;
            _window.Close();
        }
    }
}
