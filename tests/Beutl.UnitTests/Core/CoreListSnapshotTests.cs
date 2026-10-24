using System.Collections;
using System.Collections.Specialized;
using Beutl.Collections;

namespace Beutl.UnitTests.Core;

public class CoreListSnapshotTests
{
    public enum EnumerationKind
    {
        Concrete,
        Generic,
        NonGeneric,
    }

    private static IEnumerable<TestCaseData> Mutations()
    {
        (string Name, Action<CoreList<int>> Mutate, int[] Expected)[] mutations =
        [
            ("Add", list => list.Add(4), [1, 2, 3, 4]),
            ("Insert", list => list.Insert(1, 4), [1, 4, 2, 3]),
            ("Set", list => list[1] = 4, [1, 4, 3]),
            ("Remove", list => list.Remove(2), [1, 3]),
            ("RemoveAt", list => list.RemoveAt(0), [2, 3]),
            ("RemoveRange", list => list.RemoveRange(1, 2), [1]),
            ("RemoveAll", list => list.RemoveAll([1, 3]), [2]),
            ("Clear", list => list.Clear(), []),
            ("Replace", list => list.Replace([4, 5]), [4, 5]),
            ("Move", list => list.Move(0, 2), [2, 3, 1]),
            ("MoveRange", list => list.MoveRange(0, 2, 3), [3, 1, 2]),
            ("AddArray", list => list.AddRange(new[] { 4, 5 }), [1, 2, 3, 4, 5]),
            ("AddSpan", list => list.AddRange((ReadOnlySpan<int>)[4, 5]), [1, 2, 3, 4, 5]),
            ("AddCollection", list => list.AddRange((IEnumerable<int>)new List<int> { 4, 5 }), [1, 2, 3, 4, 5]),
            ("AddEnumerable", list => list.AddRange(Enumerate(4, 5)), [1, 2, 3, 4, 5]),
            ("InsertArray", list => list.InsertRange(1, new[] { 4, 5 }), [1, 4, 5, 2, 3]),
            ("InsertSpan", list => list.InsertRange(1, (ReadOnlySpan<int>)[4, 5]), [1, 4, 5, 2, 3]),
            ("InsertCollection", list => list.InsertRange(1, (IEnumerable<int>)new List<int> { 4, 5 }), [1, 4, 5, 2, 3]),
            ("InsertEnumerable", list => list.InsertRange(1, Enumerate(4, 5)), [1, 4, 5, 2, 3]),
        ];

        foreach (var mutation in mutations)
            foreach (EnumerationKind kind in Enum.GetValues<EnumerationKind>())
                yield return new TestCaseData(mutation.Mutate, mutation.Expected, kind)
                    .SetName($"EnumerationSurvivesWriter_{mutation.Name}_{kind}");
    }

    [TestCaseSource(nameof(Mutations))]
    public async Task EnumerationSurvivesWriter(Action<CoreList<int>> mutate, int[] expected, EnumerationKind kind)
    {
        var list = new CoreList<int>([1, 2, 3]);
        IEnumerator enumerator = kind switch
        {
            EnumerationKind.Concrete => list.GetEnumerator(),
            EnumerationKind.Generic => ((IEnumerable<int>)list).GetEnumerator(),
            _ => ((IEnumerable)list).GetEnumerator(),
        };
        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(1));

        await Task.Run(() => mutate(list));

        Assert.Multiple(() =>
        {
            Assert.That(ReadRemaining(enumerator), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(ReadRemaining(list.GetEnumerator()), Is.EqualTo(expected));
            Assert.That(list.AsSpan().ToArray(), Is.EqualTo(expected));
        });
    }

    [Test]
    public void SpanAndListPropertyEnumerationKeepTheirSnapshot()
    {
        var property = new ListProperty<int>();
        property.AddRange([1, 2, 3]);
        ReadOnlySpan<int> snapshot = property.AsSpan();
        var enumerator = property.GetEnumerator();

        property.Replace([4]);

        Assert.That(snapshot.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(ReadRemaining(enumerator), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(ReadRemaining(property.GetEnumerator()), Is.EqualTo(new[] { 4 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ThrowingEnumerablePublishesCommittedPrefix(bool observe)
    {
        var list = new CoreList<int>([1]);
        var before = list.GetEnumerator();
        int[]? notified = null;
        if (observe)
            list.CollectionChanged += (_, _) => notified = ReadRemaining(list.GetEnumerator());

        Assert.Throws<InvalidOperationException>(() => list.AddRange(FailingSource()));

        Assert.That(ReadRemaining(before), Is.EqualTo(new[] { 1 }));
        Assert.That(ReadRemaining(list.GetEnumerator()), Is.EqualTo(new[] { 1, 2 }));
        if (observe) Assert.That(notified, Is.EqualTo(new[] { 1, 2 }));

        static IEnumerable<int> FailingSource()
        {
            yield return 2;
            throw new InvalidOperationException("Source failed.");
        }
    }

    [Test]
    public void ReentrantNotificationPublishesLatestEditAndPreservesBothReaders()
    {
        var list = new CoreList<int>([1]);
        var before = list.GetEnumerator();
        CoreList<int>.Enumerator during = default;
        list.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                during = list.GetEnumerator();
                list.Clear();
            }
        };

        list.Add(2);

        Assert.Multiple(() =>
        {
            Assert.That(ReadRemaining(before), Is.EqualTo(new[] { 1 }));
            Assert.That(ReadRemaining(during), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ReadRemaining(list.GetEnumerator()), Is.Empty);
        });
    }

    [Test]
    public void ThrowingAttachmentDoesNotHideThePublishedEdit()
    {
        var list = new CoreList<int>([1]);
        var before = list.GetEnumerator();
        list.Attached += _ => throw new InvalidOperationException("Attachment failed.");

        Assert.Throws<InvalidOperationException>(() => list.Add(2));

        Assert.That(ReadRemaining(before), Is.EqualTo(new[] { 1 }));
        Assert.That(ReadRemaining(list.GetEnumerator()), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void EnumeratorResetReplaysItsSnapshot()
    {
        var list = new CoreList<int>([1, 2]);
        IEnumerator enumerator = ((IEnumerable)list).GetEnumerator();
        Assert.That(ReadRemaining(enumerator), Is.EqualTo(new[] { 1, 2 }));
        list.Clear();

        enumerator.Reset();

        Assert.That(ReadRemaining(enumerator), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void DerivedMutatorPublishesBeforeNotifyingItsCaller()
    {
        var list = new DerivedList();
        list.Add(1);
        var before = list.GetEnumerator();

        list.Append(2);

        Assert.That(ReadRemaining(before), Is.EqualTo(new[] { 1 }));
        Assert.That(ReadRemaining(list.GetEnumerator()), Is.EqualTo(new[] { 1, 2 }));
    }

    private static int[] ReadRemaining(IEnumerator enumerator)
    {
        var items = new List<int>();
        while (enumerator.MoveNext()) items.Add((int)enumerator.Current!);
        (enumerator as IDisposable)?.Dispose();
        return items.ToArray();
    }

    private static IEnumerable<int> Enumerate(int first, int second)
    {
        yield return first;
        yield return second;
    }

    private sealed class DerivedList : CoreList<int>
    {
        public void Append(int item)
        {
            Inner.Add(item);
            PublishSnapshot();
        }
    }
}
