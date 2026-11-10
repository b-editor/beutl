using System.Reflection;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;

using Microsoft.Extensions.DependencyInjection;

using BtlMatrix = Beutl.Graphics.Matrix;
using BtlPixelSize = Beutl.Media.PixelSize;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public sealed class PlayerViewControlsTests
{
    private static async Task<EditViewModel> OpenEditor()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("preview-controls")).Editor;
        editor.Scene.FrameSize.Value = new BtlPixelSize(1920, 1080);
        return editor;
    }

    private static async Task Prepare(PlayerView view, Window window)
    {
        window.Show();
        HeadlessTestHelpers.Render();
        await RenderThread.Dispatcher.InvokeAsync(static () => { });
        HeadlessTestHelpers.Settle();
        HeadlessTestHelpers.Render();
        Assert.That(view.image.Bounds.Width, Is.GreaterThan(0));
    }

    private static T Part<T>(Player player, string name) where T : Control
        => player.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    // Picks an entry of a combo box as a person does, through its open list.
    private static void Choose(ComboBox comboBox, Window window, int index)
    {
        comboBox.IsDropDownOpen = true;
        HeadlessTestHelpers.Render(2);
        var item = (Control)comboBox.ContainerFromIndex(index)!;
        TimelineTestHost.WaitForHitTest(window, item, HeadlessTestHelpers.Center(item, window));
        HeadlessTestHelpers.Click(item);
    }

    // What a combo box shows while closed: its selected item, or its placeholder when nothing is selected.
    private static string? ShownText(ComboBox comboBox)
    {
        Control[] parts = [.. comboBox.GetVisualDescendants().OfType<Control>()];
        TextBlock placeholder = parts.OfType<TextBlock>().Single(part => part.Name == "PlaceholderTextBlock");
        return placeholder.IsVisible
            ? placeholder.Text
            : parts.Single(part => part.Name == "ContentPresenter").GetVisualDescendants().OfType<TextBlock>().Single().Text;
    }

    private static Point PreviewCenter(PlayerView view, Window window)
        => view.image.TranslatePoint(new Point(view.image.Bounds.Width / 2, view.image.Bounds.Height / 2), window)!.Value;

    // The time editor is wider than the time, and takes more of the footer's leading part.
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_footer_parts_never_overlap_or_leave_the_panel(bool editingTime)
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1200, Height = 400 };
        try
        {
            await Prepare(view, window);
            PlayerFooterPanel footer = Part<PlayerFooterPanel>(view.Player, "PART_FooterPanel");
            TextBox timeEditor = Part<TextBox>(view.Player, "PART_CurrentTimeTextBox");
            if (editingTime)
                view.Player.BeginEditCurrentTime();

            for (int width = 1200; width >= 180; width -= 7)
            {
                window.Width = width;
                HeadlessTestHelpers.Render();

                Rect[] parts = [.. footer.Children.Where(child => child.IsVisible).Select(child => child.Bounds)];
                Assert.Multiple(() =>
                {
                    Assert.That(timeEditor.IsVisible, Is.EqualTo(editingTime), $"at width {width}");
                    for (int i = 0; i < parts.Length; i++)
                    {
                        Rect part = parts[i];
                        Assert.That(part.X, Is.GreaterThanOrEqualTo(0), $"{part} at width {width}");
                        Assert.That(part.Right, Is.LessThanOrEqualTo(footer.Bounds.Width), $"{part} at width {width}");
                        for (int j = i + 1; j < parts.Length; j++)
                            Assert.That(part.Intersects(parts[j]), Is.False, $"{part} and {parts[j]} at width {width}");
                    }
                });
            }
        }
        finally
        {
            window.Close();
        }
    }

    // What lies below the frame comes out of the frame's height, and a wide frame loses nearly twice that in width.
    [AvaloniaTest]
    public async Task The_controls_below_the_frame_stay_small()
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1200, Height = 520 };
        try
        {
            await Prepare(view, window);
            double frameBottom = view.image.TranslatePoint(new Point(0, view.image.Bounds.Height), view)!.Value.Y;
            Slider seekBar = Part<Slider>(view.Player, "PART_Slider");
            Thumb thumb = seekBar.GetVisualDescendants().OfType<Thumb>().Single();
            double thumbTop = thumb.TranslatePoint(default, seekBar)!.Value.Y;

            Assert.Multiple(() =>
            {
                Assert.That(view.Bounds.Height - frameBottom, Is.LessThanOrEqualTo(40));
                Assert.That(thumbTop, Is.GreaterThanOrEqualTo(-0.5), "The bar must hold its thumb.");
                Assert.That(thumbTop + thumb.Bounds.Height, Is.LessThanOrEqualTo(seekBar.Bounds.Height + 0.5), "The bar must hold its thumb.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // The thumb is small, so the bar's whole height has to take clicks, and the frame and the buttons keep the rest.
    // The dock's line under the panel lies 2 px below the view, and belongs to the space under the buttons.
    [AvaloniaTest]
    public async Task The_space_above_and_below_the_buttons_matches()
    {
        const double SpaceBelowTheView = 2;
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1200, Height = 520 };
        try
        {
            await Prepare(view, window);
            Control track = Part<Slider>(view.Player, "PART_Slider").GetVisualDescendants().OfType<Control>()
                .First(control => control.Name == "PART_Track");
            double trackLine = track.TranslatePoint(new Point(0, (track.Bounds.Height / 2) + 2), view)!.Value.Y;
            StackPanel buttons = Part<StackPanel>(view.Player, "PART_TransportPanel");
            double top = buttons.TranslatePoint(default, view)!.Value.Y;
            double bottom = top + buttons.Bounds.Height;

            Assert.Multiple(() =>
            {
                Assert.That(buttons.Bounds.Height, Is.EqualTo(28).Within(0.5));
                Assert.That(view.Bounds.Height - bottom + SpaceBelowTheView, Is.EqualTo(top - trackLine).Within(0.5));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_last_arrow_ends_just_inside_the_end_of_the_seek_bar()
    {
        // The arrow's glyph ends this far inside its box.
        const double GlyphInset = 3;
        const double SpaceAfterTheArrow = 5;
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1200, Height = 520 };
        try
        {
            await Prepare(view, window);
            Control seekBar = Part<Slider>(view.Player, "PART_Slider");
            Control arrow = view.resolutionComboBox.GetVisualDescendants().OfType<Control>()
                .Single(control => control.Name == "DropDownGlyph");
            double seekBarEnd = seekBar.TranslatePoint(new Point(seekBar.Bounds.Width, 0), view)!.Value.X;
            double arrowEnd = arrow.TranslatePoint(new Point(arrow.Bounds.Width, 0), view)!.Value.X - GlyphInset;

            Assert.That(seekBarEnd - arrowEnd, Is.EqualTo(SpaceAfterTheArrow).Within(1));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_seek_bar_takes_clicks_over_its_height_and_no_further()
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1200, Height = 520 };
        try
        {
            await Prepare(view, window);
            Slider seekBar = Part<Slider>(view.Player, "PART_Slider");
            Control track = seekBar.GetVisualDescendants().OfType<Control>().First(control => control.Name == "PART_Track");
            // Clear of the buttons, which overlap the bottom of the bar and take its clicks there.
            Point top = seekBar.TranslatePoint(new Point(seekBar.Bounds.Width * 0.7, 0), window)!.Value;
            TimelineTestHost.WaitForHitTest(window, seekBar, top + new Vector(0, seekBar.Bounds.Height / 2));

            bool InSeekBar(double y) => window.InputHitTest(top + new Vector(0, y)) is Visual hit
                && (hit == seekBar || seekBar.IsVisualAncestorOf(hit));

            Assert.Multiple(() =>
            {
                Assert.That(track.Bounds.Height, Is.EqualTo(seekBar.Bounds.Height).Within(1));
                Assert.That(InSeekBar(1.5), Is.True, "The top of the bar");
                Assert.That(InSeekBar(seekBar.Bounds.Height / 2), Is.True, "The middle of the bar");
                Assert.That(InSeekBar(seekBar.Bounds.Height - 1.5), Is.True, "The bottom of the bar");
                Assert.That(InSeekBar(-2), Is.False, "Above the bar is the frame's");
                Assert.That(InSeekBar(seekBar.Bounds.Height + 2), Is.False, "Below the bar is the footer's");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_small_seek_bar_thumb_can_still_be_dragged()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 1200, Height = 520 };
        Point point = default;
        try
        {
            await Prepare(view, window);
            Thumb thumb = Part<Slider>(view.Player, "PART_Slider").GetVisualDescendants().OfType<Thumb>().Single();
            Border circle = thumb.GetVisualDescendants().OfType<Border>().First();
            Assert.Multiple(() =>
            {
                Assert.That(circle.Bounds.Width, Is.EqualTo(10).Within(0.5));
                Assert.That(circle.Bounds.Height, Is.EqualTo(10).Within(0.5));
                Assert.That(circle.CornerRadius.TopLeft, Is.EqualTo(5), "It must draw a circle, not a rounded square.");
            });

            point = HeadlessTestHelpers.Center(circle, window);
            TimelineTestHost.WaitForHitTest(window, thumb, point);
            MediaTime before = player.CurrentFrame.Value;

            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(200, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(point + new Vector(200, 0), MouseButton.Left);
            HeadlessTestHelpers.Settle();

            Assert.That(player.CurrentFrame.Value, Is.GreaterThan(before));
        }
        finally
        {
            window.MouseUp(point, MouseButton.Left);
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(1000, true, 1)]
    [TestCase(600, true, 1)]
    [TestCase(500, false, 1)]
    [TestCase(420, false, 1)]
    [TestCase(320, false, 2)]
    public async Task The_total_time_gives_way_before_the_footer_takes_a_second_row(int width, bool showsTotal, int rows)
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = width, Height = 400 };
        try
        {
            await Prepare(view, window);
            double row = Part<StackPanel>(view.Player, "PART_TransportPanel").Bounds.Height;

            Assert.Multiple(() =>
            {
                Assert.That(Part<StackPanel>(view.Player, "PART_DurationPanel").IsVisible, Is.EqualTo(showsTotal));
                Assert.That(Part<PlayerFooterPanel>(view.Player, "PART_FooterPanel").Bounds.Height, Is.EqualTo(row * rows).Within(rows * 2));
            });
        }
        finally
        {
            window.Close();
        }
    }

    // Each window leaves the frame less room in height than in width, so a taller footer would shrink it. Between about 530
    // and 583 px the time's editor beside the total time did not fit the footer's single row.
    [AvaloniaTest]
    [TestCase(1200, 520)]
    [TestCase(560, 320)]
    public async Task The_frame_does_not_jump_when_the_time_is_edited_or_the_shuttle_runs(int width, int height)
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = width, Height = height };
        try
        {
            await Prepare(view, window);
            Rect frame = view.image.Bounds;

            view.Player.BeginEditCurrentTime();
            HeadlessTestHelpers.Render();
            Assert.That(Part<TextBox>(view.Player, "PART_CurrentTimeTextBox").IsVisible, Is.True);
            Assert.That(view.image.Bounds, Is.EqualTo(frame), "Editing the time");

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.That(Part<StackPanel>(view.Player, "PART_DurationPanel").IsVisible, Is.True, "The total after the edit");
            player.PlaybackDirection.Value = PlaybackDirection.Forward;
            player.PlaybackSpeed.Value = 32f;
            HeadlessTestHelpers.Render();
            Assert.That(view.image.Bounds, Is.EqualTo(frame), "Showing the shuttle's rate");
        }
        finally
        {
            player.PlaybackDirection.Value = PlaybackDirection.Stopped;
            player.PlaybackSpeed.Value = 1f;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_transport_stays_centered_while_the_footer_has_one_row()
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 1000, Height = 400 };
        try
        {
            await Prepare(view, window);
            PlayerFooterPanel footer = Part<PlayerFooterPanel>(view.Player, "PART_FooterPanel");
            Rect transport = Part<StackPanel>(view.Player, "PART_TransportPanel").Bounds;
            Assert.That(transport.Center.X, Is.EqualTo(footer.Bounds.Width / 2).Within(0.5));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void The_time_keeps_its_width_as_its_digits_change()
    {
        var player = new Player { CurrentTime = "00:00:00.00" };
        var window = new Window { Content = player, Width = 640, Height = 360 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBlock time = Part<TextBlock>(player, "PART_CurrentTimeTextBlock");
            double width = time.Bounds.Width;
            Assert.That(width, Is.GreaterThan(0));

            foreach (string text in new[] { "00:00:01.11", "00:11:11.11", "00:47:08.09" })
            {
                player.CurrentTime = text;
                HeadlessTestHelpers.Render();
                Assert.That(time.Bounds.Width, Is.EqualTo(width).Within(0.01), text);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_total_time_is_where_the_last_frame_ends()
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 400 };
        try
        {
            await Prepare(view, window);
            Scene scene = editor.Scene;
            string expected = editor.Player.FormatTime(scene.Start.Value + scene.Duration.Value);

            Assert.That(Part<TextBlock>(view.Player, "PART_DurationTextBlock").Text, Is.EqualTo(expected));
        }
        finally
        {
            window.Close();
        }
    }

    // The seek bar reaches a playhead or an element past the end of the scene, and the total stays where the scene ends.
    [AvaloniaTest]
    public async Task The_total_time_ignores_what_lies_past_the_end_of_the_scene()
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 400 };
        try
        {
            await Prepare(view, window);
            Scene scene = editor.Scene;
            MediaTime end = scene.Start.Value + scene.Duration.Value;
            string expected = editor.Player.FormatTime(end);
            TextBlock total = Part<TextBlock>(view.Player, "PART_DurationTextBlock");

            MediaTime playhead = end + MediaTime.FromSeconds(10);
            editor.GetRequiredService<IEditorClock>().Seek(playhead);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.Player.Maximum, Is.GreaterThan(end.ToTimeSpan().TotalSeconds), "The seek bar reaches the playhead.");
                Assert.That(total.Text, Is.EqualTo(expected), "A playhead past the end");
            });

            MediaTime elementEnd = playhead + MediaTime.FromSeconds(10);
            scene.Children.Add(new Element { Start = { Value = playhead }, Length = { Value = elementEnd - playhead } });
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.Player.Maximum, Is.GreaterThan(playhead.ToTimeSpan().TotalSeconds), "The seek bar reaches the element.");
                Assert.That(total.Text, Is.EqualTo(expected), "An element past the end");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase("PART_StartButton")]
    [TestCase("PART_PreviousButton")]
    [TestCase("PART_PlayButton")]
    [TestCase("PART_NextButton")]
    [TestCase("PART_EndButton")]
    [TestCase("PART_LoopButton")]
    public void Every_transport_button_has_a_tip_and_a_name(string part)
    {
        var player = new Player();
        var window = new Window { Content = player, Width = 640, Height = 360 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Control button = Part<Control>(player, part);

            Assert.Multiple(() =>
            {
                Assert.That(ToolTip.GetTip(button), Is.Not.Null.And.Not.Empty);
                Assert.That(AutomationProperties.GetName(button), Is.EqualTo(ToolTip.GetTip(button)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_tip_shows_the_shortcut_as_it_opens()
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 400 };
        try
        {
            await Prepare(view, window);
            Control play = Part<Control>(view.Player, "PART_PlayButton");
            ToolTip.SetShowDelay(play, 0);
            Point center = HeadlessTestHelpers.Center(play, window);
            TimelineTestHost.WaitForHitTest(window, play, center);

            window.MouseMove(center);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(ToolTip.GetIsOpen(play), Is.True);
                Assert.That(ToolTip.GetTip(play), Is.EqualTo($"{Strings.PlayPause} (Space)"));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase("guidesToggle", "Alt+G")]
    [TestCase("onionSkinToggle", "Alt+O")]
    [TestCase("checkerboardToggle", "Alt+T")]
    public async Task A_preview_toggles_tip_shows_its_shortcut_as_it_opens(string name, string shortcut)
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 400 };
        try
        {
            await Prepare(view, window);
            ToggleButton toggle = view.FindControl<ToggleButton>(name)!;
            string label = (string)ToolTip.GetTip(toggle)!;
            ToolTip.SetShowDelay(toggle, 0);
            Point center = HeadlessTestHelpers.Center(toggle, window);
            TimelineTestHost.WaitForHitTest(window, toggle, center);

            window.MouseMove(center);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(ToolTip.GetIsOpen(toggle), Is.True);
                Assert.That(ToolTip.GetTip(toggle), Is.EqualTo($"{label} ({KeyGesture.Parse(shortcut).ToString("p", null)})"));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [TestCase(1f, PlaybackDirection.Stopped, null)]
    [TestCase(2f, PlaybackDirection.Stopped, null)]
    [TestCase(1f, PlaybackDirection.Forward, null)]
    [TestCase(2f, PlaybackDirection.Forward, "2×")]
    [TestCase(0.5f, PlaybackDirection.Forward, "0.5×")]
    [TestCase(0.25f, PlaybackDirection.Forward, "0.25×")]
    [TestCase(1f, PlaybackDirection.Backward, "−1×")]
    [TestCase(32f, PlaybackDirection.Backward, "−32×")]
    public void The_playback_rate_reads_as_a_factor(float speed, PlaybackDirection direction, string? expected)
    {
        Assert.That(PlayerViewModel.FormatPlaybackRate(speed, direction), Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public async Task The_rate_takes_the_place_of_the_total_time_while_shuttling()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 400 };
        try
        {
            await Prepare(view, window);
            Control badge = Part<Border>(view.Player, "PART_PlaybackRateBadge");
            Control total = Part<StackPanel>(view.Player, "PART_DurationPanel");
            Assert.Multiple(() =>
            {
                Assert.That(badge.IsVisible, Is.False);
                Assert.That(total.IsVisible, Is.True);
            });

            player.PlaybackDirection.Value = PlaybackDirection.Backward;
            player.PlaybackSpeed.Value = 4f;
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(Part<TextBlock>(view.Player, "PART_PlaybackRateTextBlock").Text, Is.EqualTo("−4×"));
                Assert.That(badge.IsVisible, Is.True);
                Assert.That(total.IsVisible, Is.False);
            });

            player.PlaybackDirection.Value = PlaybackDirection.Stopped;
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(badge.IsVisible, Is.False);
                Assert.That(total.IsVisible, Is.True);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_zoom_commands_step_through_the_levels_about_the_center()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            double fit = view.ScreenZoom!.Value;
            Assert.That(fit, Is.GreaterThan(0.25).And.LessThan(0.5), "The steps below assume this fit.");
            Point center = PreviewCenter(view, window);

            player.RequestPreviewZoom(PreviewZoomAction.ZoomIn);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(view.ScreenZoom, Is.EqualTo(0.5).Within(1e-3));
                Assert.That(view.ZoomLabel, Is.EqualTo("50%"));
                // Layout rounding can leave the image half a pixel off the panel's center, which the zoom scales.
                Assert.That(PreviewCenter(view, window).X, Is.EqualTo(center.X).Within(1));
                Assert.That(PreviewCenter(view, window).Y, Is.EqualTo(center.Y).Within(1));
            });

            player.RequestPreviewZoom(PreviewZoomAction.ZoomIn);
            HeadlessTestHelpers.Settle();
            Assert.That(view.ScreenZoom, Is.EqualTo(1).Within(1e-3));

            player.RequestPreviewZoom(PreviewZoomAction.ZoomOut);
            player.RequestPreviewZoom(PreviewZoomAction.ZoomOut);
            HeadlessTestHelpers.Settle();
            Assert.That(view.ScreenZoom, Is.EqualTo(0.25).Within(1e-3));

            player.RequestPreviewZoom(PreviewZoomAction.Fit);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(player.FrameMatrix.Value, Is.EqualTo(BtlMatrix.Identity));
                Assert.That(view.ScreenZoom, Is.EqualTo(fit).Within(1e-6));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_zoom_box_offers_fit_and_the_levels_and_follows_the_zoom()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            ComboBox zoom = view.zoomComboBox;
            PreviewZoomOption[] options = [.. zoom.Items.Cast<PreviewZoomOption>()];
            Assert.That(options.Select(option => option.Label),
                Is.EqualTo(new[] { Strings.PreviewZoomFit, "10%", "25%", "50%", "100%", "200%", "400%", "800%" }));
            Assert.Multiple(() =>
            {
                Assert.That(zoom.SelectedItem, Is.SameAs(options[0]));
                Assert.That(ShownText(zoom), Does.EndWith("%").And.Not.EqualTo("100%"), "Fitting still shows the zoom it comes to.");
            });

            Choose(zoom, window, 4);
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(view.ScreenZoom, Is.EqualTo(1).Within(1e-3));
                Assert.That(zoom.SelectedItem, Is.SameAs(options[4]));
                Assert.That(ShownText(zoom), Is.EqualTo("100%"));
            });

            player.FrameMatrix.Value *= BtlMatrix.CreateScale(1.5f, 1.5f);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(zoom.SelectedItem, Is.Null, "No level matches this zoom.");
                Assert.That(ShownText(zoom), Is.EqualTo("150%"));
            });

            Choose(zoom, window, 0);
            HeadlessTestHelpers.Settle();
            Assert.That(player.FrameMatrix.Value, Is.EqualTo(BtlMatrix.Identity));
        }
        finally
        {
            window.Close();
        }
    }

    // The list keeps its items between openings, and a shortcut can change in the key map meanwhile.
    [AvaloniaTest]
    public async Task The_zoom_list_shows_the_current_shortcut_each_time_it_opens()
    {
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        ContextCommandManager manager = TestShell.MainViewModel.ContextCommandManager!;
        OSPlatform platform = OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsWindows() ? OSPlatform.Windows : OSPlatform.Linux;
        ContextCommandEntry fit = manager.GetDefinitions<SceneEditorExtension>()
            .Single(command => command.Definition.Id == "editor.previewZoomFit");
        KeyGesture original = fit.KeyGestures.First(item => item.Platform == platform).KeyGesture!;
        var changed = KeyGesture.Parse("Alt+D9");
        try
        {
            await Prepare(view, window);
            ComboBox zoom = view.zoomComboBox;

            string?[] ShownInTheList()
            {
                zoom.IsDropDownOpen = true;
                HeadlessTestHelpers.Render(2);
                string?[] texts = [.. ((Control)zoom.ContainerFromIndex(0)!).GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)];
                zoom.IsDropDownOpen = false;
                HeadlessTestHelpers.Render(2);
                return texts;
            }

            Assert.That(ShownInTheList(), Does.Contain(original.ToString("p", null)));

            manager.ChangeKeyGesture(fit, changed, platform);

            Assert.That(ShownInTheList(), Does.Contain(changed.ToString("p", null)));
        }
        finally
        {
            manager.ChangeKeyGesture(fit, original, platform);
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_zoom_commands_keep_the_zoom_box_in_step()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            MediaTime time = player.CurrentFrame.Value;
            BtlMatrix before = player.FrameMatrix.Value;

            player.RequestPreviewZoom(PreviewZoomAction.ZoomIn);
            HeadlessTestHelpers.Settle();
            var selected = (PreviewZoomOption?)view.zoomComboBox.SelectedItem;

            Assert.Multiple(() =>
            {
                Assert.That(selected?.Level, Is.EqualTo(0.5));
                Assert.That(player.FrameMatrix.Value, Is.Not.EqualTo(before));
                Assert.That(view.ScreenZoom, Is.EqualTo(0.5).Within(1e-3), "Selecting the option in step must not zoom again.");
                Assert.That(player.CurrentFrame.Value, Is.EqualTo(time));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.Control)]
    [TestCase(RawInputModifiers.Meta)]
    public async Task The_command_key_and_the_wheel_zoom_with_every_tool(RawInputModifiers modifier)
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            Point point = PreviewCenter(view, window);

            foreach (Action selectTool in new Action[]
                     {
                         () => player.IsMoveMode.Value = true,
                         () => player.IsCropMode.Value = true,
                         () => player.IsCameraMode.Value = true,
                         () => player.IsHandMode.Value = true,
                     })
            {
                player.IsMoveMode.Value = player.IsCropMode.Value = player.IsCameraMode.Value = player.IsHandMode.Value = false;
                selectTool();
                player.FrameMatrix.Value = BtlMatrix.Identity;

                window.MouseWheel(point, new Vector(0, 1), modifier);

                Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(1.2f).Within(1e-3));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_zoom_stops_at_its_limits()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            Point point = PreviewCenter(view, window);

            window.MouseWheel(point, new Vector(0, 200), RawInputModifiers.Control);
            Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(64f));

            window.MouseWheel(point, new Vector(0, -200), RawInputModifiers.Control);
            Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(0.05f));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_plain_wheel_pans_a_zoomed_frame_and_leaves_a_fitted_one_alone()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            Point point = PreviewCenter(view, window);

            window.MouseWheel(point, new Vector(0, 1));
            Assert.That(player.FrameMatrix.Value, Is.EqualTo(BtlMatrix.Identity));

            player.RequestPreviewZoom(PreviewZoomAction.ZoomIn);
            BtlMatrix zoomed = player.FrameMatrix.Value;
            window.MouseWheel(point, new Vector(0, 1));
            Assert.Multiple(() =>
            {
                Assert.That(player.FrameMatrix.Value.M11, Is.EqualTo(zoomed.M11));
                Assert.That(player.FrameMatrix.Value.M32, Is.EqualTo(zoomed.M32 + 24).Within(1e-3));
            });

            zoomed = player.FrameMatrix.Value;
            player.IsMoveMode.Value = false;
            player.IsCameraMode.Value = true;
            window.MouseWheel(point, new Vector(0, 1));
            Assert.That(player.FrameMatrix.Value, Is.EqualTo(zoomed), "In camera mode the wheel belongs to the camera.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_alt_keys_zoom_the_preview()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("preview-zoom-keys", 1920, 1080);
        // EditorHostView attaches the editor's commands this way.
        var editView = new EditView { DataContext = editor };
        TestShell.MainViewModel.ContextCommandManager.Attach(editView, SceneEditorExtension.Instance);
        var window = new Window { Content = editView, Width = 1400, Height = 900 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(3);
            PlayerView view = window.GetVisualDescendants().OfType<PlayerView>().Single();
            view.framePanel.Focus();
            double fit = view.ScreenZoom!.Value;

            window.KeyPress(Key.OemPlus, RawInputModifiers.Alt, PhysicalKey.Equal, "=");
            window.KeyRelease(Key.OemPlus, RawInputModifiers.Alt, PhysicalKey.Equal, "=");
            double zoomedIn = view.ScreenZoom!.Value;
            Assert.That(zoomedIn, Is.GreaterThan(fit));

            window.KeyPress(Key.OemPlus, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.Equal, "+");
            window.KeyRelease(Key.OemPlus, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.Equal, "+");
            Assert.That(view.ScreenZoom, Is.GreaterThan(zoomedIn));

            window.KeyPress(Key.OemMinus, RawInputModifiers.Alt, PhysicalKey.Minus, "-");
            window.KeyRelease(Key.OemMinus, RawInputModifiers.Alt, PhysicalKey.Minus, "-");
            Assert.That(view.ScreenZoom, Is.EqualTo(zoomedIn).Within(1e-3));

            window.KeyPress(Key.D0, RawInputModifiers.Alt, PhysicalKey.Digit0, "0");
            window.KeyRelease(Key.D0, RawInputModifiers.Alt, PhysicalKey.Digit0, "0");
            Assert.That(editor.Player.FrameMatrix.Value, Is.EqualTo(BtlMatrix.Identity));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(Key.Right, 1)]
    [TestCase(Key.Left, -1)]
    public async Task The_arrow_keys_step_one_frame_after_the_seek_bar_was_clicked(Key key, int frames)
    {
        (_, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(
            "preview-seek-keys", 1920, 1080, duration: MediaTime.FromSeconds(60));
        var editView = new EditView { DataContext = editor };
        TestShell.MainViewModel.ContextCommandManager.Attach(editView, SceneEditorExtension.Instance);
        var window = new Window { Content = editView, Width = 1400, Height = 900 };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(3);
            PlayerView view = window.GetVisualDescendants().OfType<PlayerView>().Single();
            Slider slider = Part<Slider>(view.Player, "PART_Slider");
            Point point = HeadlessTestHelpers.Center(slider, window);
            TimelineTestHost.WaitForHitTest(window, slider, point);

            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Settle();
            MediaTime sought = editor.Player.CurrentFrame.Value;
            Assert.That(sought, Is.GreaterThan(MediaTime.Zero), "The click must seek.");

            PhysicalKey physical = key == Key.Right ? PhysicalKey.ArrowRight : PhysicalKey.ArrowLeft;
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            HeadlessTestHelpers.Settle();

            Assert.That(editor.Player.CurrentFrame.Value,
                Is.EqualTo(sought + MediaTime.FromFrames(frames, editor.Player.GetFrameRate())));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_frame_outline_stays_one_screen_pixel_outside_the_image()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            foreach (float zoom in new[] { 1f, 2f, 0.5f })
            {
                player.FrameMatrix.Value = BtlMatrix.CreateScale(zoom, zoom);
                HeadlessTestHelpers.Settle();

                Assert.Multiple(() =>
                {
                    Assert.That(view.imageBackground.BoxShadow[0].Spread, Is.EqualTo(1 / zoom).Within(1e-6));
                    Assert.That(view.imageBackground.Bounds, Is.EqualTo(view.image.Bounds));
                });
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Changing_the_resolution_pauses_first_and_keeping_it_does_not()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            player.IsPlaying.Value = true;

            await view.ChangePreviewScaleAsync(player, RenderScale.Full);
            Assert.That(player.IsPlaying.Value, Is.True);

            Choose(view.resolutionComboBox, window, Array.IndexOf(editor.PreviewScaleOptions, RenderScale.Half));
            await HeadlessTestHelpers.WaitUntilAsync(() => editor.PreviewScale.Value == RenderScale.Half);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(player.IsPlaying.Value, Is.False);
                Assert.That(ShownText(view.resolutionComboBox), Is.EqualTo(Strings.RenderScale_Half));
            });

            editor.PreviewScale.Value = RenderScale.FitToPreviewer;
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();
            Assert.That(ShownText(view.resolutionComboBox), Is.EqualTo(Strings.RenderScale_FitToPreviewer_Short));
        }
        finally
        {
            await player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_last_resolution_chosen_during_a_pause_wins()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);

            // The first choice waits for the pause, and the second comes back to the scale in use.
            player.IsPlaying.Value = true;
            await Task.WhenAll(
                view.ChangePreviewScaleAsync(player, RenderScale.Half),
                view.ChangePreviewScaleAsync(player, RenderScale.Full));
            HeadlessTestHelpers.Settle();
            Assert.That(editor.PreviewScale.Value, Is.EqualTo(RenderScale.Full));

            player.IsPlaying.Value = true;
            await Task.WhenAll(
                view.ChangePreviewScaleAsync(player, RenderScale.Half),
                view.ChangePreviewScaleAsync(player, RenderScale.Quarter));
            HeadlessTestHelpers.Settle();
            Assert.That(editor.PreviewScale.Value, Is.EqualTo(RenderScale.Quarter));
        }
        finally
        {
            await player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_resolution_box_goes_back_when_the_pause_fails()
    {
        EditViewModel editor = await OpenEditor();
        PlayerViewModel player = editor.Player;
        var view = new PlayerView { DataContext = player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);
            RenderScale inUse = editor.PreviewScale.Value;
            // A pause still running is what the next caller waits for.
            FieldInfo pauseTask = typeof(PlayerViewModel).GetField("_pauseTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var pause = new TaskCompletionSource();
            pauseTask.SetValue(player, pause.Task);

            view.resolutionComboBox.SelectedItem = RenderScale.Half;
            HeadlessTestHelpers.Settle();
            Assert.That(view.resolutionComboBox.SelectedItem, Is.EqualTo(RenderScale.Half));

            pause.SetException(new InvalidOperationException("The pause failed."));
            await pause.Task.ContinueWith(static _ => { }, TaskScheduler.Default);
            HeadlessTestHelpers.Settle();
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(editor.PreviewScale.Value, Is.EqualTo(inUse));
                Assert.That(view.resolutionComboBox.SelectedItem, Is.EqualTo(inUse));
                Assert.That(pauseTask.GetValue(player), Is.SameAs(pause.Task), "Going back must not start another pause.");
            });
        }
        finally
        {
            await player.Pause();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_checkerboard_command_turns_the_setting_on_and_off()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool saved = config.IsPreviewCheckerboardEnabled.Value;
        config.IsPreviewCheckerboardEnabled.Value = false;
        EditViewModel editor = await OpenEditor();
        var toggle = new ContextCommandExecution("editor.togglePreviewCheckerboard");
        try
        {
            await SceneEditorExtension.Instance.RunAsync(editor, toggle);
            bool on = config.IsPreviewCheckerboardEnabled.Value;
            await SceneEditorExtension.Instance.RunAsync(editor, toggle);

            Assert.Multiple(() =>
            {
                Assert.That(on, Is.True);
                Assert.That(config.IsPreviewCheckerboardEnabled.Value, Is.False);
            });
        }
        finally
        {
            config.IsPreviewCheckerboardEnabled.Value = saved;
        }
    }

    [AvaloniaTest]
    public async Task The_view_toggles_and_the_settings_stay_in_step()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        bool guides = config.IsPreviewGuideEnabled.Value;
        bool onion = config.IsOnionSkinEnabled.Value;
        bool checkerboard = config.IsPreviewCheckerboardEnabled.Value;
        config.IsPreviewGuideEnabled.Value = false;
        config.IsOnionSkinEnabled.Value = false;
        config.IsPreviewCheckerboardEnabled.Value = false;
        EditViewModel editor = await OpenEditor();
        var view = new PlayerView { DataContext = editor.Player };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            await Prepare(view, window);

            view.guidesToggle.IsChecked = true;
            view.onionSkinToggle.IsChecked = true;
            view.checkerboardToggle.IsChecked = true;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(config.IsPreviewGuideEnabled.Value, Is.True);
                Assert.That(config.IsOnionSkinEnabled.Value, Is.True);
                Assert.That(config.IsPreviewCheckerboardEnabled.Value, Is.True);
            });

            config.IsPreviewGuideEnabled.Value = false;
            config.IsOnionSkinEnabled.Value = false;
            config.IsPreviewCheckerboardEnabled.Value = false;
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(view.guidesToggle.IsChecked, Is.False);
                Assert.That(view.onionSkinToggle.IsChecked, Is.False);
                Assert.That(view.checkerboardToggle.IsChecked, Is.False);
            });
        }
        finally
        {
            window.Close();
            config.IsPreviewGuideEnabled.Value = guides;
            config.IsOnionSkinEnabled.Value = onion;
            config.IsPreviewCheckerboardEnabled.Value = checkerboard;
        }
    }
}
