using Beutl.Serialization;
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
        // Rolling back must also detach the parent.
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
        // Re-inserting must re-attach the parent.
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

    // The two tests below exercise the parameterless overloads, which route through the real
    // StoreProject (null-Uri guard + CoreSerializer write) — the one seam the injectable overloads
    // cannot cover.

    [Test]
    public void AddItemAndPersist_ParameterlessOverload_NullProjectUri_ThrowsAndRollsBack()
    {
        var project = new Project(); // Uri is null
        var item = new FakeProjectItem();

        // StoreProject's null-Uri guard must fire, and the add must roll back through the real path.
        Assert.Throws<InvalidOperationException>(() => ProjectPersistence.AddItemAndPersist(project, item));

        Assert.That(project.Items, Does.Not.Contain(item));
        Assert.That(item.HierarchicalParent, Is.Null);
    }

    [Test]
    public void RemoveItemAndPersist_ParameterlessOverload_WritesProjectFile()
    {
        // Removing a not-present item performs no mutation, isolating the real StoreProject +
        // CoreSerializer write without serializing the test-only FakeProjectItem.
        string dir = Path.Combine(Path.GetTempPath(), $"beutl_persist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "test.bep");
            var project = new Project { Uri = new Uri(path) };

            ProjectPersistence.RemoveItemAndPersist(project, new FakeProjectItem());

            Assert.That(File.Exists(path), Is.True);
            Assert.That(CoreSerializer.RestoreFromUri<Project>(new Uri(path)), Is.Not.Null);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
