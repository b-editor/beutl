using Beutl.Collections;

namespace Beutl.UnitTests.Core;

public class PooledArrayTests
{
    [Test]
    public void Constructor_AllocatesPooledLength()
    {
        using var array = new PooledArray<int>(8);
        Assert.Multiple(() =>
        {
            Assert.That(array.Length, Is.EqualTo(8));
            Assert.That(array.IsDisposed, Is.False);
            Assert.That(array.Span.Length, Is.EqualTo(8));
            Assert.That(array.Array.Length, Is.GreaterThanOrEqualTo(8));
        });
    }

    [Test]
    public void Indexer_AllowsReadAndWrite()
    {
        using var array = new PooledArray<int>(3);
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = i + 1;
        }

        Assert.Multiple(() =>
        {
            Assert.That(array[0], Is.EqualTo(1));
            Assert.That(array[1], Is.EqualTo(2));
            Assert.That(array[2], Is.EqualTo(3));
        });
    }

    [Test]
    public void Indexer_OutOfRange_Throws()
    {
        using var array = new PooledArray<int>(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = array[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = array[2]);
    }

    [Test]
    public void Enumerator_IteratesAllElements()
    {
        using var array = new PooledArray<int>(3);
        array[0] = 10;
        array[1] = 20;
        array[2] = 30;

        var collected = new List<int>();
        foreach (int item in array)
        {
            collected.Add(item);
        }

        Assert.That(collected, Is.EqualTo(new[] { 10, 20, 30 }));
    }

    [Test]
    public void Dispose_TwiceIsSafe_AndBlocksAccess()
    {
        var array = new PooledArray<int>(4);
        array.Dispose();
        Assert.That(array.IsDisposed, Is.True);

        // Calling Dispose again must not throw.
        array.Dispose();
        Assert.That(array.IsDisposed, Is.True);

        Assert.Throws<ObjectDisposedException>(() => _ = array.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = array.Array);
    }

    [Test]
    public void ToPooledArray_FromIList_CopiesElements()
    {
        var source = new List<int> { 1, 2, 3 };
        using PooledArray<int> array = source.ToPooledArray();

        Assert.That(array.Length, Is.EqualTo(3));
        Assert.That(array.Span.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void ToPooledArray_FromEnumerable_CopiesElements()
    {
        IEnumerable<int> source = NumbersOneToFive();
        using PooledArray<int> array = source.ToPooledArray();

        Assert.That(array.Length, Is.EqualTo(5));
        Assert.That(array.Span.ToArray(), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));

        static IEnumerable<int> NumbersOneToFive()
        {
            for (int i = 1; i <= 5; i++)
            {
                yield return i;
            }
        }
    }
}
