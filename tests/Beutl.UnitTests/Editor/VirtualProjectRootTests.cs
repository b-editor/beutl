using Beutl.Editor;

namespace Beutl.UnitTests.Editor;

public class VirtualProjectRootTests
{
    [Test]
    public void AttachProject_WithNullProject_ThrowsArgumentNullException()
    {
        var root = new VirtualProjectRoot();

        Assert.Throws<ArgumentNullException>(() => root.AttachProject(null!));
    }

    [Test]
    public void AttachProject_WhenProjectAlreadyAttached_ThrowsInvalidOperationException()
    {
        var root = new VirtualProjectRoot();
        var project1 = new Project();
        var project2 = new Project();
        root.AttachProject(project1);

        Assert.Throws<InvalidOperationException>(() => root.AttachProject(project2));
    }

    [Test]
    public void DetachProject_WhenNoProjectAttached_DoesNothing()
    {
        var root = new VirtualProjectRoot();

        Assert.DoesNotThrow(() => root.DetachProject());
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(0));
    }

    [Test]
    public void OnDescendantAttached_RaisesDescendantAttachedEvent()
    {
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();
        IHierarchical? eventDescendant = null;
        root.DescendantAttached += (sender, e) => eventDescendant = e;

        root.OnDescendantAttached(descendant);

        Assert.That(eventDescendant, Is.EqualTo(descendant));
    }

    [Test]
    public void OnDescendantDetached_RaisesDescendantDetachedEvent()
    {
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();
        IHierarchical? eventDescendant = null;
        root.DescendantDetached += (sender, e) => eventDescendant = e;

        root.OnDescendantDetached(descendant);

        Assert.That(eventDescendant, Is.EqualTo(descendant));
    }

    [Test]
    public void OnDescendantAttached_WhenNoSubscribers_DoesNotThrow()
    {
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();

        Assert.DoesNotThrow(() => root.OnDescendantAttached(descendant));
    }

    [Test]
    public void OnDescendantDetached_WhenNoSubscribers_DoesNotThrow()
    {
        var root = new VirtualProjectRoot();
        var descendant = new TestHierarchical();

        Assert.DoesNotThrow(() => root.OnDescendantDetached(descendant));
    }

    [Test]
    public void AttachProject_ThenDetach_ThenReattach_WorksCorrectly()
    {
        var root = new VirtualProjectRoot();
        var project1 = new Project();
        var project2 = new Project();

        root.AttachProject(project1);
        Assert.That(((IHierarchical)root).HierarchicalChildren, Has.Count.EqualTo(1));
        Assert.That(((IHierarchical)root).HierarchicalChildren.Contains(project1), Is.True);

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
