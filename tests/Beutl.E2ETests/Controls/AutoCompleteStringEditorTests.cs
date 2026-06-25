using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class AutoCompleteStringEditorTests
{
    [AvaloniaTest]
    public void Typing_into_the_autocomplete_box_updates_text_and_raises_value_changed()
    {
        var editor = new AutoCompleteStringEditor
        {
            Header = "Tag",
            ItemsSource = new[] { "alpha", "beta", "gamma" },
        };
        var host = new EditorTestHost<AutoCompleteStringEditor>(editor);

        var changed = new List<string>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<string>)e).NewValue);

        AutoCompleteBox box = host.Require<AutoCompleteBox>("PART_InnerAutoCompleteBox");
        TextBox inner = host.RequireDescendant<TextBox>();
        host.TypeInto(inner, "al");

        // The AutoCompleteBox completes "al" to the matching item "alpha"; the editor mirrors that text.
        Assert.That(editor.Text, Is.EqualTo("alpha"));
        Assert.That(editor.Text, Is.EqualTo(box.Text));
        Assert.That(changed, Is.Not.Empty);
        Assert.That(changed[^1], Is.EqualTo("alpha"));
    }

    [AvaloniaTest]
    public void Focus_loss_after_an_edit_confirms()
    {
        var editor = new AutoCompleteStringEditor
        {
            Header = "Tag",
            ItemsSource = new[] { "alpha", "beta" },
        };
        var host = new EditorTestHost<AutoCompleteStringEditor>(editor);

        var confirmed = new List<string>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<string>)e).NewValue);

        TextBox inner = host.RequireDescendant<TextBox>();
        host.TypeInto(inner, "be");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { "beta" }));
    }
}
