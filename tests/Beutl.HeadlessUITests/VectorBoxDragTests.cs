using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;
using AvaPoint = Avalonia.Point;
using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class VectorBoxDragTests
{
    [AvaloniaTest]
    public void A_drag_on_a_box_moves_that_component_alone_with_the_pointer_locked()
    {
        var editor = new Vector2Editor<float> { Header = "H", FirstValue = 10, SecondValue = 20 };
        using var host = new BoxHost(editor);
        int scrubs = HeaderScrubGesture.ActiveCount;
        TextBox second = host.Box("Second");
        AvaPoint press = host.Over(second);

        host.Press(press);
        Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs), "A press alone can still be a click, which keeps the cursor.");

        AvaPoint started = press + new AvaPoint(4, 0);
        host.DragTo(started);
        Assert.Multiple(() =>
        {
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs + 1), "The drag hides the cursor.");
            Assert.That(editor.SecondValue, Is.EqualTo(20f));
        });

        AvaPoint end = started + new AvaPoint(30, 0);
        host.DragTo(end);
        Assert.Multiple(() =>
        {
            Assert.That(editor.SecondValue, Is.EqualTo(50f));
            Assert.That(editor.FirstValue, Is.EqualTo(10f));
            Assert.That(editor.Classes, Does.Contain(":border-pointerover"), "The border stays lit while the box is held.");
        });

        host.Release(end);
        Assert.Multiple(() =>
        {
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs), "The release shows the cursor again.");
            Assert.That(host.Confirmed, Is.EqualTo(1));
            Assert.That(second.IsKeyboardFocusWithin, Is.False);
        });
    }

    [AvaloniaTest]
    public void A_click_on_a_box_edits_its_text_with_all_of_it_selected()
    {
        var editor = new Vector2Editor<float> { Header = "H", FirstValue = 10, SecondValue = 20 };
        using var host = new BoxHost(editor);
        TextBox first = host.Box("First");
        Assert.That(first.Cursor?.ToString(), Is.EqualTo("SizeWestEast"));

        host.Click(host.Over(first));

        Assert.Multiple(() =>
        {
            Assert.That(first.IsKeyboardFocusWithin, Is.True);
            Assert.That(first.SelectedText, Is.EqualTo("10"));
            Assert.That(editor.FirstValue, Is.EqualTo(10f));
            Assert.That(host.Confirmed, Is.Zero);
            Assert.That(first.Cursor?.ToString(), Is.EqualTo("Ibeam"));
        });
    }

    [AvaloniaTest]
    public void A_drag_in_a_box_being_edited_stays_with_the_text()
    {
        var editor = new Vector2Editor<float> { Header = "H", FirstValue = 10, SecondValue = 20 };
        using var host = new BoxHost(editor);
        TextBox first = host.Box("First");
        AvaPoint point = host.Over(first);
        host.Click(point);
        int scrubs = HeaderScrubGesture.ActiveCount;

        // Away from the last press, so that it does not count as a double click.
        AvaPoint press = point + new AvaPoint(6, 0);
        AvaPoint end = press + new AvaPoint(20, 0);
        host.Press(press);
        host.DragTo(end);
        Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs));
        host.Release(end);

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(10f));
            Assert.That(first.IsKeyboardFocusWithin, Is.True);
        });
    }

    [AvaloniaTest]
    public void Enter_confirms_the_typed_number_and_leaves_the_box()
    {
        var editor = new Vector2Editor<float> { Header = "H", FirstValue = 10, SecondValue = 20 };
        using var host = new BoxHost(editor);
        TextBox first = host.Box("First");
        host.Click(host.Over(first));

        host.Window.KeyTextInput("42");
        host.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(42f));
            Assert.That(first.IsKeyboardFocusWithin, Is.False);
            Assert.That(host.Confirmed, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void The_one_box_of_a_uniform_vector_moves_every_component()
    {
        var editor = new Vector2Editor<float> { Header = "H", IsUniform = true, FirstValue = 10, SecondValue = 10 };
        using var host = new BoxHost(editor);

        host.Drag(host.Box("First"), 30);

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(40f));
            Assert.That(editor.SecondValue, Is.EqualTo(40f));
        });
    }

    [AvaloniaTest]
    public void A_box_of_a_three_part_vector_moves_its_component()
    {
        var editor = new Vector3Editor<float> { Header = "H", FirstValue = 1, SecondValue = 2, ThirdValue = 3 };
        using var host = new BoxHost(editor);

        host.Drag(host.Box("Third"), 30);

        Assert.Multiple(() =>
        {
            Assert.That(editor.ThirdValue, Is.EqualTo(33f));
            Assert.That(editor.FirstValue, Is.EqualTo(1f));
            Assert.That(editor.SecondValue, Is.EqualTo(2f));
        });
    }

    // Their components have no headers of their own, so that a box is the only way to drag one alone.
    [AvaloniaTest]
    [TestCase(null)]
    [TestCase("ThicknessEditorStyle")]
    [TestCase("CornerRadiusEditorStyle")]
    public void A_box_of_a_four_part_vector_moves_its_component(string? theme)
    {
        var editor = new Vector4Editor<float> { Header = "H", FirstValue = 1, SecondValue = 2, ThirdValue = 3, FourthValue = 4 };
        if (theme != null)
            editor.Theme = (Avalonia.Styling.ControlTheme)Application.Current!.FindResource(theme)!;

        using var host = new BoxHost(editor);

        host.Drag(host.Box("Third"), 30);

        Assert.Multiple(() =>
        {
            Assert.That(editor.ThirdValue, Is.EqualTo(33f));
            Assert.That(editor.FirstValue, Is.EqualTo(1f));
            Assert.That(editor.SecondValue, Is.EqualTo(2f));
            Assert.That(editor.FourthValue, Is.EqualTo(4f));
        });
    }

    [AvaloniaTest]
    [TestCase(RelativeUnit.Relative, 0.5f, 0.8f)]
    [TestCase(RelativeUnit.Absolute, 10f, 40f)]
    public void A_relative_point_moves_by_a_percent_or_a_unit_per_pixel(RelativeUnit unit, float start, float expected)
    {
        var editor = new RelativePointEditor { Header = "P", Unit = unit, FirstValue = start, SecondValue = start };
        using var host = new BoxHost(editor);

        host.Drag(host.Box("First"), 30);

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(expected).Within(1e-4));
            Assert.That(editor.SecondValue, Is.EqualTo(start));
            Assert.That(editor.Unit, Is.EqualTo(unit));
            Assert.That(host.Confirmed, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void A_relative_value_gives_a_pulse_when_it_crosses_zero()
    {
        var editor = new RelativePointEditor { Header = "P", Unit = RelativeUnit.Absolute, FirstValue = -10, SecondValue = 5 };
        using var host = new BoxHost(editor);
        using var feedback = new HapticFeedbackRecorder();

        host.Drag(host.Box("First"), 20);

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(10f));
            Assert.That(feedback.Pulses, Has.Count.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void The_header_of_a_relative_component_drags_it_too()
    {
        var editor = new RelativePointEditor { Header = "P", Unit = RelativeUnit.Relative, FirstValue = 0.5f, SecondValue = 0.5f };
        using var host = new BoxHost(editor);
        TextBlock header = editor.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_HeaderFirstTextBlock");
        AvaPoint press = HeadlessTestHelpers.Center(header, host.Window);
        TimelineTestHost.WaitForHitTest(host.Window, header, press);

        host.Press(press);
        AvaPoint end = press + new AvaPoint(30, 0);
        host.DragTo(end);
        host.Release(end);

        Assert.That(editor.FirstValue, Is.EqualTo(0.8f).Within(1e-4));
    }

    // Also in a window with no focusable parent to take the focus, as a floated property tab is.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void A_drag_ends_the_editing_of_the_box_beside_it(bool focusableParent)
    {
        var editor = new Vector2Editor<float> { Header = "H", FirstValue = 10, SecondValue = 20 };
        using var host = new BoxHost(editor, focusableParent);
        TextBox first = host.Box("First");
        host.Click(host.Over(first));
        Assert.That(first.IsKeyboardFocusWithin, Is.True);

        host.Drag(host.Box("Second"), 30);

        Assert.Multiple(() =>
        {
            Assert.That(first.IsKeyboardFocusWithin, Is.False);
            Assert.That(editor.SecondValue, Is.EqualTo(50f));
        });
    }

    // The border is held lit during a press, whatever the pointer is over. Over the box it stays lit, and it goes out when
    // the release comes away from the box, which only a pointer that is not locked can do.
    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public void The_border_stays_lit_after_a_drag_only_if_the_pointer_is_still_over_the_box(bool overTheBox)
    {
        var editor = new Vector2Editor<float> { Header = "H" };
        using var host = new BoxHost(editor);
        AvaPoint press = host.Over(host.Box("First"));
        AvaPoint end = overTheBox ? press + new AvaPoint(10, 0) : new AvaPoint(1, 1);
        host.Press(press);
        host.DragTo(press + new AvaPoint(4, 0));
        host.DragTo(end);
        Assert.That(editor.Classes, Does.Contain(":border-pointerover"));

        host.Release(end);

        Assert.That(editor.Classes.Contains(":border-pointerover"), Is.EqualTo(overTheBox));
    }

    [AvaloniaTest]
    public void A_drag_that_loses_its_release_shows_the_cursor_again_and_confirms_once()
    {
        var editor = new Vector2Editor<float> { Header = "H" };
        using var host = new BoxHost(editor);
        int scrubs = HeaderScrubGesture.ActiveCount;
        AvaPoint press = host.Over(host.Box("First"));
        host.Press(press);
        host.DragTo(press + new AvaPoint(4, 0));
        AvaPoint moved = press + new AvaPoint(24, 0);
        host.DragTo(moved);
        Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs + 1));

        // The release happened where the box never heard of it.
        host.Window.MouseMove(moved + new AvaPoint(10, 0));
        HeadlessTestHelpers.Render();

        Assert.Multiple(() =>
        {
            Assert.That(HeaderScrubGesture.ActiveCount, Is.EqualTo(scrubs));
            Assert.That(editor.FirstValue, Is.EqualTo(20f));
            Assert.That(host.Confirmed, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void A_read_only_box_neither_drags_nor_edits()
    {
        var editor = new Vector2Editor<float> { Header = "H", FirstValue = 10, SecondValue = 20, IsReadOnly = true };
        using var host = new BoxHost(editor);
        TextBox first = host.Box("First");
        AvaPoint press = HeadlessTestHelpers.Center(first, host.Window);
        AvaPoint end = press + new AvaPoint(30, 0);

        host.Press(press);
        host.DragTo(end);
        host.Release(end);

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(10f));
            Assert.That(first.IsKeyboardFocusWithin, Is.False);
        });
    }

    [AvaloniaTest]
    [TestCase(PropertyEditorStyle.ListItem)]
    [TestCase(PropertyEditorStyle.Compact)]
    public void Boxes_outside_the_panel_stay_plain_text_boxes(PropertyEditorStyle style)
    {
        var editor = new Vector2Editor<float> { Header = "H", EditorStyle = style, FirstValue = 10, SecondValue = 20 };
        using var host = new BoxHost(editor);
        TextBox first = host.Box("First");

        Assert.That(first.Classes, Does.Not.Contain("scrub-box"));
        host.Drag(first, 30);

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(10f));
            Assert.That(first.IsKeyboardFocusWithin, Is.True, "The press went to the text box.");
        });
    }

    // A vector editor alone in a window. A focusable parent stands for the editor's views, which take the focus when a
    // press leaves a box being edited. The pointer lock is off, since a drag would otherwise hide the real cursor and read
    // the real mouse instead of the test's moves.
    private sealed class BoxHost : IDisposable
    {
        private readonly bool _pointerLock = GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value;

        public BoxHost(VectorEditorBase editor, bool focusableParent = true)
        {
            GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = false;
            Editor = editor;
            editor.ValueConfirmed += (_, _) => Confirmed++;
            Window = new Window { Content = new Border { Focusable = focusableParent, Child = editor }, Width = 520, Height = 120 };
            Window.Show();
            HeadlessTestHelpers.Render(3);
        }

        public VectorEditorBase Editor { get; }

        public Window Window { get; }

        public int Confirmed { get; private set; }

        public TextBox Box(string part)
            => Editor.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == $"PART_Inner{part}TextBox");

        public AvaPoint Over(TextBox box)
        {
            AvaPoint center = HeadlessTestHelpers.Center(box, Window);
            TimelineTestHost.WaitForHitTest(Window, box, center);
            return center;
        }

        public void Press(AvaPoint point)
        {
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
        }

        public void DragTo(AvaPoint point) => Window.MouseMove(point, RawInputModifiers.LeftMouseButton);

        public void Release(AvaPoint point)
        {
            Window.MouseUp(point, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }

        public void Click(AvaPoint point)
        {
            Press(point);
            Release(point);
        }

        // Starts a drag on the box, which the first few pixels do, and moves on by pixels.
        public void Drag(TextBox box, double pixels)
        {
            AvaPoint started = Over(box) + new AvaPoint(4, 0);
            Press(started - new AvaPoint(4, 0));
            DragTo(started);
            AvaPoint end = started + new AvaPoint(pixels, 0);
            DragTo(end);
            Release(end);
        }

        public void Dispose()
        {
            Window.Close();
            GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = _pointerLock;
        }
    }
}
