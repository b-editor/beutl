using System.Collections.Specialized;
using Avalonia.Collections;
using Beutl.Collections;

namespace Beutl.UnitTests.Core;

// AvaloniaTypeConverter で行っている CoreList<T>.Move → AvaloniaList<T>.Move の同期処理が
// 正しいセマンティクスで動作することを検証する。
// GradientStopsSlider のドラッグで Stop が他のストップを横断する操作で発生していた
// ArgumentOutOfRangeException の回帰を防ぐ。
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

    [Test]
    public void Move_ForwardByOne_KeepsMirrorInSync()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C", "D");

        source.Move(0, 1);

        Assert.That(source, Is.EqualTo(new[] { "B", "A", "C", "D" }));
        Assert.That(mirror, Is.EqualTo(source));
    }

    [Test]
    public void Move_ForwardAcrossMiddle_KeepsMirrorInSync()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C", "D");

        source.Move(0, 2);

        Assert.That(source, Is.EqualTo(new[] { "B", "C", "A", "D" }));
        Assert.That(mirror, Is.EqualTo(source));
    }

    [Test]
    public void Move_ForwardToTail_KeepsMirrorInSync()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C", "D");

        source.Move(0, 3);

        Assert.That(source, Is.EqualTo(new[] { "B", "C", "D", "A" }));
        Assert.That(mirror, Is.EqualTo(source));
    }

    [Test]
    public void Move_BackwardByOne_KeepsMirrorInSync()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C", "D");

        source.Move(3, 2);

        Assert.That(source, Is.EqualTo(new[] { "A", "B", "D", "C" }));
        Assert.That(mirror, Is.EqualTo(source));
    }

    [Test]
    public void Move_BackwardToHead_KeepsMirrorInSync()
    {
        (CoreList<string> source, AvaloniaList<string> mirror) = BuildPair("A", "B", "C", "D");

        source.Move(3, 0);

        Assert.That(source, Is.EqualTo(new[] { "D", "A", "B", "C" }));
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
