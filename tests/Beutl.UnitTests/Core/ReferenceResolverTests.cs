using System;
using System.Threading.Tasks;

namespace Beutl.UnitTests.Core;

public class ReferenceResolverTests
{
    private sealed class DummyItem : ProjectItem {}

    [Test]
    public async Task Resolve_WaitsForRootAndDescendantAttach()
    {
        var app = BeutlApplication.Current;
        var proj = new Project();
        app.Project = proj;

        // Anchor not attached yet
        var anchor = new DummyItem { FileName = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "anchor.item") };
        Guid targetId = Guid.NewGuid();
        var resolver = new ReferenceResolver(anchor, targetId);
        var task = resolver.Resolve();

        // Attach anchor to root
        proj.Items.Add(anchor);

        // Add target later => should complete
        var target = new DummyItem { FileName = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "target.item") };
        target.Id = targetId;
        proj.Items.Add(target);

        var resolved = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2))) == task
            ? await task
            : throw new TimeoutException("Resolver did not complete in time");

        Assert.That(resolved.Id, Is.EqualTo(targetId));
    }
}

