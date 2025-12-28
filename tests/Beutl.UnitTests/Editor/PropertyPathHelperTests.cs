using Beutl.Editor.Infrastructure;

namespace Beutl.UnitTests.Editor;

public class PropertyPathHelperTests
{
    [Test]
    public void GetPropertyNameFromPath_WhenPathHasNoDots_ReturnsSamePath()
    {
        // Arrange
        string path = "PropertyName";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo("PropertyName"));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenPathHasOneDot_ReturnsLastPart()
    {
        // Arrange
        string path = "Parent.Child";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo("Child"));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenPathHasMultipleDots_ReturnsLastPart()
    {
        // Arrange
        string path = "Root.Parent.Child.Property";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo("Property"));
    }

    [TestCase("a.b", "b")]
    [TestCase("x.y.z", "z")]
    [TestCase("One.Two.Three.Four", "Four")]
    [TestCase("Property", "Property")]
    public void GetPropertyNameFromPath_VariousPaths_ReturnsExpectedPropertyName(string path, string expected)
    {
        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenPathEndsWithDot_ReturnsEmptyString()
    {
        // Arrange
        string path = "Parent.Child.";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenPathIsEmpty_ReturnsEmptyString()
    {
        // Arrange
        string path = "";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenPathStartsWithDot_ReturnsLastPart()
    {
        // Arrange
        string path = ".Property";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo("Property"));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenPathHasConsecutiveDots_ReturnsEmptyString()
    {
        // Arrange
        string path = "Parent..Property";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo("Property"));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenPathContainsIndexer_ReturnsLastPart()
    {
        // Arrange
        string path = "Items[0].Value";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo("Value"));
    }

    [Test]
    public void GetPropertyNameFromPath_WhenLastPartContainsSpecialCharacters_ReturnsLastPart()
    {
        // Arrange
        string path = "Parent.Child_Property";

        // Act
        string result = PropertyPathHelper.GetPropertyNameFromPath(path);

        // Assert
        Assert.That(result, Is.EqualTo("Child_Property"));
    }
}
