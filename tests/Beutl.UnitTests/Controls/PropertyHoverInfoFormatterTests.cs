using Beutl.Controls.PropertyEditors;
using Beutl.Language;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace Beutl.UnitTests.Controls;

[TestFixture]
public class PropertyHoverInfoFormatterTests
{
    [Test]
    public void Format_CombinesDescriptionTypeAndRange()
    {
        string? text = PropertyHoverInfoFormatter.Format(typeof(float), "Rectangle width",
            [new RangeAttribute(0, 100)]);

        Assert.That(text, Is.EqualTo(string.Join(Environment.NewLine,
            "Rectangle width",
            string.Format(Strings.PropertyHover_Type, "float"),
            string.Format(Strings.PropertyHover_Range, 0, 100))));
    }

    [Test]
    public void Format_UsesTypeEvenWhenDescriptionAndRangeAreMissing()
    {
        string? text = PropertyHoverInfoFormatter.Format(typeof(int?), null, []);

        Assert.That(text, Is.EqualTo(string.Format(Strings.PropertyHover_Type, "int?")));
    }

    [Test]
    public void Format_FormatsTypedRangeBounds()
    {
        string? text = PropertyHoverInfoFormatter.Format(typeof(Beutl.Graphics.Size), null,
            [new RangeAttribute(typeof(Beutl.Graphics.Size), "0,0", "max,max")]);

        Assert.That(text, Does.Contain(string.Format(Strings.PropertyHover_Range, "0,0", "max,max")));
    }
}
