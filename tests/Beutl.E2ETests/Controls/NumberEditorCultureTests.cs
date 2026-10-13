using System.Globalization;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;

using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

// Numeric property editors format and parse with the regional CurrentCulture, not the UI-language
// CurrentUICulture. These tests split the two: UI language en-US, regional format de-DE (comma decimal).
[TestFixture]
public class NumberEditorCultureTests
{
    private static readonly CultureInfo s_regional = CultureInfo.GetCultureInfo("de-DE");
    private static readonly CultureInfo s_uiLanguage = CultureInfo.GetCultureInfo("en-US");

    private static void WithSplitCulture(Action body)
    {
        CultureInfo prevCulture = CultureInfo.CurrentCulture;
        CultureInfo prevUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = s_regional;
            CultureInfo.CurrentUICulture = s_uiLanguage;
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUiCulture;
        }
    }

    [AvaloniaTest]
    public void NumberEditor_formats_value_with_the_regional_culture()
    {
        WithSplitCulture(() =>
        {
            var editor = new NumberEditor<double> { Header = "Value" };
            using var host = new EditorTestHost<NumberEditor<double>>(editor);

            editor.Value = 2.5;

            // de-DE renders 2.5 as "2,5"; the UI language (en-US) would render "2.5".
            Assert.That(editor.Text, Is.EqualTo("2,5"));
        });
    }

    [AvaloniaTest]
    public void NumberEditor_parses_typed_text_with_the_regional_culture()
    {
        WithSplitCulture(() =>
        {
            var editor = new NumberEditor<double> { Header = "Value" };
            using var host = new EditorTestHost<NumberEditor<double>>(editor);

            TextBox box = host.Require<TextBox>("PART_InnerTextBox");
            host.TypeInto(box, "2,5");

            // Parsed under en-US the comma is a group separator, so "2,5" would become 25.
            Assert.That(editor.Value, Is.EqualTo(2.5));
        });
    }

    [AvaloniaTest]
    public void NumberEditor_preserves_a_decimal_prefix_until_focus_loss()
    {
        WithSplitCulture(() =>
        {
            var editor = new NumberEditor<double> { Header = "Range: 50–200", Value = 100, NumberFormat = "F2" };
            using var host = new EditorTestHost<NumberEditor<double>>(editor);
            editor.ValueChanged += (_, _) => editor.Value = Math.Clamp(editor.Value, 50, 200);
            TextBox box = host.Require<TextBox>("PART_InnerTextBox");

            host.TypeInto(box, "7");
            host.Window.KeyTextInput("0");
            host.Window.KeyTextInput(",");
            HeadlessTestHelpers.Settle();
            Assert.That(box.Text, Is.EqualTo("70,"));

            host.Window.KeyTextInput("5");
            HeadlessTestHelpers.Settle();
            Assert.That(box.Text, Is.EqualTo("70,5"));
            Assert.That(editor.Value, Is.EqualTo(70.5));

            host.MoveFocusToSink();
            Assert.That(box.Text, Is.EqualTo("70,50"));
        });
    }

    [AvaloniaTest]
    public void Vector2Editor_parses_typed_component_with_the_regional_culture()
    {
        WithSplitCulture(() =>
        {
            var editor = new Vector2Editor<double> { Header = "Size" };
            using var host = new EditorTestHost<Vector2Editor<double>>(editor);

            TextBox first = host.Require<TextBox>("PART_InnerFirstTextBox");
            host.TypeInto(first, "2,5");

            Assert.That(editor.FirstValue, Is.EqualTo(2.5));
        });
    }
}
