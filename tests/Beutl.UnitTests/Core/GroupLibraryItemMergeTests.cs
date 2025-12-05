using System;
using Beutl.Services;

namespace Beutl.UnitTests.Core;

public class GroupLibraryItemMergeTests
{
    private sealed class Dummy : CoreObject { }

    [Test]
    public void Merge_GroupsWithSameName_CombinesItems()
    {
        var g1 = new GroupLibraryItem("Root");
        g1.Add<Dummy>(KnownLibraryItemFormats.Drawable, "D1");
        g1.AddGroup("Sub", gg => gg.Add<Dummy>(KnownLibraryItemFormats.Sound, "S1"));

        var g2 = new GroupLibraryItem("Root");
        g2.Add<Dummy>(KnownLibraryItemFormats.Geometry, "G1");
        g2.AddGroup("Sub", gg => gg.Add<Dummy>(KnownLibraryItemFormats.SourceOperator, "SO1"));

        g1.Merge(g2);

        // g1 should contain both D1/G1 under root and both S1/SO1 under Sub
        Assert.That(g1.Items.Count, Is.EqualTo(3)); // D1, G1, Sub
        var sub = (GroupLibraryItem)g1.Items[2];
        Assert.That(sub.Items.Count, Is.EqualTo(2));
    }
}

