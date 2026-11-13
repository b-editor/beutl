using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class StringEditorTests
{
    // One line with no line break, far longer than the 320-pixel host is wide.
    private const string LongLine =
        "A single line of text that runs well past the width of the property editor and keeps on going";

    [AvaloniaTest]
    [TestCase(PropertyEditorStyle.Normal)]
    [TestCase(PropertyEditorStyle.Compact)]
    [TestCase(PropertyEditorStyle.Settings)]
    public void A_multiline_editor_wraps_a_long_line_at_its_width(PropertyEditorStyle style)
    {
        var editor = new StringEditor { Header = "Text", EditorStyle = style, Text = LongLine, Classes = { "multiline" } };
        using var host = new EditorTestHost<StringEditor>(editor, width: 320, height: 400);
        if (style == PropertyEditorStyle.Settings)
        {
            // The settings layout keeps the text box in the item's expandable area.
            host.RequireDescendant<OptionsDisplayItem>().IsExpanded = true;
            HeadlessTestHelpers.Render(3);
        }

        Assert.That(PresenterOf(host).TextLayout.TextLines, Has.Count.GreaterThan(1),
            "a line longer than the editor should continue on the next line instead of running out of view");
    }

    [AvaloniaTest]
    public void A_single_line_editor_keeps_a_long_line_on_one_line()
    {
        var editor = new StringEditor { Header = "Name", Text = LongLine };
        using var host = new EditorTestHost<StringEditor>(editor, width: 320);

        Assert.That(PresenterOf(host).TextLayout.TextLines, Has.Count.EqualTo(1));
    }

    private static TextPresenter PresenterOf(EditorTestHost<StringEditor> host)
    {
        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        TextPresenter? presenter = box.FindDescendantOfType<TextPresenter>();
        Assert.That(presenter, Is.Not.Null, "The text box has no text presenter.");
        return presenter!;
    }

    [AvaloniaTest]
    public void Typing_updates_text_and_raises_value_changed_while_focused()
    {
        var editor = new StringEditor { Header = "Name" };
        using var host = new EditorTestHost<StringEditor>(editor);

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
        using var host = new EditorTestHost<StringEditor>(editor);

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
        using var host = new EditorTestHost<StringEditor>(editor);

        bool confirmed = false;
        editor.ValueConfirmed += (_, _) => confirmed = true;

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        box.Focus();
        HeadlessTestHelpers.Settle();
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.False);
    }
}
