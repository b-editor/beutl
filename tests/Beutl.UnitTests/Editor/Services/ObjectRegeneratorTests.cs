using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using NUnit.Framework.Legacy;

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

        ClassicAssert.AreNotEqual(Guid.Empty, regenerated.Id);
        ClassicAssert.AreNotEqual(srcId, regenerated.Id);
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

        ClassicAssert.AreNotEqual(srcId, regenerated.Id);
        ClassicAssert.AreEqual(1, regenerated.Objects.Count, "Embed mode should re-materialize child objects.");
        Guid regeneratedChildId = regenerated.Objects[0].Id;
        ClassicAssert.AreNotEqual(Guid.Empty, regeneratedChildId);
        ClassicAssert.AreNotEqual(childId, regeneratedChildId);
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

        ClassicAssert.AreEqual(3, regenerated.Length);

        // Order preserved (identified by Start).
        ClassicAssert.AreEqual(a.Start, regenerated[0].Start);
        ClassicAssert.AreEqual(b.Start, regenerated[1].Start);
        ClassicAssert.AreEqual(c.Start, regenerated[2].Start);

        // All Ids regenerated, no duplicates.
        for (int i = 0; i < regenerated.Length; i++)
        {
            ClassicAssert.AreNotEqual(originalIds[i], regenerated[i].Id);
        }
        var idSet = new HashSet<Guid>(regenerated.Select(r => r.Id));
        ClassicAssert.AreEqual(3, idSet.Count);
    }
}
