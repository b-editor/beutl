using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;

namespace Beutl.HeadlessUITests;

// .NET writes negative zero as "-0", and an expression such as Round(-0.2) or a typed "-0" gives one (#4176).
[TestFixture]
public sealed class NumberEditorFormatTests
{
    [TestCase(null, "0")]
    [TestCase("F2", "0.00")]
    [SetCulture("en-US")]
    public void Negative_zero_is_written_as_zero(string? format, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(NumberEditorHelper.Format(-0f, format), Is.EqualTo(expected));
            Assert.That(NumberEditorHelper.Format(-0d, format), Is.EqualTo(expected));
            Assert.That(NumberEditorHelper.Format(-1.5, format), Does.StartWith("-1.5"));
        });
    }

    [AvaloniaTest]
    public void A_number_box_and_a_vector_box_show_negative_zero_as_zero()
    {
        var number = new NumberEditor<float> { Value = 1 };
        var vector = new Vector2Editor<float> { FirstValue = 1, SecondValue = 1 };

        number.Value = -0f;
        vector.FirstValue = -0f;

        Assert.Multiple(() =>
        {
            Assert.That(number.Text, Is.EqualTo("0"));
            Assert.That(vector.FirstText, Is.EqualTo("0"));
        });
    }
}
