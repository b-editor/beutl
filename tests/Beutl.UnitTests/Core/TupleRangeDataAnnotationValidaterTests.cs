using Beutl.Graphics;
using Beutl.Validation;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl.UnitTests.Core;

public class TupleRangeDataAnnotationValidaterTests
{
    private static ValidationContext EmptyContext => default;

    [Test]
    public void Constructor_StoresAttribute_AndParsesBounds()
    {
        var attr = new RangeAttribute(typeof(Vector), "0,0", "10,10");
        var validator = new TupleRangeDataAnnotationValidater<Vector, float>(attr);

        Assert.That(validator.Attribute, Is.SameAs(attr));
        Assert.That(validator.Minimum, Is.EqualTo(new Vector(0, 0)));
        Assert.That(validator.Maximum, Is.EqualTo(new Vector(10, 10)));
    }

    [Test]
    public void Constructor_RejectsExclusiveBounds()
    {
        var attr = new RangeAttribute(typeof(Vector), "0,0", "10,10")
        {
            MaximumIsExclusive = true
        };

        Assert.Throws<NotSupportedException>(
            () => new TupleRangeDataAnnotationValidater<Vector, float>(attr));
    }

    [Test]
    public void Constructor_RejectsExclusiveMinimum()
    {
        var attr = new RangeAttribute(typeof(Vector), "0,0", "10,10")
        {
            MinimumIsExclusive = true
        };

        Assert.Throws<NotSupportedException>(
            () => new TupleRangeDataAnnotationValidater<Vector, float>(attr));
    }

    [Test]
    public void TryCoerce_ClampsEachComponent()
    {
        var attr = new RangeAttribute(typeof(Vector), "0,0", "10,10");
        var validator = new TupleRangeDataAnnotationValidater<Vector, float>(attr);

        Vector value = new(-3f, 25f);
        Assert.That(validator.TryCoerce(EmptyContext, ref value), Is.True);
        Assert.That(value, Is.EqualTo(new Vector(0f, 10f)));
    }

    [Test]
    public void TryCoerce_InsideRange_RetainsValue()
    {
        var attr = new RangeAttribute(typeof(Vector), "0,0", "10,10");
        var validator = new TupleRangeDataAnnotationValidater<Vector, float>(attr);

        Vector value = new(4f, 6f);
        Assert.That(validator.TryCoerce(EmptyContext, ref value), Is.True);
        Assert.That(value, Is.EqualTo(new Vector(4f, 6f)));
    }

    [Test]
    public void Validate_ReturnsNull_NoMatterTheValue()
    {
        var attr = new RangeAttribute(typeof(Vector), "0,0", "10,10");
        var validator = new TupleRangeDataAnnotationValidater<Vector, float>(attr);

        Assert.That(validator.Validate(EmptyContext, new Vector(50f, 50f)), Is.Null);
    }
}
