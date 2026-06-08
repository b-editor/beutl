using Beutl.Services;

namespace Beutl.UnitTests.Core;

public class ProjectMutateAndPersistTests
{
    private sealed class FakeProjectItem : ProjectItem
    {
    }

    [Test]
    public void AddItemAndPersist_NewItemPersistSucceeds_ItemAdded()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        int persistCalls = 0;

        ProjectPersistence.AddItemAndPersist(project, item, () => persistCalls++);

        Assert.That(project.Items, Does.Contain(item));
        Assert.That(persistCalls, Is.EqualTo(1));
        // Adding through the HierarchicalList must also attach the parent.
        Assert.That(item.HierarchicalParent, Is.SameAs(project));
    }

    [Test]
    public void AddItemAndPersist_NewItemPersistThrows_ItemRolledBack()
    {
        var project = new Project();
        var item = new FakeProjectItem();

        Assert.Throws<InvalidOperationException>(() =>
            ProjectPersistence.AddItemAndPersist(project, item, () => throw new InvalidOperationException("disk full")));

        Assert.That(project.Items, Does.Not.Contain(item));
        // Rolling the add back must also detach the parent, not just drop the list entry.
        Assert.That(item.HierarchicalParent, Is.Null);
    }

    [Test]
    public void AddItemAndPersist_ExistingItemPersistThrows_ItemNotRemoved()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        project.Items.Add(item);

        Assert.Throws<InvalidOperationException>(() =>
            ProjectPersistence.AddItemAndPersist(project, item, () => throw new InvalidOperationException("disk full")));

        // The item was already a member, so a failed persist must not remove it.
        Assert.That(project.Items, Does.Contain(item));
    }

    [Test]
    public void AddItemAndPersist_ExistingItem_NotAddedTwice()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        project.Items.Add(item);

        ProjectPersistence.AddItemAndPersist(project, item, () => { });

        Assert.That(project.Items.Count(i => ReferenceEquals(i, item)), Is.EqualTo(1));
    }

    [Test]
    public void AddItemAndPersist_NullProject_Throws()
    {
        var item = new FakeProjectItem();

        Assert.Throws<ArgumentNullException>(() => ProjectPersistence.AddItemAndPersist(null!, item, () => { }));
    }

    [Test]
    public void AddItemAndPersist_NullItem_Throws()
    {
        var project = new Project();

        Assert.Throws<ArgumentNullException>(() => ProjectPersistence.AddItemAndPersist(project, null!, () => { }));
    }

    [Test]
    public void AddItemAndPersist_NullPersist_Throws()
    {
        var project = new Project();
        var item = new FakeProjectItem();

        Assert.Throws<ArgumentNullException>(() => ProjectPersistence.AddItemAndPersist(project, item, null!));
    }

    [Test]
    public void RemoveItemAndPersist_PresentItemPersistSucceeds_ItemRemoved()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        project.Items.Add(item);
        int persistCalls = 0;

        ProjectPersistence.RemoveItemAndPersist(project, item, () => persistCalls++);

        Assert.That(project.Items, Does.Not.Contain(item));
        Assert.That(persistCalls, Is.EqualTo(1));
        // Removing through the HierarchicalList must also detach the parent.
        Assert.That(item.HierarchicalParent, Is.Null);
    }

    [Test]
    public void RemoveItemAndPersist_PersistThrows_ItemReinsertedAtOriginalIndex()
    {
        var project = new Project();
        var first = new FakeProjectItem();
        var target = new FakeProjectItem();
        var last = new FakeProjectItem();
        project.Items.Add(first);
        project.Items.Add(target);
        project.Items.Add(last);

        Assert.Throws<InvalidOperationException>(() =>
            ProjectPersistence.RemoveItemAndPersist(project, target, () => throw new InvalidOperationException("disk full")));

        // The removal is rolled back, restoring the original position.
        Assert.That(project.Items, Has.Count.EqualTo(3));
        Assert.That(project.Items[1], Is.SameAs(target));
        // Re-inserting must re-attach the parent, not just restore the list entry.
        Assert.That(target.HierarchicalParent, Is.SameAs(project));
    }

    [Test]
    public void RemoveItemAndPersist_ItemNotPresent_PersistStillRuns()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        int persistCalls = 0;

        ProjectPersistence.RemoveItemAndPersist(project, item, () => persistCalls++);

        Assert.That(persistCalls, Is.EqualTo(1));
        Assert.That(project.Items, Does.Not.Contain(item));
    }

    [Test]
    public void RemoveItemAndPersist_NullProject_Throws()
    {
        var item = new FakeProjectItem();

        Assert.Throws<ArgumentNullException>(() => ProjectPersistence.RemoveItemAndPersist(null!, item, () => { }));
    }

    [Test]
    public void RemoveItemAndPersist_NullItem_Throws()
    {
        var project = new Project();

        Assert.Throws<ArgumentNullException>(() => ProjectPersistence.RemoveItemAndPersist(project, null!, () => { }));
    }

    [Test]
    public void RemoveItemAndPersist_NullPersist_Throws()
    {
        var project = new Project();
        var item = new FakeProjectItem();

        Assert.Throws<ArgumentNullException>(() => ProjectPersistence.RemoveItemAndPersist(project, item, null!));
    }
}
