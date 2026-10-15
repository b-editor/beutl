using Beutl.Engine;
using DisplayAttribute = System.ComponentModel.DataAnnotations.DisplayAttribute;

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
    public void CreateAnimatable_ReturnsAnimatableProperty()
    {
        IProperty<float> property = Property.CreateAnimatable(2.0f);

        Assert.That(property, Is.InstanceOf<AnimatableProperty<float>>());
        Assert.That(property.IsAnimatable, Is.True);
        Assert.That(property.DefaultValue, Is.EqualTo(2.0f));
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
            new Attribute[] { new DisplayAttribute { Name = "Friendly" } });

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
