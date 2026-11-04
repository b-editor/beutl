using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Where the decimal separator is a comma, an editor took a typed period for a group separator, so "1.5" was saved
// as 15 (#3598). A period reads as the decimal point there too, and the culture's own separators still work.
[TestFixture]
public sealed class NumberEditorCultureTests
{
    [TestCase("1.5", 1.5f)]
    [TestCase("1,5", 1.5f)]
    [TestCase("1.000", 1f)]
    [TestCase("1.000,5", 1000.5f)]
    [SetCulture("de-DE")]
    public void A_comma_decimal_culture_reads_either_decimal_point(string text, float expected)
    {
        AssertReads(text, expected);
    }

    // An integer editor reads no decimal point, so a grouped "1.000" is not taken for 1 and stays rejected.
    [TestCase("1.000")]
    [TestCase("1.5")]
    [SetCulture("de-DE")]
    public void An_integer_editor_reads_no_decimal_point(string text)
    {
        Assert.That(NumberEditorHelper.TryParseEdit(text, "2", out int _, out _), Is.False);
    }

    [TestCase("1.5", 1.5f)]
    [TestCase("1,000.5", 1000.5f)]
    [SetCulture("en-US")]
    public void A_period_decimal_culture_reads_as_before(string text, float expected)
    {
        AssertReads(text, expected);
    }

    [AvaloniaTest]
    [TestCase("50.5")]
    [TestCase("50,5")]
    [TestCase("50,5%")]
    public void The_color_pickers_opacity_reads_either_decimal_point(string text)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        var picker = new SimpleColorPicker { Color = Colors.Red };
        var window = new Window { Content = picker };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox box = picker.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "OpacityBox");

            box.Text = text;
            HeadlessTestHelpers.Settle();

            Assert.That(picker.Color.A, Is.EqualTo(0.505 * 255).Within(1));
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static void AssertReads(string text, float expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(NumberEditorHelper.TryParseEdit(text, "2", out float value, out _), Is.True);
            Assert.That(value, Is.EqualTo(expected));
            Assert.That(RelativeUnitParser.TryParse(text, out float relative, out _), Is.True);
            Assert.That(relative, Is.EqualTo(expected));
        });
    }
}
