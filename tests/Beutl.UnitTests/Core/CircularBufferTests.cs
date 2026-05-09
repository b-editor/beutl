using Beutl.Collections;

namespace Beutl.UnitTests.Core;

public class CircularBufferTests
{
    [Test]
    public void NewBuffer_IsEmptyAndNotFull()
    {
        var buf = new CircularBuffer<int>(3);
        Assert.That(buf.IsEmpty, Is.True);
        Assert.That(buf.IsFull, Is.False);
        Assert.That(buf.Size, Is.EqualTo(0));
        Assert.That(buf.Capacity, Is.EqualTo(3));
    }

    [Test]
    public void Constructor_CapacityLessThanOne_Throws()
    {
        Assert.That(() => new CircularBuffer<int>(0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Constructor_ItemsExceedCapacity_Throws()
    {
        Assert.That(() => new CircularBuffer<int>(2, [1, 2, 3]),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Constructor_WithItems_PopulatesBuffer()
    {
        var buf = new CircularBuffer<int>(5, [1, 2, 3]);
        Assert.That(buf.Size, Is.EqualTo(3));
        Assert.That(buf.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void PushBack_BelowCapacity_AppendsAtEnd()
    {
        var buf = new CircularBuffer<int>(3);
        buf.PushBack(1);
        buf.PushBack(2);
        Assert.That(buf.Front(), Is.EqualTo(1));
        Assert.That(buf.Back(), Is.EqualTo(2));
        Assert.That(buf.Size, Is.EqualTo(2));
    }

    [Test]
    public void PushBack_OverCapacity_OverwritesFront()
    {
        var buf = new CircularBuffer<int>(3);
        buf.PushBack(1);
        buf.PushBack(2);
        buf.PushBack(3);
        buf.PushBack(4);
        Assert.That(buf.IsFull, Is.True);
        Assert.That(buf.ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));
        Assert.That(buf.Front(), Is.EqualTo(2));
        Assert.That(buf.Back(), Is.EqualTo(4));
    }

    [Test]
    public void PushFront_BelowCapacity_PrependsAtStart()
    {
        var buf = new CircularBuffer<int>(3);
        buf.PushFront(1);
        buf.PushFront(2);
        Assert.That(buf.Front(), Is.EqualTo(2));
        Assert.That(buf.Back(), Is.EqualTo(1));
    }

    [Test]
    public void PushFront_OverCapacity_OverwritesBack()
    {
        var buf = new CircularBuffer<int>(3, [1, 2, 3]);
        buf.PushFront(0);
        Assert.That(buf.ToArray(), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void PopBack_ReducesSize_RemovesLastItem()
    {
        var buf = new CircularBuffer<int>(3, [1, 2, 3]);
        buf.PopBack();
        Assert.That(buf.Size, Is.EqualTo(2));
        Assert.That(buf.Back(), Is.EqualTo(2));
    }

    [Test]
    public void PopFront_ReducesSize_RemovesFirstItem()
    {
        var buf = new CircularBuffer<int>(3, [1, 2, 3]);
        buf.PopFront();
        Assert.That(buf.Size, Is.EqualTo(2));
        Assert.That(buf.Front(), Is.EqualTo(2));
    }

    [Test]
    public void PopFront_OnEmpty_Throws()
    {
        var buf = new CircularBuffer<int>(3);
        Assert.That(() => buf.PopFront(), Throws.InvalidOperationException);
        Assert.That(() => buf.PopBack(), Throws.InvalidOperationException);
        Assert.That(() => buf.Front(), Throws.InvalidOperationException);
        Assert.That(() => buf.Back(), Throws.InvalidOperationException);
    }

    [Test]
    public void Indexer_OnEmpty_Throws()
    {
        var buf = new CircularBuffer<int>(3);
        Assert.That(() => _ = buf[0], Throws.TypeOf<IndexOutOfRangeException>());
        Assert.That(() => buf[0] = 1, Throws.TypeOf<IndexOutOfRangeException>());
    }

    [Test]
    public void Indexer_OutOfRange_Throws()
    {
        var buf = new CircularBuffer<int>(3, [1, 2]);
        Assert.That(() => _ = buf[2], Throws.TypeOf<IndexOutOfRangeException>());
        Assert.That(() => buf[2] = 0, Throws.TypeOf<IndexOutOfRangeException>());
    }

    [Test]
    public void Indexer_GetAndSet_RoundTrips()
    {
        var buf = new CircularBuffer<int>(3, [1, 2, 3]);
        Assert.That(buf[0], Is.EqualTo(1));
        Assert.That(buf[1], Is.EqualTo(2));
        Assert.That(buf[2], Is.EqualTo(3));
        buf[1] = 9;
        Assert.That(buf[1], Is.EqualTo(9));
    }

    [Test]
    public void Clear_ResetsSize_KeepsCapacity()
    {
        var buf = new CircularBuffer<int>(3, [1, 2, 3]);
        buf.Clear();
        Assert.That(buf.IsEmpty, Is.True);
        Assert.That(buf.Capacity, Is.EqualTo(3));
    }

    [Test]
    public void Enumerator_AfterWrap_ReturnsLogicalOrder()
    {
        var buf = new CircularBuffer<int>(3);
        buf.PushBack(1);
        buf.PushBack(2);
        buf.PushBack(3);
        buf.PushBack(4); // wraps; expected [2, 3, 4]
        Assert.That(buf.ToList(), Is.EqualTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public void ToArraySegments_HasTwoSegments()
    {
        var buf = new CircularBuffer<int>(3);
        buf.PushBack(1);
        buf.PushBack(2);
        buf.PushBack(3);
        buf.PushBack(4); // wraps internally

        IList<ArraySegment<int>> segs = buf.ToArraySegments();
        Assert.That(segs, Has.Count.EqualTo(2));
        int total = 0;
        foreach (ArraySegment<int> seg in segs)
        {
            total += seg.Count;
        }
        Assert.That(total, Is.EqualTo(buf.Size));
    }
}
