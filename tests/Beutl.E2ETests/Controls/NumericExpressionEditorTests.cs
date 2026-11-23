using System.Globalization;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;

using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class NumericExpressionEditorTests
{
    [AvaloniaTest]
    public void NumberEditor_evaluates_expression_while_typing_and_formats_on_focus_loss()
    {
        var editor = new NumberEditor<int> { Header = "Width", Value = 1920 };
        using var host = new EditorTestHost<NumberEditor<int>>(editor);

        var confirmed = new List<(int Old, int New)>();
        editor.ValueConfirmed += (_, e) =>
        {
            var args = (PropertyEditorValueChangedEventArgs<int>)e;
            confirmed.Add((args.OldValue, args.NewValue));
        };

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "1920/");
        Assert.Multiple(() =>
        {
            Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);
            Assert.That(editor.Value, Is.EqualTo(1920));
        });

        host.Window.KeyTextInput("2");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(DataValidationErrors.GetHasErrors(box), Is.False);
            Assert.That(box.Text, Is.EqualTo("1920/2"));
            Assert.That(editor.Value, Is.EqualTo(960));
            Assert.That(confirmed, Is.Empty);
        });

        host.MoveFocusToSink();
        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("960"));
            Assert.That(confirmed, Is.EqualTo(new[] { (1920, 960) }));
        });
    }

    [AvaloniaTest]
    public void NumberSliderEditor_evaluates_expression_and_updates_value()
    {
        var editor = new NumberSliderEditor<float> { Header = "Scale", Minimum = 0, Maximum = 500, Value = 100 };
        using var host = new EditorTestHost<NumberSliderEditor<float>>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "(100+50)*2");
        Assert.Multiple(() =>
        {
            Assert.That(DataValidationErrors.GetHasErrors(box), Is.False);
            Assert.That(editor.Value, Is.EqualTo(300f));
        });

        host.MoveFocusToSink();
        Assert.That(box.Text, Is.EqualTo("300"));
    }

    [AvaloniaTest]
    public void Division_by_zero_sets_validation_error_and_preserves_value()
    {
        var editor = new NumberEditor<float> { Header = "Value", Value = 42 };
        using var host = new EditorTestHost<NumberEditor<float>>(editor);

        TextBox box = host.Require<TextBox>("PART_InnerTextBox");
        host.TypeInto(box, "42/0");

        Assert.Multiple(() =>
        {
            Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);
            Assert.That(editor.Value, Is.EqualTo(42f));
        });
    }

    [AvaloniaTest]
    public void Vector2Editor_evaluates_expressions_in_each_component_and_formats_on_focus_loss()
    {
        var editor = new Vector2Editor<int> { Header = "Size", FirstValue = 1920, SecondValue = 1080 };
        using var host = new EditorTestHost<Vector2Editor<int>>(editor);

        var confirmed = new List<(int, int)>();
        editor.ValueConfirmed += (_, e) => confirmed.Add(((PropertyEditorValueChangedEventArgs<(int, int)>)e).NewValue);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        TextBox second = host.Require<TextBox>("PART_InnerSecondTextBox");

        host.TypeInto(first, "1920/2");
        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(960));
            Assert.That(first.Text, Is.EqualTo("1920/2"));
        });

        host.TypeInto(second, "1080/2");
        Assert.Multiple(() =>
        {
            Assert.That(first.Text, Is.EqualTo("960"));
            Assert.That(editor.SecondValue, Is.EqualTo(540));
            Assert.That(second.Text, Is.EqualTo("1080/2"));
        });

        host.MoveFocusToSink();
        Assert.Multiple(() =>
        {
            Assert.That(second.Text, Is.EqualTo("540"));
            Assert.That(confirmed[^1], Is.EqualTo((960, 540)));
        });
    }

    [AvaloniaTest]
    public void Vector2Editor_uniform_applies_expression_result_to_both_components()
    {
        var editor = new Vector2Editor<float> { Header = "Scale", IsUniform = true, FirstValue = 100, SecondValue = 100 };
        using var host = new EditorTestHost<Vector2Editor<float>>(editor);

        TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
        host.TypeInto(first, "50*3");
        host.MoveFocusToSink();

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(150f));
            Assert.That(editor.SecondValue, Is.EqualTo(150f));
            Assert.That(first.Text, Is.EqualTo("150"));
        });
    }

    [AvaloniaTest]
    public void RelativePointEditor_evaluates_percent_expression_and_keeps_relative_unit()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var editor = new RelativePointEditor
            {
                Header = "Origin",
                Unit = RelativeUnit.Relative,
                FirstValue = 0.5f,
                SecondValue = 0.5f,
            };
            using var host = new EditorTestHost<RelativePointEditor>(editor);

            TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
            host.TypeInto(first, "50.00%/2");
            Assert.Multiple(() =>
            {
                Assert.That(DataValidationErrors.GetHasErrors(editor), Is.False);
                Assert.That(editor.FirstValue, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(editor.Unit, Is.EqualTo(RelativeUnit.Relative));
            });

            host.MoveFocusToSink();
            Assert.That(first.Text, Is.EqualTo($"{25f:f}%"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
