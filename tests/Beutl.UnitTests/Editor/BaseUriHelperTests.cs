using Beutl.Editor.Infrastructure;

namespace Beutl.UnitTests.Editor;

public class BaseUriHelperTests
{
    [Test]
    public void FindBaseUri_WhenObjectIsNull_ReturnsNull()
    {
        // Arrange
        ICoreObject? obj = null;

        // Act
        Uri? result = obj.FindBaseUri();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindBaseUri_WhenObjectHasUri_ReturnsThatUri()
    {
        // Arrange
        var uri = new Uri("file:///path/to/file.txt");
        var obj = new TestHierarchicalObject { Uri = uri };

        // Act
        Uri? result = obj.FindBaseUri();

        // Assert
        Assert.That(result, Is.EqualTo(uri));
    }

    [Test]
    public void FindBaseUri_WhenObjectHasNoUriButParentHasUri_ReturnsParentUri()
    {
        // Arrange
        var parentUri = new Uri("file:///path/to/parent.txt");
        var parent = new TestHierarchicalObject { Uri = parentUri };
        var child = new TestHierarchicalObject();
        parent.AddChild(child);

        // Act
        Uri? result = child.FindBaseUri();

        // Assert
        Assert.That(result, Is.EqualTo(parentUri));
    }

    [Test]
    public void FindBaseUri_WhenNoObjectInHierarchyHasUri_ReturnsNull()
    {
        // Arrange
        var parent = new TestHierarchicalObject();
        var child = new TestHierarchicalObject();
        parent.AddChild(child);

        // Act
        Uri? result = child.FindBaseUri();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindBaseUri_WhenGrandparentHasUri_ReturnsGrandparentUri()
    {
        // Arrange
        var grandparentUri = new Uri("file:///path/to/grandparent.txt");
        var grandparent = new TestHierarchicalObject { Uri = grandparentUri };
        var parent = new TestHierarchicalObject();
        var child = new TestHierarchicalObject();
        grandparent.AddChild(parent);
        parent.AddChild(child);

        // Act
        Uri? result = child.FindBaseUri();

        // Assert
        Assert.That(result, Is.EqualTo(grandparentUri));
    }

    [Test]
    public void FindBaseUri_WhenMultipleAncestorsHaveUri_ReturnsFirstAncestorUri()
    {
        // Arrange
        var grandparentUri = new Uri("file:///path/to/grandparent.txt");
        var parentUri = new Uri("file:///path/to/parent.txt");
        var grandparent = new TestHierarchicalObject { Uri = grandparentUri };
        var parent = new TestHierarchicalObject { Uri = parentUri };
        var child = new TestHierarchicalObject();
        grandparent.AddChild(parent);
        parent.AddChild(child);

        // Act
        Uri? result = child.FindBaseUri();

        // Assert
        // EnumerateAncestors starts from self, so child (no URI) -> parent (has URI) -> returns parent's URI
        Assert.That(result, Is.EqualTo(parentUri));
    }

    [Test]
    public void FindBaseUri_WhenObjectIsNotIHierarchical_ReturnsNull()
    {
        // Arrange
        var obj = new NonHierarchicalCoreObject();

        // Act
        Uri? result = obj.FindBaseUri();

        // Assert
        Assert.That(result, Is.Null);
    }

    private class TestHierarchicalObject : Hierarchical
    {
        public void AddChild(TestHierarchicalObject child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    private class NonHierarchicalCoreObject : CoreObject
    {
    }
}

