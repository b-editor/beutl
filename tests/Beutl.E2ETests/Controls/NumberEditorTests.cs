using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class NumberEditorTests
{
    [AvaloniaTest]
    public void Typing_an_integer_updates_value()
    {
        var editor = new NumberEditor<int> { Header = "Count" };
        using var host = new EditorTestHost<NumberEditor<int>>(editor);

        var changed = new List<int>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<int>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "123");

        Assert.That(editor.Value, Is.EqualTo(123));
        Assert.That(changed[^1], Is.EqualTo(123));
    }

    [AvaloniaTest]
    public void Typing_a_float_updates_value()
    {
        var editor = new NumberEditor<float> { Header = "Scale" };
        using var host = new EditorTestHost<NumberEditor<float>>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, 2.5f.ToString(CultureInfo.CurrentCulture));

        Assert.That(editor.Value, Is.EqualTo(2.5f));
    }

    [AvaloniaTest]
    public void Wheel_up_adds_large_change_and_wheel_down_subtracts_it()
    {
        var editor = new NumberEditor<int> { Header = "Count", LargeChange = 10 };
        using var host = new EditorTestHost<NumberEditor<int>>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "5");

        host.WheelOver(box, deltaY: 1);
        Assert.That(editor.Value, Is.EqualTo(15));

        host.WheelOver(box, deltaY: -1);
        Assert.That(editor.Value, Is.EqualTo(5));
    }

    [AvaloniaTest]
    public void Focus_loss_after_an_edit_confirms_with_the_new_value()
    {
        var editor = new NumberEditor<double> { Header = "Value" };
        using var host = new EditorTestHost<NumberEditor<double>>(editor);

        var confirmed = new List<double>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<double>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, 7.25d.ToString(CultureInfo.CurrentCulture));
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { 7.25 }));
    }

    [AvaloniaTest]
    public void Non_numeric_text_raises_a_validation_error_and_keeps_value()
    {
        var editor = new NumberEditor<int> { Header = "Count" };
        editor.Value = 99;
        using var host = new EditorTestHost<NumberEditor<int>>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "abc");

        Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);
        Assert.That(editor.Value, Is.EqualTo(99));
    }

    [AvaloniaTest]
    public void Typing_below_the_minimum_preserves_text_until_the_number_is_complete()
    {
        var editor = new NumberEditor<int> { Header = "Range: 50–200", Value = 100 };
        using var host = new EditorTestHost<NumberEditor<int>>(editor);
        // ValueEditorViewModel writes the property's coerced value back during ValueChanged.
        editor.ValueChanged += (_, _) => editor.Value = Math.Clamp(editor.Value, 50, 200);
        var confirmed = new List<(int OldValue, int NewValue)>();
        editor.ValueConfirmed += (_, e) =>
        {
            var args = (PropertyEditorValueChangedEventArgs<int>)e;
            confirmed.Add((args.OldValue, args.NewValue));
        };

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "7");

        Assert.That(box.Text, Is.EqualTo("7"));
        Assert.That(box.CaretIndex, Is.EqualTo(1));
        Assert.That(editor.Value, Is.EqualTo(50));
        Capture(host.Window, "typing-7");

        host.Window.KeyTextInput("0");
        HeadlessTestHelpers.Settle();

        Assert.That(box.Text, Is.EqualTo("70"));
        Assert.That(editor.Value, Is.EqualTo(70));
        Assert.That(confirmed, Is.Empty);
        Capture(host.Window, "typing-70");

        host.MoveFocusToSink();

        Assert.That(box.Text, Is.EqualTo("70"));
        Assert.That(confirmed, Is.EqualTo(new[] { (100, 70) }));
        Assert.That(DataValidationErrors.GetHasErrors(box), Is.False);
        Capture(host.Window, "confirmed-70");
    }

    [AvaloniaTest]
    [TestCase(100, "7", 50)]
    [TestCase(50, "7", 50)]
    [TestCase(100, "300", 200)]
    [TestCase(200, "300", 200)]
    public void Focus_loss_displays_the_coerced_value_even_when_it_has_not_changed(
        int initialValue, string text, int expectedValue)
    {
        var editor = new NumberEditor<int> { Header = "Range: 50–200", Value = initialValue };
        using var host = new EditorTestHost<NumberEditor<int>>(editor);
        editor.ValueChanged += (_, _) => editor.Value = Math.Clamp(editor.Value, 50, 200);
        var confirmed = new List<int>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<int>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, text);

        Assert.That(box.Text, Is.EqualTo(text));
        Assert.That(editor.Value, Is.EqualTo(expectedValue));

        host.MoveFocusToSink();

        Assert.That(box.Text, Is.EqualTo(expectedValue.ToString(CultureInfo.CurrentCulture)));
        Assert.That(confirmed, Is.EqualTo(initialValue == expectedValue ? Array.Empty<int>() : new[] { expectedValue }));
    }

    [AvaloniaTest]
    [TestCase(50, -1, 50)]
    [TestCase(195, 1, 200)]
    public void Wheel_changes_display_the_coerced_value_immediately(int initialValue, int delta, int expectedValue)
    {
        var editor = new NumberEditor<int> { Header = "Range: 50–200", Value = initialValue };
        using var host = new EditorTestHost<NumberEditor<int>>(editor);
        editor.ValueChanged += (_, _) => editor.Value = Math.Clamp(editor.Value, 50, 200);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.WheelOver(box, delta);

        Assert.That(editor.Value, Is.EqualTo(expectedValue));
        Assert.That(box.Text, Is.EqualTo(expectedValue.ToString(CultureInfo.CurrentCulture)));
    }

    [AvaloniaTest]
    public void External_value_changes_still_update_the_focused_text_box()
    {
        var editor = new NumberEditor<int> { Header = "Count", Value = 100 };
        using var host = new EditorTestHost<NumberEditor<int>>(editor);
        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "70");

        editor.Value = 120;
        HeadlessTestHelpers.Settle();

        Assert.That(box.Text, Is.EqualTo("120"));
        Assert.That(editor.Value, Is.EqualTo(120));
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_NUMBER_EDITOR_CAPTURE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);
            frame!.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
