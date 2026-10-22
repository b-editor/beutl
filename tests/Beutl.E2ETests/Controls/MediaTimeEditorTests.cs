using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class MediaTimeEditorTests
{
    [AvaloniaTest]
    public void Typing_a_valid_time_parses_into_value()
    {
        var editor = new MediaTimeEditor { Header = "Duration" };
        using var host = new EditorTestHost<MediaTimeEditor>(editor);

        var changed = new List<MediaTime>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<MediaTime>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "00:01:30");

        Assert.That(editor.Value, Is.EqualTo(MediaTime.FromSeconds(90)));
        Assert.That(changed[^1], Is.EqualTo(MediaTime.FromSeconds(90)));
        Assert.That(DataValidationErrors.GetHasErrors(box), Is.False);
    }

    [AvaloniaTest]
    public void Focus_loss_after_a_valid_edit_confirms()
    {
        var editor = new MediaTimeEditor { Header = "Duration" };
        using var host = new EditorTestHost<MediaTimeEditor>(editor);

        var confirmed = new List<MediaTime>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<MediaTime>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "00:00:05");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { MediaTime.FromSeconds(5) }));
    }

    [AvaloniaTest]
    public void Invalid_text_raises_a_validation_error_and_keeps_value()
    {
        var editor = new MediaTimeEditor { Header = "Duration" };
        editor.Value = MediaTime.FromSeconds(42);
        using var host = new EditorTestHost<MediaTimeEditor>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "not-a-timespan");

        Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);
        Assert.That(editor.Value, Is.EqualTo(MediaTime.FromSeconds(42)), "Invalid text must not corrupt the value.");
    }

    [AvaloniaTest]
    public void Invalid_text_does_not_confirm_on_focus_loss()
    {
        var editor = new MediaTimeEditor { Header = "Duration" };
        editor.Value = MediaTime.FromSeconds(42);
        using var host = new EditorTestHost<MediaTimeEditor>(editor);

        bool confirmed = false;
        editor.ValueConfirmed += (_, _) => confirmed = true;

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "garbage");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.False);
    }
}
