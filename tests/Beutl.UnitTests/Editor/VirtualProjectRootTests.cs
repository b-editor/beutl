using Beutl.Editor;

namespace Beutl.UnitTests.Editor;

public class VirtualProjectRootTests
{
    [Test]
    public void AttachProject_WithValidProject_AttachesSuccessfully()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var project = new Project();

        // Act
        root.AttachProject(project);

        // Assert
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(1));
        Assert.That(((IHierarchical)root).HierarchicalChildren.Contains(project), Is.True);
    }

    [Test]
    public void AttachProject_WithNullProject_ThrowsArgumentNullException()
    {
        // Arrange
        var root = new VirtualProjectRoot();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => root.AttachProject(null!));
    }

    [Test]
    public void AttachProject_WhenProjectAlreadyAttached_ThrowsInvalidOperationException()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var project1 = new Project();
        var project2 = new Project();
        root.AttachProject(project1);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => root.AttachProject(project2));
    }

    [Test]
    public void DetachProject_WhenProjectAttached_DetachesSuccessfully()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var project = new Project();
        root.AttachProject(project);

        // Act
        root.DetachProject();

        // Assert
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(0));
    }

    [Test]
    public void DetachProject_WhenNoProjectAttached_DoesNothing()
    {
        // Arrange
        var root = new VirtualProjectRoot();

        // Act & Assert (should not throw)
        Assert.DoesNotThrow(() => root.DetachProject());
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(0));
    }

    [Test]
    public void OnDescendantAttached_RaisesDescendantAttachedEvent()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();
        IHierarchical? eventDescendant = null;
        root.DescendantAttached += (sender, e) => eventDescendant = e;

        // Act
        root.OnDescendantAttached(descendant);

        // Assert
        Assert.That(eventDescendant, Is.EqualTo(descendant));
    }

    [Test]
    public void OnDescendantDetached_RaisesDescendantDetachedEvent()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();
        IHierarchical? eventDescendant = null;
        root.DescendantDetached += (sender, e) => eventDescendant = e;

        // Act
        root.OnDescendantDetached(descendant);

        // Assert
        Assert.That(eventDescendant, Is.EqualTo(descendant));
    }

    [Test]
    public void OnDescendantAttached_WhenNoSubscribers_DoesNotThrow()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();

        // Act & Assert
        Assert.DoesNotThrow(() => root.OnDescendantAttached(descendant));
    }

    [Test]
    public void OnDescendantDetached_WhenNoSubscribers_DoesNotThrow()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();

        // Act & Assert
        Assert.DoesNotThrow(() => root.OnDescendantDetached(descendant));
    }

    [Test]
    public void AttachProject_ThenDetach_ThenReattach_WorksCorrectly()
    {
        // Arrange
        var root = new VirtualProjectRoot();
        var project1 = new Project();
        var project2 = new Project();

        // Act & Assert
        root.AttachProject(project1);
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(1));

        root.DetachProject();
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(0));

        root.AttachProject(project2);
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(1));
        Assert.That(((IHierarchical)root).HierarchicalChildren.Contains(project2), Is.True);
    }

    private class TestHierarchical : Hierarchical
    {
    }
}
