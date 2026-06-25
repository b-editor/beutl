using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class RationalEditorTests
{
    [AvaloniaTest]
    public void Typing_a_fraction_parses_into_value()
    {
        var editor = new RationalEditor { Header = "Rate" };
        var host = new EditorTestHost<RationalEditor>(editor);

        var changed = new List<Rational>();
        editor.ValueChanged += (_, e) => changed.Add(((PropertyEditorValueChangedEventArgs<Rational>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "30/1");

        Assert.That(editor.Value, Is.EqualTo(new Rational(30, 1)));
        Assert.That(changed[^1], Is.EqualTo(new Rational(30, 1)));
    }

    [AvaloniaTest]
    public void Focus_loss_after_an_edit_confirms()
    {
        var editor = new RationalEditor { Header = "Rate" };
        var host = new EditorTestHost<RationalEditor>(editor);

        var confirmed = new List<Rational>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<Rational>)e).NewValue);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "24/1");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.EqualTo(new[] { new Rational(24, 1) }));
    }

    [AvaloniaTest]
    public void Invalid_text_raises_a_validation_error_and_keeps_value()
    {
        var editor = new RationalEditor { Header = "Rate" };
        editor.Value = new Rational(60, 1);
        var host = new EditorTestHost<RationalEditor>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "garbage");

        Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);
        Assert.That(editor.Value, Is.EqualTo(new Rational(60, 1)));
    }
}
