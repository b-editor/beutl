namespace Beutl.UnitTests.Core;

public class ProjectMutateAndPersistTests
{
    private sealed class FakeProjectItem : ProjectItem
    {
    }

    [Test]
    public void AddAndPersist_NewItemPersistSucceeds_ItemAdded()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        int persistCalls = 0;

        project.AddAndPersist(item, () => persistCalls++);

        Assert.That(project.Items, Does.Contain(item));
        Assert.That(persistCalls, Is.EqualTo(1));
        // Adding through the HierarchicalList must also attach the parent.
        Assert.That(item.HierarchicalParent, Is.SameAs(project));
    }

    [Test]
    public void AddAndPersist_NewItemPersistThrows_ItemRolledBack()
    {
        var project = new Project();
        var item = new FakeProjectItem();

        Assert.Throws<InvalidOperationException>(() =>
            project.AddAndPersist(item, () => throw new InvalidOperationException("disk full")));

        Assert.That(project.Items, Does.Not.Contain(item));
        // Rolling the add back must also detach the parent, not just drop the list entry.
        Assert.That(item.HierarchicalParent, Is.Null);
    }

    [Test]
    public void AddAndPersist_ExistingItemPersistThrows_ItemNotRemoved()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        project.Items.Add(item);

        Assert.Throws<InvalidOperationException>(() =>
            project.AddAndPersist(item, () => throw new InvalidOperationException("disk full")));

        // The item was already a member, so a failed persist must not remove it.
        Assert.That(project.Items, Does.Contain(item));
    }

    [Test]
    public void AddAndPersist_ExistingItem_NotAddedTwice()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        project.Items.Add(item);

        project.AddAndPersist(item, () => { });

        Assert.That(project.Items.Count(i => ReferenceEquals(i, item)), Is.EqualTo(1));
    }

    [Test]
    public void AddAndPersist_NullItem_Throws()
    {
        var project = new Project();

        Assert.Throws<ArgumentNullException>(() => project.AddAndPersist(null!, () => { }));
    }

    [Test]
    public void AddAndPersist_NullPersist_Throws()
    {
        var project = new Project();
        var item = new FakeProjectItem();

        Assert.Throws<ArgumentNullException>(() => project.AddAndPersist(item, null!));
    }

    [Test]
    public void RemoveAndPersist_PresentItemPersistSucceeds_ItemRemoved()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        project.Items.Add(item);
        int persistCalls = 0;

        project.RemoveAndPersist(item, () => persistCalls++);

        Assert.That(project.Items, Does.Not.Contain(item));
        Assert.That(persistCalls, Is.EqualTo(1));
        // Removing through the HierarchicalList must also detach the parent.
        Assert.That(item.HierarchicalParent, Is.Null);
    }

    [Test]
    public void RemoveAndPersist_PersistThrows_ItemReinsertedAtOriginalIndex()
    {
        var project = new Project();
        var first = new FakeProjectItem();
        var target = new FakeProjectItem();
        var last = new FakeProjectItem();
        project.Items.Add(first);
        project.Items.Add(target);
        project.Items.Add(last);

        Assert.Throws<InvalidOperationException>(() =>
            project.RemoveAndPersist(target, () => throw new InvalidOperationException("disk full")));

        // The removal is rolled back, restoring the original position.
        Assert.That(project.Items, Has.Count.EqualTo(3));
        Assert.That(project.Items[1], Is.SameAs(target));
        // Re-inserting must re-attach the parent, not just restore the list entry.
        Assert.That(target.HierarchicalParent, Is.SameAs(project));
    }

    [Test]
    public void RemoveAndPersist_ItemNotPresent_PersistStillRuns()
    {
        var project = new Project();
        var item = new FakeProjectItem();
        int persistCalls = 0;

        project.RemoveAndPersist(item, () => persistCalls++);

        Assert.That(persistCalls, Is.EqualTo(1));
        Assert.That(project.Items, Does.Not.Contain(item));
    }

    [Test]
    public void RemoveAndPersist_NullItem_Throws()
    {
        var project = new Project();

        Assert.Throws<ArgumentNullException>(() => project.RemoveAndPersist(null!, () => { }));
    }

    [Test]
    public void RemoveAndPersist_NullPersist_Throws()
    {
        var project = new Project();
        var item = new FakeProjectItem();

        Assert.Throws<ArgumentNullException>(() => project.RemoveAndPersist(item, null!));
    }
}
