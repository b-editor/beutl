using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ObjectRegeneratorTests
{
    [Test]
    public void Regenerate_AssignsNewId_OnTopLevelElement()
    {
        var src = new Element
        {
            Start = TimeSpan.FromSeconds(0),
            Length = TimeSpan.FromSeconds(1),
            ZIndex = 0,
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        Guid srcId = src.Id;

        ObjectRegenerator.Regenerate(src, out Element regenerated);

        Assert.That(regenerated.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(regenerated.Id, Is.Not.EqualTo(srcId));
    }

    [Test]
    public void Regenerate_AssignsNewIdsToEmbeddedChildren()
    {
        // Element has a Uri, so the default ReadWrite mode would emit only a file
        // reference and hide child Ids. Embed mode expands them so Ids get rewritten.
        var src = new Element
        {
            Start = TimeSpan.FromSeconds(0),
            Length = TimeSpan.FromSeconds(1),
            ZIndex = 0,
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        var child = new PortalObject();
        src.AddObject(child);
        Guid srcId = src.Id;
        Guid childId = child.Id;

        ObjectRegenerator.Regenerate(src, out Element regenerated);

        Assert.That(regenerated.Id, Is.Not.EqualTo(srcId));
        Assert.That(regenerated.Objects.Count, Is.EqualTo(1), "Embed mode should re-materialize child objects.");
        Guid regeneratedChildId = regenerated.Objects[0].Id;
        Assert.That(regeneratedChildId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(regeneratedChildId, Is.Not.EqualTo(childId));
    }

    [Test]
    public void Regenerate_Array_PreservesOrderAndAssignsNewIds()
    {
        // PlaceDuplicates relies on positional zip (sourceElements[i] -> newElements[i]).
        // If Regenerate reorders the array, group remap silently corrupts.
        var a = new Element
        {
            Start = TimeSpan.FromSeconds(0),
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        var b = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        var c = new Element
        {
            Start = TimeSpan.FromSeconds(10),
            Length = TimeSpan.FromSeconds(3),
            Uri = new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")),
        };
        Guid[] originalIds = [a.Id, b.Id, c.Id];

        ObjectRegenerator.Regenerate([a, b, c], out Element[] regenerated);

        Assert.That(regenerated.Length, Is.EqualTo(3));

        // Order preserved (identified by Start).
        Assert.That(regenerated[0].Start, Is.EqualTo(a.Start));
        Assert.That(regenerated[1].Start, Is.EqualTo(b.Start));
        Assert.That(regenerated[2].Start, Is.EqualTo(c.Start));

        // All Ids regenerated, no duplicates.
        for (int i = 0; i < regenerated.Length; i++)
        {
            Assert.That(regenerated[i].Id, Is.Not.EqualTo(originalIds[i]));
        }
        var idSet = new HashSet<Guid>(regenerated.Select(r => r.Id));
        Assert.That(idSet.Count, Is.EqualTo(3));
    }
}
