using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class VectorEditorTests
{
    [AvaloniaTest]
    public void Vector2_editing_one_component_updates_only_that_component()
    {
        var editor = new Vector2Editor<int> { Header = "Size" };
        var host = new EditorTestHost<Vector2Editor<int>>(editor);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        TextBox second = host.Require<TextBox>("PART_InnerSecondTextBox");

        host.TypeInto(first, "12");
        host.TypeInto(second, "34");

        Assert.That(editor.FirstValue, Is.EqualTo(12));
        Assert.That(editor.SecondValue, Is.EqualTo(34));
    }

    [AvaloniaTest]
    public void Vector2_focus_loss_confirms_the_composed_pair()
    {
        var editor = new Vector2Editor<int> { Header = "Size" };
        var host = new EditorTestHost<Vector2Editor<int>>(editor);

        var confirmed = new List<(int, int)>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<(int, int)>)e).NewValue);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        host.TypeInto(first, "7");
        host.MoveFocusToSink();

        Assert.That(confirmed, Is.Not.Empty);
        Assert.That(confirmed[^1].Item1, Is.EqualTo(7));
    }

    [AvaloniaTest]
    public void Vector2_wheel_changes_the_hovered_component()
    {
        var editor = new Vector2Editor<int> { Header = "Size", LargeChange = 10 };
        var host = new EditorTestHost<Vector2Editor<int>>(editor);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        host.TypeInto(first, "2");
        host.WheelOver(first, deltaY: 1);

        Assert.That(editor.FirstValue, Is.EqualTo(12));
        Assert.That(editor.SecondValue, Is.EqualTo(0));
    }

    [AvaloniaTest]
    public void Vector3_editing_all_three_components_composes_the_value()
    {
        var editor = new Vector3Editor<float> { Header = "XYZ" };
        var host = new EditorTestHost<Vector3Editor<float>>(editor);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), "1.5");
        host.TypeInto(host.Require<TextBox>("PART_InnerSecondTextBox"), "2.5");
        host.TypeInto(host.Require<TextBox>("PART_InnerThirdTextBox"), "3.5");

        Assert.That(editor.FirstValue, Is.EqualTo(1.5f));
        Assert.That(editor.SecondValue, Is.EqualTo(2.5f));
        Assert.That(editor.ThirdValue, Is.EqualTo(3.5f));
    }

    [AvaloniaTest]
    public void Vector4_editing_all_four_components_composes_the_value()
    {
        var editor = new Vector4Editor<float> { Header = "XYZW" };
        var host = new EditorTestHost<Vector4Editor<float>>(editor);

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
    public void RelativePoint_editing_components_confirms_a_composed_relative_point()
    {
        var editor = new RelativePointEditor { Header = "Origin", Unit = RelativeUnit.Absolute };
        var host = new EditorTestHost<RelativePointEditor>(editor);

        var confirmed = new List<RelativePoint>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<RelativePoint>)e).NewValue);

        host.TypeInto(host.Require<TextBox>("PART_InnerFirstTextBox"), "10");
        host.TypeInto(host.Require<TextBox>("PART_InnerSecondTextBox"), "20");
        host.MoveFocusToSink();

        Assert.That(editor.FirstValue, Is.EqualTo(10f));
        Assert.That(editor.SecondValue, Is.EqualTo(20f));
        Assert.That(confirmed, Is.Not.Empty);
        Assert.That(confirmed[^1], Is.EqualTo(new RelativePoint(10, 20, RelativeUnit.Absolute)));
    }
}
