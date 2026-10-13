using System.Collections.Specialized;
using Avalonia.Collections;
using Beutl.Collections;

namespace Beutl.UnitTests.Core;

// CoreList.Move の添字が AvaloniaList.Move と同じ意味であることを確かめる
// （GradientStopsSlider のドラッグで起きていた ArgumentOutOfRangeException の回帰）。
[TestFixture]
public class CoreListAvaloniaSyncTests
{
    private static (CoreList<string> Source, AvaloniaList<string> Mirror) BuildPair(params string[] items)
    {
        var source = new CoreList<string>();
        foreach (string s in items)
        {
            source.Add(s);
        }

        var mirror = new AvaloniaList<string>(items);

        source.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Move:
                    if (e.OldStartingIndex >= 0
                        && e.OldStartingIndex < mirror.Count
                        && e.NewStartingIndex >= 0
                        && e.NewStartingIndex < mirror.Count
                        && e.OldStartingIndex != e.NewStartingIndex
                        && e.OldItems is { Count: 1 })
                    {
                        mirror.Move(e.OldStartingIndex, e.NewStartingIndex);
                    }
                    break;
            }
        };

        return (source, mirror);
    }

    // The mirror replays the Move event's indices with AvaloniaList.Move.
    [TestCase(0, 1, new[] { "B", "A", "C", "D" })]
    [TestCase(0, 2, new[] { "B", "C", "A", "D" })]
    [TestCase(0, 3, new[] { "B", "C", "D", "A" })]
    [TestCase(3, 2, new[] { "A", "B", "D", "C" })]
    [TestCase(3, 0, new[] { "D", "A", "B", "C" })]
    public void Move_TakesTheIndicesAvaloniaListMoveTakes(int from, int to, string[] expected)
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C", "D");

        source.Move(from, to);

        Assert.That(source, Is.EqualTo(expected));
        Assert.That(mirror, Is.EqualTo(source));
    }

    [Test]
    public void Move_TwoElementCollection_BothDirections()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B");

        source.Move(0, 1);
        Assert.That(mirror, Is.EqualTo(source));

        source.Move(1, 0);
        Assert.That(mirror, Is.EqualTo(source));
    }

    [Test]
    public void Move_TailToTail_DoesNotThrow()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C");

        Assert.DoesNotThrow(() => source.Move(2, 2));
        Assert.That(mirror, Is.EqualTo(source));
    }

    // Stop が連続して隣のストップを横断するシナリオの再現。
    // ドラッグ中の OnGradientStopChanged の連続 list.Move を模倣している。
    [Test]
    public void Move_ConsecutiveSwaps_AcrossAllStops_KeepsMirrorInSync()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("Drag", "B", "C", "D");

        source.Move(0, 1);
        Assert.That(mirror, Is.EqualTo(source));

        source.Move(1, 2);
        Assert.That(mirror, Is.EqualTo(source));

        source.Move(2, 3);
        Assert.That(mirror, Is.EqualTo(source));

        Assert.That(source, Is.EqualTo(new[] { "B", "C", "D", "Drag" }));
    }

    [Test]
    public void Move_ConsecutiveSwapsBackwards_KeepsMirrorInSync()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C", "Drag");

        source.Move(3, 2);
        Assert.That(mirror, Is.EqualTo(source));

        source.Move(2, 1);
        Assert.That(mirror, Is.EqualTo(source));

        source.Move(1, 0);
        Assert.That(mirror, Is.EqualTo(source));

        Assert.That(source, Is.EqualTo(new[] { "Drag", "A", "B", "C" }));
    }
}
