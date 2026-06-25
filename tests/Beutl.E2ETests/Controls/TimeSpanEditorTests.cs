using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class TimeSpanEditorTests
{
    [AvaloniaTest]
    public void Typing_a_valid_timespan_parses_into_value()
    {
        var editor = new TimeSpanEditor { Header = "Duration" };
        var host = new EditorTestHost<TimeSpanEditor>(editor);

        var changed = new List<TimeSpan>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<TimeSpan>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "00:01:30");

        Assert.That(editor.Value, Is.EqualTo(TimeSpan.FromSeconds(90)));
        Assert.That(changed[^1], Is.EqualTo(TimeSpan.FromSeconds(90)));
        Assert.That(DataValidationErrors.GetHasErrors(box), Is.False);
    }

    [AvaloniaTest]
    public void Focus_loss_after_a_valid_edit_confirms()
    {
        var editor = new TimeSpanEditor { Header = "Duration" };
        var host = new EditorTestHost<TimeSpanEditor>(editor);

        var confirmed = new List<TimeSpan>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<TimeSpan>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "00:00:05");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { TimeSpan.FromSeconds(5) }));
    }

    [AvaloniaTest]
    public void Invalid_text_raises_a_validation_error_and_keeps_value()
    {
        var editor = new TimeSpanEditor { Header = "Duration" };
        editor.Value = TimeSpan.FromSeconds(42);
        var host = new EditorTestHost<TimeSpanEditor>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "not-a-timespan");

        Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);
        Assert.That(editor.Value, Is.EqualTo(TimeSpan.FromSeconds(42)), "Invalid text must not corrupt the value.");
    }

    [AvaloniaTest]
    public void Invalid_text_does_not_confirm_on_focus_loss()
    {
        var editor = new TimeSpanEditor { Header = "Duration" };
        editor.Value = TimeSpan.FromSeconds(42);
        var host = new EditorTestHost<TimeSpanEditor>(editor);

        bool confirmed = false;
        editor.ValueConfirmed += (_, _) => confirmed = true;

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "garbage");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.False);
    }
}
