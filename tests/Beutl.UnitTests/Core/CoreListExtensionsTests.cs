using Beutl.Collections;

namespace Beutl.UnitTests.Core;

public class CoreListExtensionsTests
{
    [Test]
    public void ForEachItem_AppliesAddedToInitialItems()
    {
        var list = new CoreList<int> { 1, 2, 3 };
        var seen = new List<(int Index, int Value)>();
        var removed = new List<int>();
        bool resetCalled = false;

        using var sub = list.ForEachItem(
            (i, v) => seen.Add((i, v)),
            (_, v) => removed.Add(v),
            () => resetCalled = true);

        Assert.That(seen, Is.EqualTo(new[] { (0, 1), (1, 2), (2, 3) }));
        Assert.That(removed, Is.Empty);
        Assert.That(resetCalled, Is.False);
    }

    [Test]
    public void ForEachItem_ReceivesAddRemoveAndReplaceNotifications()
    {
        var list = new CoreList<string>();
        var added = new List<(int Index, string Value)>();
        var removed = new List<(int Index, string Value)>();

        using var sub = list.ForEachItem<string>(
            (i, v) => added.Add((i, v)),
            (i, v) => removed.Add((i, v)),
            () => { });

        list.Add("a");
        list.Add("b");
        list[0] = "z";
        list.RemoveAt(1);

        Assert.That(added, Does.Contain((0, "a")));
        Assert.That(added, Does.Contain((1, "b")));
        Assert.That(added, Does.Contain((0, "z")));
        Assert.That(removed, Does.Contain((0, "a")));
        Assert.That(removed, Does.Contain((1, "b")));
    }

    [Test]
    public void ForEachItem_OnReset_FiresResetAndReAdds()
    {
        var list = new CoreList<int> { 1, 2 };
        var added = new List<int>();
        bool reset = false;

        using var sub = list.ForEachItem<int>(
            (_, v) => added.Add(v),
            (_, _) => { },
            () => reset = true);

        added.Clear();
        list.Clear();

        Assert.That(reset, Is.True);
        Assert.That(added, Is.Empty);
    }

    [Test]
    public void ForEachItem_DisposeStopsNotifications()
    {
        var list = new CoreList<int>();
        var added = new List<int>();

        var sub = list.ForEachItem<int>(
            (_, v) => added.Add(v),
            (_, _) => { },
            () => { });

        list.Add(1);
        sub.Dispose();
        list.Add(2);

        Assert.That(added, Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void TrackCollectionChanged_DoesNotEmitInitialItems()
    {
        var list = new CoreList<int> { 1, 2 };
        var added = new List<int>();

        using var sub = list.TrackCollectionChanged(
            v => added.Add(v),
            _ => { },
            () => { });

        Assert.That(added, Is.Empty);

        list.Add(99);
        Assert.That(added, Is.EqualTo(new[] { 99 }));
    }
}
