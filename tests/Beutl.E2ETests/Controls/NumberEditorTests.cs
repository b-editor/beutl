using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

public class NumberEditorTests
{
    [AvaloniaTest]
    public void Typing_an_integer_updates_value()
    {
        var editor = new NumberEditor<int> { Header = "Count" };
        var host = new EditorTestHost<NumberEditor<int>>(editor);

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
        var host = new EditorTestHost<NumberEditor<float>>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "2.5");

        Assert.That(editor.Value, Is.EqualTo(2.5f));
    }

    [AvaloniaTest]
    public void Wheel_up_adds_large_change_and_wheel_down_subtracts_it()
    {
        var editor = new NumberEditor<int> { Header = "Count", LargeChange = 10 };
        var host = new EditorTestHost<NumberEditor<int>>(editor);

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
        var host = new EditorTestHost<NumberEditor<double>>(editor);

        var confirmed = new List<double>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<double>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "7.25");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { 7.25 }));
    }

    [AvaloniaTest]
    public void Non_numeric_text_raises_a_validation_error_and_keeps_value()
    {
        var editor = new NumberEditor<int> { Header = "Count" };
        editor.Value = 99;
        var host = new EditorTestHost<NumberEditor<int>>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "abc");

        Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);
        Assert.That(editor.Value, Is.EqualTo(99));
    }
}
