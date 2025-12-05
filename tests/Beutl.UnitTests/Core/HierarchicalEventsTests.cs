using System;

namespace Beutl.UnitTests.Core;

public class HierarchicalEventsTests
{
    private sealed class DummyItem : ProjectItem {}

    [Test]
    public void SettingProject_RaisesAttachAndDetach()
    {
        var app = BeutlApplication.Current;
        var proj1 = new Project();
        var proj2 = new Project();
        int attached = 0;
        int detached = 0;
        app.DescendantAttached += (_, __) => attached++;
        app.DescendantDetached += (_, __) => detached++;

        app.Project = proj1;
        Assert.That(attached, Is.GreaterThanOrEqualTo(1));

        app.Project = proj2;
        Assert.That(detached, Is.GreaterThanOrEqualTo(1));
        Assert.That(attached, Is.GreaterThanOrEqualTo(2));

        app.Project = null;
        Assert.That(detached, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void AddingProjectItem_RaisesAttach_Detach()
    {
        var app = BeutlApplication.Current;
        var proj = new Project();
        app.Project = proj;
        int attached = 0;
        int detached = 0;
        app.DescendantAttached += (_, __) => attached++;
        app.DescendantDetached += (_, __) => detached++;

        var item = new DummyItem { FileName = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "dummy.item") };
        proj.Items.Add(item);
        Assert.That(attached, Is.GreaterThanOrEqualTo(1));

        proj.Items.Remove(item);
        Assert.That(detached, Is.GreaterThanOrEqualTo(1));
    }
}

