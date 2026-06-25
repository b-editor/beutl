using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class StringEditorTests
{
    [AvaloniaTest]
    public void Typing_updates_text_and_raises_value_changed_while_focused()
    {
        var editor = new StringEditor { Header = "Name" };
        var host = new EditorTestHost<StringEditor>(editor);

        var changed = new List<string>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<string>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "hello");

        Assert.That(editor.Text, Is.EqualTo("hello"));
        Assert.That(changed, Is.Not.Empty);
        Assert.That(changed[^1], Is.EqualTo("hello"));
    }

    [AvaloniaTest]
    public void Losing_focus_after_an_edit_raises_value_confirmed()
    {
        var editor = new StringEditor { Header = "Name" };
        var host = new EditorTestHost<StringEditor>(editor);

        var confirmed = new List<string>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<string>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "world");
        Assert.That(confirmed, Is.Empty, "Confirm should only fire on focus loss.");

        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { "world" }));
    }

    [AvaloniaTest]
    public void Focus_loss_without_a_change_does_not_confirm()
    {
        var editor = new StringEditor { Header = "Name", Text = "initial" };
        var host = new EditorTestHost<StringEditor>(editor);

        bool confirmed = false;
        editor.ValueConfirmed += (_, _) => confirmed = true;

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        box.Focus();
        HeadlessTestHelpers.Settle();
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.False);
    }
}
