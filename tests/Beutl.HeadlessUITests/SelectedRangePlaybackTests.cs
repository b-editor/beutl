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
            int lastSceneFrame = shown.FindLastIndex(frame => frame > 7);
            Assert.That(lastSceneFrame, Is.GreaterThan(0), "the existing scene pass must finish before selected looping starts");
            Assert.That(shown[lastSceneFrame + 1], Is.EqualTo(4));
            Assert.That(shown.Skip(lastSceneFrame + 1), Has.All.InRange(4, 7));
            Assert.That(playing, Is.EqualTo(new[] { true }));
        }
        finally
        {
            await editor.Player.Pause();
        }
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
