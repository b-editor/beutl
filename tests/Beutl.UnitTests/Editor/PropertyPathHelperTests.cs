using Beutl.Editor.Infrastructure;

namespace Beutl.UnitTests.Editor;

public class PropertyPathHelperTests
{
    [TestCase("a.b", "b")]
    [TestCase("x.y.z", "z")]
    [TestCase("One.Two.Three.Four", "Four")]
    [TestCase("Property", "Property")]
    [TestCase("Parent.Child.", "")]
    [TestCase("", "")]
    [TestCase(".Property", "Property")]
    [TestCase("Parent..Property", "Property")]
    [TestCase("Items[0].Value", "Value")]
    [TestCase("Parent.Child_Property", "Child_Property")]
    public void GetPropertyNameFromPath_VariousPaths_ReturnsExpectedPropertyName(string path, string expected)
    {
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("Opacity", "Opacity", "Value")]
    [TestCase("Transform.X", "X", "Value")]
    [TestCase("Opacity.Expression", "Opacity", "Expression")]
    [TestCase("Transform.X.Animation", "X", "Animation")]
    [TestCase("Opacity.Animation.UseGlobalClock", "UseGlobalClock", "Value")]
    [TestCase("Animation", "Animation", "Value")]
    public void Parse_GivesThePropertyAndWhatChanged(string path, string name, string slot)
    {
        Assert.That(PropertyPathHelper.Parse(path), Is.EqualTo((name, Enum.Parse<PropertyPathSlot>(slot))));
        Assert.That(PropertyPathHelper.GetPropertyNameFromPath(path), Is.EqualTo(name));
    }

    [TestCase("", "Opacity")]
    [TestCase("Objects", "Transform")]
    public void Append_ThenParse_GivesTheNameBack(string parent, string name)
    {
        string path = PropertyPathHelper.Append(parent, name);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PropertyPathHelper.Parse(path), Is.EqualTo((name, PropertyPathSlot.Value)));
            Assert.That(PropertyPathHelper.Parse(PropertyPathHelper.Append(path, "Expression")),
                Is.EqualTo((name, PropertyPathSlot.Expression)));
            Assert.That(PropertyPathHelper.ChildName(path, parent), Is.EqualTo(name));
        }
    }

    [TestCase("Transform.X.Expression", "Transform", "X")]
    [TestCase("Transform", "", "Transform")]
    [TestCase("StrokeWidth.X", "Width", null)]
    [TestCase("Transform", "Transform", null)]
    public void ChildName_IsTheNameRightBelowTheParent(string path, string parent, string? expected)
    {
        Assert.That(PropertyPathHelper.ChildName(path, parent), Is.EqualTo(expected));
    }
}
