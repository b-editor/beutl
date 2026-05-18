using Beutl.Engine;
using DisplayAttribute = System.ComponentModel.DataAnnotations.DisplayAttribute;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;
using ValidationAttribute = System.ComponentModel.DataAnnotations.ValidationAttribute;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class PropertyFactoryTests
{
    [Test]
    public void Create_ReturnsSimpleProperty_NotAnimatable()
    {
        IProperty<double> property = Property.Create(1.5);

        Assert.That(property, Is.InstanceOf<SimpleProperty<double>>());
        Assert.That(property.IsAnimatable, Is.False);
        Assert.That(property.SupportsExpression, Is.True);
        Assert.That(property.DefaultValue, Is.EqualTo(1.5));
        Assert.That(property.CurrentValue, Is.EqualTo(1.5));
        Assert.That(property.HasLocalValue, Is.False);
    }

    [Test]
    public void Create_WithDefaultedGeneric_UsesTypeDefault()
    {
        IProperty<int> property = Property.Create<int>();

        Assert.That(property.DefaultValue, Is.EqualTo(0));
        Assert.That(property.CurrentValue, Is.EqualTo(0));
    }

    [Test]
    public void Create_WithValidationAttributes_AppliesValidatorOnSet()
    {
        IProperty<int> property = Property.Create(0, new RangeAttribute(0, 10));
        property.SetAttributes("Test", []);

        property.CurrentValue = 100;

        Assert.That(
            property.CurrentValue,
            Is.EqualTo(10),
            "Out-of-range value should be coerced to max."
        );
    }

    [Test]
    public void Create_WithoutValidationAttributes_HasNoValidator()
    {
        var property = (SimpleProperty<int>)Property.Create(5);

        Assert.That(property.HasValidator, Is.False);
    }

    [Test]
    public void Create_WithEmptyValidationAttributesParams_HasNoValidator()
    {
        var property = (SimpleProperty<int>)Property.Create(5, new ValidationAttribute[0]);

        Assert.That(property.HasValidator, Is.False);
    }

    [Test]
    public void CreateAnimatable_ReturnsAnimatableProperty()
    {
        IProperty<float> property = Property.CreateAnimatable(2.0f);

        Assert.That(property, Is.InstanceOf<AnimatableProperty<float>>());
        Assert.That(property.IsAnimatable, Is.True);
        Assert.That(property.DefaultValue, Is.EqualTo(2.0f));
    }

    [Test]
    public void CreateAnimatable_WithValidationAttributes_AppliesValidator()
    {
        IProperty<int> property = Property.CreateAnimatable(0, new RangeAttribute(-5, 5));
        property.SetAttributes("Test", []);

        property.CurrentValue = -100;

        Assert.That(property.CurrentValue, Is.EqualTo(-5));
    }

    [Test]
    public void CreateList_ReturnsListProperty()
    {
        IListProperty<string> property = Property.CreateList<string>();

        Assert.That(property, Is.InstanceOf<ListProperty<string>>());
        Assert.That(property.ElementType, Is.EqualTo(typeof(string)));
        Assert.That(property.Count, Is.EqualTo(0));
    }

    [Test]
    public void GetLocalizedName_ReturnsDisplayNameWhenAttributeSet()
    {
        IProperty<int> property = Property.Create(0);
        property.SetAttributes(
            "RawName",
            new Attribute[] { new DisplayAttribute { Name = "Friendly" } }
        );

        Assert.That(Property.GetLocalizedName(property), Is.EqualTo("Friendly"));
    }

    [Test]
    public void GetLocalizedName_FallsBackToPropertyName()
    {
        IProperty<int> property = Property.Create(0);
        property.SetAttributes("RawName", []);

        Assert.That(Property.GetLocalizedName(property), Is.EqualTo("RawName"));
    }
}
