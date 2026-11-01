using Beutl.Controls.PropertyEditors;

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

    [TestCase("1.5", 1.5f)]
    [TestCase("1,000.5", 1000.5f)]
    [SetCulture("en-US")]
    public void A_period_decimal_culture_reads_as_before(string text, float expected)
    {
        AssertReads(text, expected);
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
