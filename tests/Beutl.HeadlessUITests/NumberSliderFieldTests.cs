using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.Styling;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using AvaPoint = Avalonia.Point;
using Pen = Beutl.Media.Pen;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NumberSliderFieldTests
{
    private static readonly object[] s_themes = [ThemeVariant.Light, ThemeVariant.Dark, FluentAvaloniaTheme.HighContrastTheme];

    [AvaloniaTest]
    public void In_the_panel_every_number_takes_a_drag_and_is_a_slider_where_it_has_a_range()
    {
        var grading = new ColorGrading();
        var scene = new Scene3D();
        var pen = new Pen();
        var shape = new RectShape();

        Assert.Multiple(() =>
        {
            Assert.That(HasRange(grading.Temperature, grading), Is.True, "-100 to 100 in whole numbers.");
            Assert.That(HasRange(grading.ContrastPivot, grading), Is.True, "0 to 1 in steps of its NumberStep.");
            Assert.That(HasRange(pen.Thickness, pen), Is.False, "A range with no real end.");
            Assert.That(HasRange(scene.RenderWidth, scene), Is.False, "Thousands of steps.");
            Assert.That(HasRange(shape.Width, shape), Is.False, "No range at all.");
            foreach (EditorSurface surface in new[] { EditorSurface.Node, EditorSurface.Settings, EditorSurface.ListItem })
                Assert.That(TakesADrag(grading.Temperature, grading, surface), Is.False, surface.ToString());
        });
    }

    [AvaloniaTest]
    public void A_number_with_no_range_takes_a_drag_and_a_click_like_any_other()
    {
        var shape = new RectShape();
        using var host = new FieldHost(shape, shape.Width);
        AvaPoint press = HeadlessTestHelpers.Center(host.Slider, host.Window);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.DragTo(press + new AvaPoint(4, 0));
        AvaPoint end = press + new AvaPoint(34, 0);
        host.DragTo(end);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(130f));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
        });

        // Away from the last press, so that it does not count as a double click.
        host.Click(press + new AvaPoint(8, 0));
        Assert.Multiple(() =>
        {
            Assert.That(host.Box.IsKeyboardFocusWithin, Is.True);
            Assert.That(host.Box.SelectedText, Is.EqualTo("130"));
        });
    }

    [AvaloniaTest]
    public void With_no_range_the_box_shows_neither_a_fill_nor_a_thumb()
    {
        var slider = new ValueSliderField
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            ShowsValue = false,
            Foreground = Brushes.White,
            ThumbBrush = Brushes.White,
            BorderBrush = null,
            Background = null,
            Opacity = 1,
        };
        var window = new Window { Content = slider, Width = 200, Height = 24, Background = Brushes.Black };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            (byte[] pixels, int rowBytes, _) = HeadlessTestHelpers.CaptureFramePixels(window);
            Assert.That(
                Enumerable.Range(0, 200).Select(x => pixels[(rowBytes * 12) + (x * 4) + 1]),
                Has.All.LessThan(40));
        }
        finally
        {
            window.Close();
        }
    }

    // Showing no range is for the box. A slider beside the box still has to reach the whole of the property.
    [AvaloniaTest]
    public void A_slider_beside_the_box_keeps_a_range_too_wide_for_a_box_to_show()
    {
        var scene = new Scene3D();
        using var model = new NumberEditorViewModel<float>(new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)scene.RenderWidth, scene));
        var editor = new NumberSliderEditor<float> { Theme = (ControlTheme)Application.Current!.FindResource("NumberSliderEditorTheme")! };

        model.Accept(editor);

        Assert.Multiple(() =>
        {
            Assert.That(editor.Minimum, Is.EqualTo(1));
            Assert.That(editor.Maximum, Is.EqualTo(8192));
            Assert.That(editor.ShowsRange, Is.False);
        });
    }

    [AvaloniaTest]
    public void A_click_edits_the_text_with_all_of_it_selected_and_leaves_the_value()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        AvaPoint away = host.PointAt(-60);

        host.Click(away);

        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(20f));
            Assert.That(host.History.UndoCount, Is.Zero);
            Assert.That(host.Box.IsKeyboardFocusWithin, Is.True);
            Assert.That(host.Box.SelectedText, Is.EqualTo("20"));
        });
        // While the text is edited, the pointer goes to the text box, to place the caret.
        TimelineTestHost.WaitForHitTest(host.Window, host.Box, away);
    }

    [AvaloniaTest]
    public void A_press_that_barely_moves_is_still_a_click()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        AvaPoint press = host.PointAt(-60);
        AvaPoint release = press + new AvaPoint(2, 1);

        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.Window.MouseMove(release, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(release, MouseButton.Left);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(20f));
            Assert.That(host.Box.IsKeyboardFocusWithin, Is.True);
        });
    }

    [AvaloniaTest]
    public void A_drag_moves_the_value_by_the_pointers_travel_in_one_undo_step_with_the_pointer_locked()
    {
        using var host = new FieldHost(effect => effect.Temperature);
        int scrubs = HeaderScrubGesture.ActiveCount;
        AvaPoint start = host.PointAt(0);
        host.Window.MouseMove(start);
        host.Window.MouseDown(start, MouseButton.Left);
        Assert.Multiple(() =>
        {
            Assert.That(host.Editor.Classes, Does.Not.Contain(":sliding"), "A press alone can still be a click.");
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs), "A click keeps the cursor.");
        });

        // The drag starts once the press has travelled, from there.
        host.DragTo(start + new AvaPoint(4, 0));
        Assert.Multiple(() =>
        {
            Assert.That(host.Editor.Classes, Does.Contain(":sliding"));
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs + 1), "The drag hides the cursor.");
            Assert.That(host.Property.Value, Is.Zero);
        });

        host.DragTo(start + new AvaPoint(34, 0));
        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(30f));
            Assert.That(host.Box.Text, Is.EqualTo("30"));
        });

        AvaPoint end = start + new AvaPoint(59, 0);
        host.DragTo(end);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(55f));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
            Assert.That(host.Editor.Classes, Does.Not.Contain(":sliding"));
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs), "The release shows the cursor again.");
            Assert.That(host.Box.IsKeyboardFocusWithin, Is.False);
        });

        Assert.That(host.History.Undo(), Is.True);
        Assert.That(host.Property.Value, Is.Zero);
    }

    [AvaloniaTest]
    public void A_drag_goes_on_from_the_value_wherever_in_the_box_it_starts()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        AvaPoint press = host.PointAt(70);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);

        host.DragTo(press + new AvaPoint(4, 0));
        Assert.That(host.Property.Value, Is.EqualTo(20f), "It does not jump to the value under the pointer.");

        AvaPoint end = press + new AvaPoint(14, 0);
        host.DragTo(end);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.That(host.Property.Value, Is.EqualTo(30f));
    }

    [AvaloniaTest]
    [TestCase(RawInputModifiers.None, 30, 30f)]
    [TestCase(RawInputModifiers.Shift, 3, 30f)]
    [TestCase(RawInputModifiers.Alt, 30, 3f)]
    public void A_drag_goes_at_the_speed_of_a_drag_of_the_header(RawInputModifiers modifier, int pixels, float expected)
    {
        using var host = new FieldHost(effect => effect.Temperature);
        AvaPoint press = host.PointAt(0);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        AvaPoint started = press + new AvaPoint(4, 0);
        host.DragTo(started);

        AvaPoint end = started + new AvaPoint(pixels, 0);
        host.DragTo(end, modifier);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();

        Assert.That(host.Property.Value, Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public void A_drag_stops_at_the_end_of_the_range_and_comes_back_at_once()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 95);
        AvaPoint press = host.PointAt(95);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        AvaPoint started = press + new AvaPoint(4, 0);
        host.DragTo(started);

        host.DragTo(started + new AvaPoint(40, 0));
        Assert.That(host.Property.Value, Is.EqualTo(100f));

        AvaPoint end = started + new AvaPoint(30, 0);
        host.DragTo(end);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.That(host.Property.Value, Is.EqualTo(90f));
    }

    // A drag hides the cursor until it ends, so that it has to end however the release is lost.
    [AvaloniaTest]
    [TestCase(false, Description = "The release happened where the slider never heard of it.")]
    [TestCase(true, Description = "The editor went away, and the slider's capture with it.")]
    public void A_drag_that_loses_its_release_shows_the_cursor_again_and_confirms_once(bool removeTheEditor)
    {
        using var host = new FieldHost(effect => effect.Temperature);
        int scrubs = HeaderScrubGesture.ActiveCount;
        AvaPoint press = host.PointAt(0);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.DragTo(press + new AvaPoint(4, 0));
        AvaPoint moved = press + new AvaPoint(24, 0);
        host.DragTo(moved);
        Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs + 1));

        if (removeTheEditor)
            ((Border)host.Window.Content!).Child = null;
        else
            host.Window.MouseMove(moved + new AvaPoint(10, 0));

        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs));
            Assert.That(host.Property.Value, Is.EqualTo(20f));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void Enter_confirms_the_typed_number_and_gives_the_box_back_to_the_slider()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        AvaPoint point = host.PointAt(-60);
        host.Click(point);

        host.Window.KeyTextInput("42");
        Assert.That(host.Property.Value, Is.EqualTo(42f), "The text replaced the selection and previews at once.");
        host.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(42f));
            Assert.That(host.History.UndoCount, Is.EqualTo(1));
            Assert.That(host.Box.IsKeyboardFocusWithin, Is.False);
            Assert.That(host.Slider.IsHitTestVisible, Is.True);
        });
        TimelineTestHost.WaitForHitTest(host.Window, host.Slider, point);
    }

    // What was typed goes in an undo step of its own, also in a window with no focusable parent to take the focus, as a
    // floated property tab is.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void A_drag_ends_the_typing_in_another_box(bool focusableParent)
    {
        bool pointerLock = GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value;
        GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = false;
        var effect = new ColorGrading();
        using var history = new HistoryScope(effect);
        (PropertyEditorViewModel firstModel, Control first) = CreateControl(
            new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)effect.Temperature, effect));
        (PropertyEditorViewModel secondModel, Control second) = CreateControl(
            new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)effect.Contrast, effect));
        firstModel.Accept(ServiceVisitor.For(history.History));
        secondModel.Accept(ServiceVisitor.For(history.History));
        var window = new Window
        {
            Content = new Border { Focusable = focusableParent, Child = new StackPanel { Children = { first, second } } },
            Width = 420,
            Height = 160,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ValueSliderField firstSlider = first.GetVisualDescendants().OfType<ValueSliderField>().Single();
            ValueSliderField secondSlider = second.GetVisualDescendants().OfType<ValueSliderField>().Single();
            TextBox firstBox = first.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_InnerTextBox");
            AvaPoint typeAt = HeadlessTestHelpers.Center(firstSlider, window);
            AvaPoint press = HeadlessTestHelpers.Center(secondSlider, window);
            TimelineTestHost.WaitForHitTest(window, firstSlider, typeAt);
            TimelineTestHost.WaitForHitTest(window, secondSlider, press);
            window.MouseMove(typeAt);
            window.MouseDown(typeAt, MouseButton.Left);
            window.MouseUp(typeAt, MouseButton.Left);
            HeadlessTestHelpers.Render();
            window.KeyTextInput("42");

            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            Assert.Multiple(() =>
            {
                Assert.That(firstBox.IsKeyboardFocusWithin, Is.False);
                Assert.That(history.History.UndoCount, Is.EqualTo(1), "The press confirms what was typed.");
            });

            window.MouseMove(press + new AvaPoint(4, 0), RawInputModifiers.LeftMouseButton);
            AvaPoint end = press + new AvaPoint(24, 0);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            HeadlessTestHelpers.Render();
            Assert.That(history.History.Undo(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(effect.Contrast.Value, Is.Zero, "The undo takes back the drag.");
                Assert.That(effect.Temperature.Value, Is.EqualTo(42f), "It leaves what was typed.");
            });
        }
        finally
        {
            window.Close();
            firstModel.Dispose();
            secondModel.Dispose();
            GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = pointerLock;
        }
    }

    [AvaloniaTest]
    public void A_right_click_opens_the_menu_of_the_text_box_under_the_slider()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        AvaPoint point = host.PointAt(-60);

        host.Window.MouseMove(point);
        host.Window.MouseDown(point, MouseButton.Right);
        host.Window.MouseUp(point, MouseButton.Right);
        HeadlessTestHelpers.Render();

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(host.Box.ContextFlyout!.IsOpen, Is.True);
                Assert.That(host.Box.SelectedText, Is.EqualTo("20"), "So that copy and cut have the number to work on.");
                Assert.That(host.Property.Value, Is.EqualTo(20f));
            });
        }
        finally
        {
            host.Box.ContextFlyout!.Hide();
            HeadlessTestHelpers.Render();
        }
    }

    // While its text does not parse, the box is not scrubbed, as its header is not: a drag leaves the value alone and
    // its release edits the text, as a click does, so it can be put right.
    [AvaloniaTest]
    public void A_box_whose_text_does_not_parse_takes_a_drag_as_a_click()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        host.Click(host.PointAt(-60));
        host.Window.KeyTextInput("abc");
        ((Border)host.Window.Content!).Focus();
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(DataValidationErrors.GetHasErrors(host.Box), Is.True, "The text that does not parse stays.");
            Assert.That(host.Slider.IsHitTestVisible, Is.True);
        });

        int scrubs = HeaderScrubGesture.ActiveCount;
        AvaPoint start = host.PointAt(0);
        host.Window.MouseMove(start);
        host.Window.MouseDown(start, MouseButton.Left);
        host.DragTo(start + new AvaPoint(34, 0));
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(20f));
            Assert.That(host.Editor.Classes, Does.Not.Contain(":sliding"));
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs), "The cursor stays.");
        });

        host.Window.MouseUp(start + new AvaPoint(34, 0), MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(20f));
            Assert.That(host.History.UndoCount, Is.Zero);
            Assert.That(host.Box.IsKeyboardFocusWithin, Is.True, "The release edits the text.");
            Assert.That(host.Box.SelectedText, Is.EqualTo("abc"));
        });
    }

    [AvaloniaTest]
    public void A_read_only_box_neither_slides_nor_edits()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        host.Editor.IsReadOnly = true;
        HeadlessTestHelpers.Render(2);
        AvaPoint press = host.PointAt(-60);
        AvaPoint moved = host.PointAt(60);

        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.Window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(moved, MouseButton.Left);
        host.Click(press + new AvaPoint(0, 2));

        Assert.Multiple(() =>
        {
            Assert.That(host.Property.Value, Is.EqualTo(20f));
            Assert.That(host.History.UndoCount, Is.Zero);
            Assert.That(host.Box.IsKeyboardFocusWithin, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task The_slider_shows_only_under_the_pointer_and_takes_the_whole_box()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20);
        Assert.That(host.Slider.Opacity, Is.Zero, "A row at rest looks like any other box.");

        Rect box = host.Slider.Bounds;
        foreach (AvaPoint local in new[]
                 {
                     new AvaPoint(2, box.Height / 2), new AvaPoint(box.Width - 2, box.Height / 2),
                     new AvaPoint(box.Width / 2, 2), new AvaPoint(box.Width / 2, box.Height - 2),
                 })
        {
            AvaPoint point = host.Slider.TranslatePoint(local, host.Window)!.Value;
            Assert.That(host.Window.InputHitTest(point), Is.SameAs(host.Slider), local.ToString());
        }

        host.Window.MouseMove(host.PointAt(-60));
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            return host.Slider.Opacity == 1;
        });

        host.Window.MouseMove(new AvaPoint(1, 1));
        await HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            return host.Slider.Opacity == 0;
        });
    }

    // A brush fading from Transparent, which is a white, went through colors brighter than it ended on, and the border
    // flashed as the pointer came.
    [AvaloniaTest]
    public async Task The_border_fades_in_without_going_past_the_color_it_ends_on()
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20, ThemeVariant.Dark);
        AvaPoint border = host.Box.TranslatePoint(new AvaPoint(host.Box.Bounds.Width * 0.75, 0.25), host.Window)!.Value;
        int Brightness()
        {
            (byte[] pixels, int rowBytes, _) = HeadlessTestHelpers.CaptureFramePixels(host.Window);
            return pixels[(rowBytes * (int)border.Y) + ((int)border.X * 4) + 1];
        }

        int atRest = Brightness();
        host.Window.MouseMove(host.PointAt(-60));
        var seen = new List<int>();
        for (int i = 0; i < 16; i++)
        {
            HeadlessTestHelpers.Render();
            seen.Add(Brightness());
            await Task.Delay(16);
        }

        Assert.Multiple(() =>
        {
            Assert.That(seen[^1], Is.GreaterThan(atRest), "The border lights up under the pointer.");
            Assert.That(seen, Has.All.LessThanOrEqualTo(seen[^1] + 2), string.Join(", ", seen));
        });
    }

    // The slider lies over the text, so a fill that hid what is under it would hide the number.
    [AvaloniaTest]
    [TestCaseSource(nameof(s_themes))]
    public async Task The_number_shows_through_the_slider_in_every_theme(ThemeVariant theme)
    {
        using var host = new FieldHost(effect => effect.Temperature, start: 20, theme);
        AvaPoint press = host.PointAt(20);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.Window.MouseMove(host.PointAt(60), RawInputModifiers.LeftMouseButton);
        await Task.Delay(250);
        HeadlessTestHelpers.Render(3);

        Assert.Multiple(() =>
        {
            Assert.That(Alpha(host.Slider.Background), Is.InRange(1, 64));
            Assert.That(Alpha(host.Slider.Foreground), Is.InRange(1, 64));
        });
        host.Window.MouseUp(host.PointAt(60), MouseButton.Left);
    }

    // Half the box is 100 px, so the value 50 of -100 to 100 ends a quarter of the way past the middle.
    [AvaloniaTest]
    [TestCase(-100, 100, 50, 130, 60, Description = "A range around zero fills from zero.")]
    [TestCase(0, 100, 50, 60, 170, Description = "Any other range fills from its minimum.")]
    [TestCase(-60, 0, -30, 40, 170, Description = "Zero at an end of the range is not a middle to fill from.")]
    public void The_fill_runs_from_zero_or_else_the_minimum(double minimum, double maximum, double value, int filled, int empty)
    {
        var slider = new ValueSliderField
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Foreground = Brushes.White,
            ThumbBrush = null,
            BorderBrush = null,
            Background = null,
            Opacity = 1,
        };
        var window = new Window { Content = slider, Width = 200, Height = 24, Background = Brushes.Black };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            (byte[] pixels, int rowBytes, _) = HeadlessTestHelpers.CaptureFramePixels(window);
            int Red(int x) => pixels[(rowBytes * 12) + (x * 4) + 1];
            Assert.Multiple(() =>
            {
                Assert.That(Red(filled), Is.GreaterThan(200));
                Assert.That(Red(empty), Is.LessThan(40));
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static bool HasRange(IExpressionProperty<float> property, EngineObject owner)
    {
        (PropertyEditorViewModel model, Control control) = CreateControl(
            new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)property, owner));
        using (model)
        {
            return ((NumberSliderEditor<float>)control).ShowsRange;
        }
    }

    private static bool TakesADrag(IExpressionProperty<float> property, EngineObject owner, EditorSurface surface)
    {
        (PropertyEditorViewModel model, Control control) = CreateControl(
            new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)property, owner), surface);
        using (model)
        {
            Assert.That(control, Is.InstanceOf<NumberEditor<float>>());
            return control is NumberSliderEditor<float>;
        }
    }

    private static byte Alpha(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color.A : (byte)0;

    // The editor the panel makes for one property of a ColorGrading, with a history. A focusable parent stands for the
    // editor's views, which take the focus when Enter leaves the box. The pointer lock is off, since a drag would
    // otherwise hide the real cursor and read the real mouse instead of the test's moves.
    private sealed class FieldHost : IDisposable
    {
        private readonly HistoryScope _history;
        private readonly PropertyEditorViewModel _model;
        private readonly bool _pointerLock = GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value;

        public FieldHost(Func<ColorGrading, IExpressionProperty<float>> pick, float? start = null, ThemeVariant? theme = null)
            : this(new ColorGrading(), pick, start, theme)
        {
        }

        private FieldHost(ColorGrading effect, Func<ColorGrading, IExpressionProperty<float>> pick, float? start, ThemeVariant? theme)
            : this(effect, pick(effect), start, theme)
        {
        }

        public FieldHost(EngineObject owner, IExpressionProperty<float> property, float? start = null, ThemeVariant? theme = null)
        {
            GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = false;
            Property = property;
            if (start is { } value)
                Property.Value = value;

            _history = new HistoryScope(owner);
            (_model, Control control) = CreateControl(new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)property, owner));
            _model.Accept(ServiceVisitor.For(_history.History));
            Editor = (NumberSliderEditor<float>)control;
            Window = new Window
            {
                Content = new Border { Focusable = true, Child = control },
                Width = 420,
                Height = 80,
                RequestedThemeVariant = theme,
            };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            Slider = Editor.GetVisualDescendants().OfType<ValueSliderField>().Single();
            Box = Editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "PART_InnerTextBox");
            TimelineTestHost.WaitForHitTest(Window, Slider, HeadlessTestHelpers.Center(Slider, Window));
        }

        public IProperty<float> Property { get; }

        public HistoryManager History => _history.History;

        public NumberSliderEditor<float> Editor { get; }

        public ValueSliderField Slider { get; }

        public TextBox Box { get; }

        public Window Window { get; }

        // Where in the window the box's thumb shows value.
        public AvaPoint PointAt(double value)
            => Slider.TranslatePoint(new AvaPoint(Slider.XOf(value), Slider.Bounds.Height / 2), Window)!.Value;

        // Moves the pointer with the left button held.
        public void DragTo(AvaPoint point, RawInputModifiers modifiers = RawInputModifiers.None)
            => Window.MouseMove(point, RawInputModifiers.LeftMouseButton | modifiers);

        public void Click(AvaPoint point)
        {
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }

        public void Dispose()
        {
            Window.Close();
            _model.Dispose();
            _history.Dispose();
            GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = _pointerLock;
        }
    }
}
