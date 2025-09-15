using System;
using System.Linq;

namespace Beutl.UnitTests.Core;

public class HierarchicalExtensionsTests
{
    private sealed class DummyItem : ProjectItem {}

    [Test]
    public void FindHierarchicalRoot_AndParent()
    {
        var app = BeutlApplication.Current;
        var proj = new Project();
        app.Project = proj;
        var item = new DummyItem { FileName = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "hi.item") };
        proj.Items.Add(item);

        // Root is application
        Assert.That(((IHierarchical)item).FindHierarchicalRoot(), Is.SameAs(app));
        // Find project as parent
        Assert.That(((IHierarchical)item).FindHierarchicalParent<Project>(), Is.SameAs(proj));
        Assert.That(((IHierarchical)item).FindRequiredHierarchicalParent<IHierarchicalRoot>(), Is.SameAs(app));
    }

    [Test]
    public void EnumerateAllChildren_ReturnsDescendants()
    {
        var proj = new Project();
        var item1 = new DummyItem { FileName = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "a.item") };
        var item2 = new DummyItem { FileName = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "b.item") };
        proj.Items.Add(item1);
        proj.Items.Add(item2);

        var all = ((IHierarchical)proj).EnumerateAllChildren<ProjectItem>().ToArray();
        Assert.That(all.Length, Is.EqualTo(2));
        Assert.That(all.Contains(item1) && all.Contains(item2), Is.True);
    }
}
