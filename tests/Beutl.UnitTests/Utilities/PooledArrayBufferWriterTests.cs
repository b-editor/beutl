using Beutl.Utilities;

namespace Beutl.UnitTests.Utilities;

public class PooledArrayBufferWriterTests
{
    [Test]
    public void Advance_ShouldIncreaseWrittenCount()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        writer.Advance(10);
        Assert.That(writer.WrittenCount, Is.EqualTo(10));
    }

    [Test]
    public void Advance_ShouldThrowArgumentOutOfRangeExceptionForNegativeCount()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        Assert.That(() => writer.Advance(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Advance_ShouldThrowInvalidOperationExceptionWhenAdvancingTooFar()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        writer.Advance(5);
        Assert.That(() => writer.Advance(writer.FreeCapacity + 1), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void GetMemory_ShouldReturnMemoryWithSufficientCapacity()
    {
        using var writer = new PooledArrayBufferWriter<int>();
        var memory = writer.GetMemory(5);
        Assert.That(memory.Length, Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void GetMemory_ShouldThrowOutOfMemoryExceptionForExcessiveSizeHint()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        Assert.That(() => writer.GetMemory(int.MaxValue), Throws.TypeOf<OutOfMemoryException>());
    }

    [Test]
    public void GetSpan_ShouldReturnSpanWithSufficientCapacity()
    {
        using var writer = new PooledArrayBufferWriter<int>();
        var span = writer.GetSpan(5);
        Assert.That(span.Length, Is.GreaterThanOrEqualTo(5));
    }

    [Test]
    public void GetSpan_ShouldThrowOutOfMemoryExceptionForExcessiveSizeHint()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        Assert.That(() => writer.GetSpan(int.MaxValue), Throws.TypeOf<OutOfMemoryException>());
    }

    [Test]
    public void Clear_ShouldResetWrittenCount()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        writer.Advance(10);
        writer.Clear();
        Assert.That(writer.WrittenCount, Is.EqualTo(0));
    }

    [Test]
    public void Dispose_ShouldMarkAsDisposed()
    {
        var writer = new PooledArrayBufferWriter<int>();
        writer.Dispose();
        Assert.That(writer.IsDisposed, Is.True);
    }

    [Test]
    public void Dispose_ShouldThrowObjectDisposedExceptionWhenAccessingAfterDispose()
    {
        var writer = new PooledArrayBufferWriter<int>();
        writer.Dispose();
        Assert.That(() => writer.Advance(1), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Constructor_ShouldThrowArgumentOutOfRangeExceptionForNegativeInitialCapacity()
    {
        Assert.That(() => new PooledArrayBufferWriter<int>(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void WrittenMemory_ShouldReturnCorrectMemory()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        writer.Advance(10);
        var memory = writer.WrittenMemory;
        Assert.That(memory.Length, Is.EqualTo(10));
    }

    [Test]
    public void WrittenSpan_ShouldReturnCorrectSpan()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        writer.Advance(10);
        var span = writer.WrittenSpan;
        Assert.That(span.Length, Is.EqualTo(10));
    }

    [Test]
    public void GetArray_ShouldReturnInternalBuffer()
    {
        using var writer = new PooledArrayBufferWriter<int>();
        int[] buffer = PooledArrayBufferWriter<int>.GetArray(writer);
        Assert.That(buffer, Is.Not.Null);
    }

    [Test]
    public void GetArray_ShouldReturnSameBufferAfterAdvance()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        int[] initialBuffer = PooledArrayBufferWriter<int>.GetArray(writer);
        writer.Advance(10);
        int[] bufferAfterAdvance = PooledArrayBufferWriter<int>.GetArray(writer);
        Assert.That(bufferAfterAdvance, Is.SameAs(initialBuffer));
    }

    [Test]
    public void GetArray_ShouldReturnSameBufferAfterClear()
    {
        using var writer = new PooledArrayBufferWriter<int>(10);
        int[] initialBuffer = PooledArrayBufferWriter<int>.GetArray(writer);
        writer.Clear();
        int[] bufferAfterClear = PooledArrayBufferWriter<int>.GetArray(writer);
        Assert.That(bufferAfterClear, Is.SameAs(initialBuffer));
    }

    [Test]
    public void GetArray_ShouldReturnDifferentBufferAfterResize()
    {
        using var writer = new PooledArrayBufferWriter<int>(1);
        int[] initialBuffer = PooledArrayBufferWriter<int>.GetArray(writer);
        writer.GetMemory(100);
        int[] bufferAfterResize = PooledArrayBufferWriter<int>.GetArray(writer);
        Assert.That(bufferAfterResize, Is.Not.SameAs(initialBuffer));
    }
}
