using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class RelativeRectEditorTests
{
    [AvaloniaTest]
    public void Editing_all_four_components_updates_the_values()
    {
        var editor = new RelativeRectEditor { Header = "Rect", Unit = RelativeUnit.Absolute };
        using var host = new EditorTestHost<RelativeRectEditor>(editor);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), "1");
        host.TypeInto(host.Require<TextBox>("PART_InnerSecondTextBox"), "2");
        host.TypeInto(host.Require<TextBox>("PART_InnerThirdTextBox"), "3");
        host.TypeInto(host.Require<TextBox>("PART_InnerFourthTextBox"), "4");

        Assert.That(editor.FirstValue, Is.EqualTo(1f));
        Assert.That(editor.SecondValue, Is.EqualTo(2f));
        Assert.That(editor.ThirdValue, Is.EqualTo(3f));
        Assert.That(editor.FourthValue, Is.EqualTo(4f));
    }

    [AvaloniaTest]
    public void Focus_loss_after_an_edit_confirms_a_composed_relative_rect()
    {
        var editor = new RelativeRectEditor { Header = "Rect", Unit = RelativeUnit.Absolute };
        using var host = new EditorTestHost<RelativeRectEditor>(editor);

        var confirmed = new List<RelativeRect>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<RelativeRect>)e).NewValue);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), "5");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.Not.Empty);
        Assert.That(confirmed[^1].Rect.X, Is.EqualTo(5f));
    }
}
