using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;

using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views;

using FluentAvalonia.UI.Controls;

using Microsoft.Extensions.DependencyInjection;

using BtlColors = Beutl.Media.Colors;
using BtlPixelSize = Beutl.Media.PixelSize;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

// The preview, the seek bar's tip, the rulers and the marker lists write times in the editor's time display format, and
// the time is typed as it is shown (#4067).
[NonParallelizable]
[TestFixture]
public sealed class TimeDisplayFormatTests
{
    private static readonly MediaTime s_markerTime = MediaTime.FromSeconds(5);

    private static EditorConfig Config => GlobalConfiguration.Instance.EditorConfig;

    private static async Task<(EditViewModel Editor, PlayerView View, Window Window)> OpenPlayer(string name)
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync(name, duration: MediaTime.FromSeconds(180))).Editor;
        editor.Scene.FrameSize.Value = new BtlPixelSize(1920, 1080);
        editor.Scene.Markers.Add(new SceneMarker(s_markerTime, "Intro", BtlColors.Red));
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1200, Height = 520 };
        window.Show();
        HeadlessTestHelpers.Render();
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
        return (editor, view, window);
    }

    private static void Seek(EditViewModel editor, MediaTime time)
    {
        editor.GetRequiredService<IEditorClock>().Seek(time);
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
    }

    private static void SetFormat(TimeDisplayFormat format)
    {
        Config.TimeDisplayFormat.Value = format;
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
    }

    private static T Part<T>(Player player, string name) where T : Control
        => player.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    // Types into the time's editor and presses Enter, which the editor reads.
    private static void Submit(PlayerView view, Window window, string? text)
    {
        TextBox box = Part<TextBox>(view.Player, "PART_CurrentTimeTextBox");
        if (text is not null)
            box.Text = text;

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
    }

    [AvaloniaTest]
    [TestCase(TimeDisplayFormat.Time, "00:00:02.50", "00:03:00.00", "00:00:05.00")]
    [TestCase(TimeDisplayFormat.Timecode, "00:00:02:15", "00:03:00:00", "00:00:05:00")]
    [TestCase(TimeDisplayFormat.Frames, "75", "5400", "150")]
    public async Task The_player_writes_its_times_in_the_display_format(
        TimeDisplayFormat format, string current, string total, string marker)
    {
        TimeDisplayFormat original = Config.TimeDisplayFormat.Value;
        (EditViewModel editor, PlayerView view, Window window) = await OpenPlayer("time-format-player");
        try
        {
            SetFormat(format);
            Seek(editor, MediaTime.FromSeconds(2.5));
            HeadlessCapture.SaveIfRequested(window, nameof(TimeDisplayFormatTests), $"player-{format}");

            Assert.Multiple(() =>
            {
                Assert.That(Part<TextBlock>(view.Player, "PART_CurrentTimeTextBlock").Text, Is.EqualTo(current));
                Assert.That(Part<TextBlock>(view.Player, "PART_DurationTextBlock").Text, Is.EqualTo(total));
                Assert.That(view.Player.Markers!.Single().TimeText, Is.EqualTo(marker), "The time editor's marker list");
                Assert.That(view.Player.GetVisualDescendants().OfType<PlayerSeekBar>().Single().TimeDisplayFormat, Is.EqualTo(format));
            });
        }
        finally
        {
            window.Close();
            Config.TimeDisplayFormat.Value = original;
        }
    }

    [AvaloniaTest]
    [TestCase(TimeDisplayFormat.Time, "00:00:02.50", "00:00:04.333")]
    [TestCase(TimeDisplayFormat.Timecode, "00:00:02:15", "00:00:04:10")]
    [TestCase(TimeDisplayFormat.Frames, "75", "130")]
    public async Task The_time_is_typed_as_it_is_shown(TimeDisplayFormat format, string shown, string typed)
    {
        TimeDisplayFormat original = Config.TimeDisplayFormat.Value;
        (EditViewModel editor, PlayerView view, Window window) = await OpenPlayer("time-format-typed");
        try
        {
            SetFormat(format);
            Seek(editor, MediaTime.FromSeconds(2.5));

            view.Player.BeginEditCurrentTime();
            string? prefilled = Part<TextBox>(view.Player, "PART_CurrentTimeTextBox").Text;
            Submit(view, window, typed);

            Assert.Multiple(() =>
            {
                Assert.That(prefilled, Is.EqualTo(shown));
                Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(MediaTime.FromFrames(130, new Rational(30))));
                Assert.That(Part<TextBox>(view.Player, "PART_CurrentTimeTextBox").IsVisible, Is.False, "The edit ends.");
            });
        }
        finally
        {
            window.Close();
            Config.TimeDisplayFormat.Value = original;
        }
    }

    // Enter on the time as it is shown changes nothing, also where the playhead lies between frames.
    [AvaloniaTest]
    [TestCase(TimeDisplayFormat.Timecode)]
    [TestCase(TimeDisplayFormat.Frames)]
    public async Task Enter_on_the_time_as_it_is_shown_keeps_the_playhead(TimeDisplayFormat format)
    {
        TimeDisplayFormat original = Config.TimeDisplayFormat.Value;
        (EditViewModel editor, PlayerView view, Window window) = await OpenPlayer("time-format-unchanged");
        try
        {
            SetFormat(format);
            MediaTime betweenFrames = MediaTime.FromSeconds(2.51);
            Seek(editor, betweenFrames);

            view.Player.BeginEditCurrentTime();
            Submit(view, window, null);

            Assert.Multiple(() =>
            {
                Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(betweenFrames));
                Assert.That(Part<TextBox>(view.Player, "PART_CurrentTimeTextBox").IsVisible, Is.False, "The edit ends.");
            });
        }
        finally
        {
            window.Close();
            Config.TimeDisplayFormat.Value = original;
        }
    }

    // At 29.97 fps the timecode is drop-frame: it skips the first two numbers of a minute, and takes them typed.
    [AvaloniaTest]
    public async Task At_29_97_fps_the_timecode_drops_frames()
    {
        TimeDisplayFormat original = Config.TimeDisplayFormat.Value;
        (EditViewModel editor, PlayerView view, Window window) = await OpenPlayer("time-format-drop-frame");
        try
        {
            var rate = new Rational(30000, 1001);
            editor.Scene.FindHierarchicalParent<Project>()!.FrameRate.Value = rate;
            SetFormat(TimeDisplayFormat.Timecode);
            Seek(editor, MediaTime.FromFrames(1800, rate));
            string atAMinute = view.Player.CurrentTime;

            view.Player.BeginEditCurrentTime();
            Submit(view, window, "00:02:00:00");

            Assert.Multiple(() =>
            {
                Assert.That(atAMinute, Is.EqualTo("00:01:00;02"));
                Assert.That(editor.Player.CurrentFrame.Value, Is.EqualTo(MediaTime.FromFrames(3598, rate)));
                Assert.That(view.Player.CurrentTime, Is.EqualTo("00:02:00;02"));
            });
        }
        finally
        {
            window.Close();
            Config.TimeDisplayFormat.Value = original;
        }
    }

    // One click on the menu that a right click opens chooses the format, for every editor and the settings.
    [AvaloniaTest]
    public async Task A_right_click_on_the_time_chooses_the_format()
    {
        TimeDisplayFormat original = Config.TimeDisplayFormat.Value;
        (EditViewModel editor, PlayerView view, Window window) = await OpenPlayer("time-format-menu");
        try
        {
            SetFormat(TimeDisplayFormat.Timecode);
            Seek(editor, MediaTime.FromSeconds(2.5));
            TextBlock time = Part<TextBlock>(view.Player, "PART_CurrentTimeTextBlock");
            var menu = (FAMenuFlyout)Part<StackPanel>(view.Player, "PART_TimePanel").ContextFlyout!;

            HeadlessTestHelpers.Click(time, button: MouseButton.Right, move: true);
            HeadlessCapture.SaveIfRequested(window, nameof(TimeDisplayFormatTests), "menu");
            bool open = menu.IsOpen;
            bool editing = Part<TextBox>(view.Player, "PART_CurrentTimeTextBox").IsVisible;
            FARadioMenuFlyoutItem[] items = [.. menu.Items.OfType<FARadioMenuFlyoutItem>()];
            string?[] names = [.. items.Select(item => item.Text)];
            bool[] marked = [.. items.Select(item => item.IsChecked)];
            HeadlessTestHelpers.Click(items[2]);
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(open, Is.True);
                Assert.That(editing, Is.False, "A right click starts no edit.");
                Assert.That(names, Is.EqualTo(new[]
                {
                    SettingsStrings.TimeDisplayFormat_Time,
                    SettingsStrings.TimeDisplayFormat_Timecode,
                    SettingsStrings.TimeDisplayFormat_Frames,
                }));
                Assert.That(marked, Is.EqualTo(new[] { false, true, false }));
                Assert.That(Config.TimeDisplayFormat.Value, Is.EqualTo(TimeDisplayFormat.Frames));
                Assert.That(time.Text, Is.EqualTo("75"));
                Assert.That(menu.IsOpen, Is.False);
            });
        }
        finally
        {
            window.Close();
            Config.TimeDisplayFormat.Value = original;
        }
    }

    // A frame number gains digits as the playhead moves, and the total after it stays where it was.
    [AvaloniaTest]
    public void A_frame_number_gaining_digits_leaves_the_total_in_place()
    {
        var player = new Player { CurrentTime = "0", Duration = "5400", TimeDisplayFormat = TimeDisplayFormat.Frames };
        var window = new Window { Content = player, Width = 640, Height = 360 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            StackPanel total = Part<StackPanel>(player, "PART_DurationPanel");
            double x = total.Bounds.X;
            Assert.That(total.IsVisible, Is.True);

            foreach (string text in new[] { "9", "75", "480", "5399" })
            {
                player.CurrentTime = text;
                HeadlessTestHelpers.Render();
                Assert.That(total.Bounds.X, Is.EqualTo(x).Within(0.01), text);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_rulers_write_times_as_the_player_does()
    {
        TimeDisplayFormat original = Config.TimeDisplayFormat.Value;
        try
        {
            SetFormat(TimeDisplayFormat.Time);
            using (TimelineTestHost host = await TimelineTestHost.CreateAsync("time-format-ruler", MediaTime.FromSeconds(10)))
            {
                TimelineScale ruler = host.Ruler;
                HeadlessTestHelpers.Render();
                HeadlessCapture.SaveIfRequested(host.Window, nameof(TimeDisplayFormatTests), "ruler-Time");
                using WriteableBitmap? asTime = host.Window.CaptureRenderedFrame();
                SetFormat(TimeDisplayFormat.Frames);
                HeadlessCapture.SaveIfRequested(host.Window, nameof(TimeDisplayFormatTests), "ruler-Frames");
                using WriteableBitmap? asFrames = host.Window.CaptureRenderedFrame();

                Assert.Multiple(() =>
                {
                    Assert.That((ruler.TimeDisplayFormat, ruler.FrameRate), Is.EqualTo((TimeDisplayFormat.Frames, new Rational(30))));
                    Assert.That(Pixels(asFrames!, ruler, host.Window), Is.Not.EqualTo(Pixels(asTime!, ruler, host.Window)),
                        "The ruler draws its labels anew.");
                });
            }

            using GraphScope graph = await GraphScope.CreateAsync();
            TimelineScale graphRuler = graph.View.FindControl<TimelineScale>("scale")!;
            SetFormat(TimeDisplayFormat.Timecode);
            HeadlessCapture.SaveIfRequested(graph.Window, nameof(TimeDisplayFormatTests), "graph-ruler-Timecode");
            Assert.That((graphRuler.TimeDisplayFormat, graphRuler.FrameRate), Is.EqualTo((TimeDisplayFormat.Timecode, new Rational(30))));
        }
        finally
        {
            Config.TimeDisplayFormat.Value = original;
        }
    }

    [AvaloniaTest]
    public void The_settings_page_chooses_the_format()
    {
        TestGpu.RequireAnyDevice();

        TimeDisplayFormat original = Config.TimeDisplayFormat.Value;
        string? priorGpu = GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName.Value;
        Config.TimeDisplayFormat.Value = TimeDisplayFormat.Timecode;
        using var viewModel = new EditorSettingsPageViewModel();
        var page = new EditorSettingsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 900, Height = 700, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            OptionsDisplayItem row = page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                .Single(x => Equals(x.Header, SettingsStrings.TimeDisplayFormat));
            row.BringIntoView();
            HeadlessTestHelpers.Render(3);
            var comboBox = (ComboBox)row.ActionButton!;
            int shown = comboBox.SelectedIndex;
            HeadlessCapture.SaveIfRequested(window, nameof(TimeDisplayFormatTests), "settings");

            comboBox.SelectedIndex = 2;
            TimeDisplayFormat picked = Config.TimeDisplayFormat.Value;
            comboBox.SelectedIndex = -1;

            Assert.Multiple(() =>
            {
                Assert.That(comboBox.Items.Cast<ComboBoxItem>().Select(x => x.Content), Is.EqualTo(new object[]
                {
                    SettingsStrings.TimeDisplayFormat_Time,
                    SettingsStrings.TimeDisplayFormat_Timecode,
                    SettingsStrings.TimeDisplayFormat_Frames,
                }));
                Assert.That(shown, Is.EqualTo(1));
                Assert.That(picked, Is.EqualTo(TimeDisplayFormat.Frames));
                Assert.That(Config.TimeDisplayFormat.Value, Is.EqualTo(TimeDisplayFormat.Frames), "No selection is no format.");
            });
        }
        finally
        {
            window.Close();
            Config.TimeDisplayFormat.Value = original;
            GlobalConfiguration.Instance.GraphicsConfig.SelectedGpuName.Value = priorGpu;
        }
    }

    // The ruler's pixels in the frame.
    private static byte[] Pixels(WriteableBitmap frame, Control ruler, Window window)
    {
        Point origin = ruler.TranslatePoint(default, window)!.Value;
        var area = new PixelRect((int)origin.X, (int)origin.Y, (int)ruler.Bounds.Width, (int)ruler.Bounds.Height);
        using ILockedFramebuffer buffer = frame.Lock();
        var pixels = new byte[area.Width * area.Height * 4];
        for (int y = 0; y < area.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                buffer.Address + ((area.Y + y) * buffer.RowBytes) + (area.X * 4), pixels, y * area.Width * 4, area.Width * 4);
        }

        return pixels;
    }
}
