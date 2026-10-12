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
}
