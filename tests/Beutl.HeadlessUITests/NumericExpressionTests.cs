using System.Globalization;

using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.VisualTree;

using Beutl.Controls.PropertyEditors;
using Beutl.Testing.Headless;

using RelativeUnit = Beutl.Graphics.RelativeUnit;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class NumericExpressionTests
{
    [TestCase("10 + 5", 15d)]
    [TestCase("10 - 3", 7d)]
    [TestCase("6 * 7", 42d)]
    [TestCase("1920 / 2", 960d)]
    [TestCase("370 % 360", 10d)]
    [TestCase("2 ^ 3", 8d)]
    [TestCase("2 + 3 * 4", 14d)]
    [TestCase("(2 + 3) * 4", 20d)]
    [TestCase("10 - 6 / 2", 7d)]
    [TestCase("10 - 3 - 2", 5d)]
    [TestCase("24 / 6 / 2", 2d)]
    [TestCase("2 ^ 3 ^ 2", 512d)]
    [TestCase("2 ^ -3", 0.125d)]
    [TestCase("-2 ^ 2", -4d)]
    [TestCase("(-2) ^ 2", 4d)]
    [TestCase("0 ^ 0", 1d)]
    [TestCase("4 ^ 0.5", 2d)]
    [TestCase("-5 + 10", 5d)]
    [TestCase("-(2 + 3)", -5d)]
    [TestCase("10 * -2", -20d)]
    [TestCase("10 - -5", 15d)]
    [TestCase("--5", 5d)]
    [TestCase("+3 + +4", 7d)]
    [TestCase("((1 + 2) * (3 + 4)) / 3", 7d)]
    [TestCase("(42)", 42d)]
    [TestCase("  1920/2  ", 960d)]
    [TestCase(".5 + .5", 1d)]
    [TestCase("1e3 + 500", 1500d)]
    [TestCase("1.5e-1 * 10", 1.5d)]
    [TestCase("1E+2 / 4", 25d)]
    [TestCase("1e50 / 1e48", 100d)]
    [TestCase("0.1 ^ -30", 1e30d)]
    [TestCase("0.1 ^ 30", 1e-30d)]
    [TestCase("100 ^ -20", 1e-40d)]
    [TestCase("1e-20 * 1e-20", 1e-40d)]
    [TestCase("1e-20 / 1e20", 1e-40d)]
    [TestCase("1e-30 * 1e28", 0.01d)]
    [TestCase("1e-28 * 1.5 * 1e28", 1.5d)]
    [TestCase("1e-10 * 1e-18 * 1.5 * 1e28", 1.5d)]
    [TestCase("0.1 ^ 28 * 1.5 * 10 ^ 28", 1.5d)]
    [TestCase("(1 + 1e-16 - 1) * 1e16", 1d)]
    [TestCase("0e-30 + 1", 1d)]
    [SetCulture("en-US")]
    public void Evaluates_arithmetic_expressions_for_double(string text, double expected)
    {
        Assert.That(NumberEditorHelper.TryParse(text, out double actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected).Within(Math.Abs(expected) * 1e-12 + 1e-300));
    }

    [Test]
    [SetCulture("en-US")]
    public void Decimal_first_evaluation_avoids_binary_floating_point_noise()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NumberEditorHelper.TryParse("0.1 + 0.2", out double d1), Is.True);
            Assert.That(d1, Is.EqualTo(0.3d));

            Assert.That(NumberEditorHelper.TryParse("10.5 - 10.2", out double d2), Is.True);
            Assert.That(d2, Is.EqualTo(0.3d));

            Assert.That(NumberEditorHelper.TryParse("100 * 1.15", out int i), Is.True);
            Assert.That(i, Is.EqualTo(115));

            Assert.That(NumberEditorHelper.TryParse("1.50 + 0.50", out decimal dec), Is.True);
            Assert.That(NumberEditorHelper.Format(dec, null), Is.EqualTo("2"));

            Assert.That(NumberEditorHelper.TryParse("0 * -5", out double zero), Is.True);
            Assert.That(NumberEditorHelper.Format(zero, null), Is.EqualTo("0"));
        });
    }

    [TestCase("1920 / 2", 960)]
    [TestCase("1920 * 0.5", 960)]
    [TestCase("10 / 3", 3)]
    [TestCase("2 / 3", 1)]
    [TestCase("-5 / 2", -3)]
    [TestCase("(10 + 20) * 2", 60)]
    [TestCase("2 ^ 10", 1024)]
    [SetCulture("en-US")]
    public void Evaluates_and_rounds_expressions_for_integers(string text, int expected)
    {
        Assert.That(NumberEditorHelper.TryParse(text, out int actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    [SetCulture("en-US")]
    public void Rejects_out_of_range_expression_results_for_bounded_numeric_types()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NumberEditorHelper.TryParse("100 + 50", out byte bOk), Is.True);
            Assert.That(bOk, Is.EqualTo((byte)150));
            Assert.That(NumberEditorHelper.TryParse("200 + 100", out byte _), Is.False);
            Assert.That(NumberEditorHelper.TryParse("10 - 20", out byte _), Is.False);

            Assert.That(NumberEditorHelper.TryParse("100 + 27", out sbyte sbOk), Is.True);
            Assert.That(sbOk, Is.EqualTo((sbyte)127));
            Assert.That(NumberEditorHelper.TryParse("100 + 28", out sbyte _), Is.False);

            Assert.That(NumberEditorHelper.TryParse("2147483647 + 1", out int _), Is.False);
            Assert.That(NumberEditorHelper.TryParse("10 - 20", out uint _), Is.False);
            Assert.That(NumberEditorHelper.TryParse("10 - 20", out ulong _), Is.False);
            Assert.That(NumberEditorHelper.TryParse("1e38 * 10", out float _), Is.False);
        });
    }

    [TestCase("1.5")]
    [TestCase("-1.5")]
    [TestCase("1.000")]
    [SetCulture("de-DE")]
    public void Plain_decimal_text_stays_rejected_for_integers(string text)
    {
        Assert.That(NumberEditorHelper.TryParse(text, out int _), Is.False);
    }

    [TestCase("1,5 + 2,5", 4f)]
    [TestCase("1.5 + 2.5", 4f)]
    [TestCase("1.000,5 + 0,5", 1001f)]
    [TestCase("(1,5 + 0,5) * 3", 6f)]
    [SetCulture("de-DE")]
    public void Expressions_respect_comma_decimal_and_period_decimal_in_comma_culture(string text, float expected)
    {
        Assert.That(NumberEditorHelper.TryParse(text, out float actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("1,000.5 + 0.5", 1001f)]
    [SetCulture("en-US")]
    public void Expressions_respect_group_separators_in_period_culture(string text, float expected)
    {
        Assert.That(NumberEditorHelper.TryParse(text, out float actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase("fr-FR")]
    [TestCase("de-CH")]
    public void Expressions_respect_culture_specific_group_separators(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            string group = culture.NumberFormat.NumberGroupSeparator;
            string dec = culture.NumberFormat.NumberDecimalSeparator;

            string text = $"1{group}000{dec}5 + 0{dec}5";
            Assert.That(NumberEditorHelper.TryParse(text, out float actual), Is.True);
            Assert.That(actual, Is.EqualTo(1001f));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("10 +")]
    [TestCase("* 5")]
    [TestCase("10 + * 2")]
    [TestCase("(10 + 5")]
    [TestCase("10 + 5)")]
    [TestCase("()")]
    [TestCase("abc")]
    [TestCase("10 / 0")]
    [TestCase("10 % 0")]
    [TestCase("1 / (2 - 2)")]
    [TestCase("0 ^ -1")]
    [TestCase("(-2) ^ 0.5")]
    [TestCase("10 ^ 1000")]
    [TestCase("1e308 * 10")]
    [TestCase("1e+")]
    [TestCase("1e")]
    [TestCase("1.2.3 + 1")]
    [SetCulture("en-US")]
    public void Rejects_invalid_and_non_finite_expressions(string text)
    {
        Assert.That(NumberEditorHelper.TryParse(text, out double _), Is.False);
    }

    [Test]
    [SetCulture("en-US")]
    public void Rejects_excessively_nested_parentheses_without_overflowing_stack()
    {
        string deep = new string('(', 100) + "1" + new string(')', 100);
        Assert.That(NumberEditorHelper.TryParse(deep, out double _), Is.False);
    }

    [TestCase("50+25%", 0.75f, RelativeUnit.Relative)]
    [TestCase("(50+25)%", 0.75f, RelativeUnit.Relative)]
    [TestCase("50.00%/2", 0.25f, RelativeUnit.Relative)]
    [TestCase("50.00% / 2", 0.25f, RelativeUnit.Relative)]
    [TestCase("50% + 25%", 0.75f, RelativeUnit.Relative)]
    [TestCase("50% - 10", 0.40f, RelativeUnit.Relative)]
    [TestCase("(50% + 25%) / 2", 0.375f, RelativeUnit.Relative)]
    [TestCase("100 + 50", 150f, RelativeUnit.Absolute)]
    [TestCase("100 % 3", 1f, RelativeUnit.Absolute)]
    [SetCulture("en-US")]
    public void RelativeUnitParser_evaluates_expressions_with_and_without_percent_units(
        string text, float expectedValue, RelativeUnit expectedUnit)
    {
        Assert.That(RelativeUnitParser.TryParse(text, out float value, out RelativeUnit unit), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(expectedValue).Within(0.0001f));
            Assert.That(unit, Is.EqualTo(expectedUnit));
        });
    }

    [TestCase("%")]
    [TestCase("50%%")]
    [TestCase("50%+")]
    [SetCulture("en-US")]
    public void RelativeUnitParser_rejects_malformed_percent_expressions(string text)
    {
        Assert.That(RelativeUnitParser.TryParse(text, out _, out _), Is.False);
    }

    [AvaloniaTest]
    public void SimpleColorPicker_opacity_evaluates_expression_clears_errors_and_formats_on_focus_loss()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var picker = new SimpleColorPicker { Color = Colors.Red };
        var sink = new Button { Content = "sink" };
        var window = new Window { Content = new StackPanel { Children = { picker, sink } } };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            TextBox box = picker.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "OpacityBox");

            box.Focus();
            HeadlessTestHelpers.Settle();

            box.Text = "50+";
            HeadlessTestHelpers.Settle();
            Assert.That(DataValidationErrors.GetHasErrors(box), Is.True);

            box.Text = "50% + 25%";
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(DataValidationErrors.GetHasErrors(box), Is.False);
                Assert.That(picker.Color.A, Is.EqualTo(0.75 * 255).Within(1));
            });

            sink.Focus();
            HeadlessTestHelpers.Settle();
            Assert.That(box.Text, Is.EqualTo("75%"));
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentCulture = previous;
        }
    }
}
