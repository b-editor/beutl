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

    private static IEnumerable<TestCaseData> NullArgumentCalls()
    {
        yield return Case("AddItemAndPersist, null project",
            () => ProjectPersistence.AddItemAndPersist(null!, new FakeProjectItem(), () => { }));
        yield return Case("AddItemAndPersist, null item",
            () => ProjectPersistence.AddItemAndPersist(new Project(), null!, () => { }));
        yield return Case("AddItemAndPersist, null persist",
            () => ProjectPersistence.AddItemAndPersist(new Project(), new FakeProjectItem(), null!));
        yield return Case("RemoveItemAndPersist, null project",
            () => ProjectPersistence.RemoveItemAndPersist(null!, new FakeProjectItem(), () => { }));
        yield return Case("RemoveItemAndPersist, null item",
            () => ProjectPersistence.RemoveItemAndPersist(new Project(), null!, () => { }));
        yield return Case("RemoveItemAndPersist, null persist",
            () => ProjectPersistence.RemoveItemAndPersist(new Project(), new FakeProjectItem(), null!));

        static TestCaseData Case(string name, Action call) => new TestCaseData(call).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(NullArgumentCalls))]
    public void NullArgument_Throws(Action call)
    {
        Assert.Throws<ArgumentNullException>(() => call());
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

        Assert.That(project.Items, Has.Count.EqualTo(3));
        Assert.That(project.Items[1], Is.SameAs(target));
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

    // The parameterless overloads are the only path through the real StoreProject.

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
