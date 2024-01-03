using Beutl.Media;

using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Beutl.Graphics.UnitTests;

public class TimeRangeTests
{
    [Test]
    public void Contains()
    {
        // range1: #######---
        // range2: --###-----
        // range3: -----#####
        var range1 = TimeRange.FromSeconds(7);
        var range2 = TimeRange.FromSeconds(2, 3);
        var range3 = TimeRange.FromSeconds(5, 5);

        ClassicAssert.AreEqual(true, range1.Contains(range2));
        ClassicAssert.AreEqual(false, range2.Contains(range3));
        ClassicAssert.AreEqual(false, range1.Contains(TimeSpan.FromSeconds(7)));
    }
    
    [Test]
    public void Union()
    {
        // range1: #####-----
        // range2: --#####---
        // range3: -----#####
        var range1 = TimeRange.FromSeconds(5);
        var range2 = TimeRange.FromSeconds(2, 5);
        var range3 = TimeRange.FromSeconds(5, 5);

        ClassicAssert.AreEqual(TimeRange.FromSeconds(7), range1.Union(range2));
        ClassicAssert.AreEqual(TimeRange.FromSeconds(2, 8), range2.Union(range3));
    }
    
    [Test]
    public void Intersect()
    {
        // range1: #####-----
        // range2: --#####---
        // range3: -----#####
        var range1 = TimeRange.FromSeconds(5);
        var range2 = TimeRange.FromSeconds(2, 5);
        var range3 = TimeRange.FromSeconds(5, 5);

        ClassicAssert.AreEqual(TimeRange.FromSeconds(2, 3), range1.Intersect(range2));
        ClassicAssert.AreEqual(TimeRange.FromSeconds(5, 2), range2.Intersect(range3));
    }

    [Test]
    public void Intersects()
    {
        // range1: #####-----
        // range2: --#####---
        // range3: -----#####
        var range1 = TimeRange.FromSeconds(5);
        var range2 = TimeRange.FromSeconds(2, 5);
        var range3 = TimeRange.FromSeconds(5, 5);

        ClassicAssert.AreEqual(true, range1.Intersects(range2));
        ClassicAssert.AreEqual(true, range2.Intersects(range1));
        ClassicAssert.AreEqual(false, range1.Intersects(range3));
        ClassicAssert.AreEqual(false, range3.Intersects(range1));
    }
}
