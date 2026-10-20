using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Beutl.Utilities;

public sealed class PooledArrayBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private const int ArrayMaxLength = 0x7FFFFFC7;

    private const int DefaultInitialBufferSize = 256;

    private readonly ArrayPool<T> _pool;
    private T[] _buffer;

    public PooledArrayBufferWriter(int initialCapacity, ArrayPool<T>? pool = null)
    {
        IsDisposed = true;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialCapacity, 0, nameof(initialCapacity));

        _pool = pool ?? ArrayPool<T>.Shared;
        _buffer = _pool.Rent(initialCapacity);
        WrittenCount = 0;
        IsDisposed = false;
    }

    [ExcludeFromCodeCoverage]
    ~PooledArrayBufferWriter()
    {
        Dispose();
    }

    public int WrittenCount { get; private set; }

    public int FreeCapacity => _buffer.Length - WrittenCount;

    public bool IsDisposed { get; private set; }

    private void Verify()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public void Advance(int count)
    {
        Verify();
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 0, nameof(count));

        if (WrittenCount > _buffer.Length - count)
            ThrowInvalidOperationException_AdvancedTooFar(_buffer.Length);

        WrittenCount += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        Verify();
        CheckAndResizeBuffer(sizeHint);
        Debug.Assert(_buffer.Length > WrittenCount);
        return _buffer.AsMemory(WrittenCount);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        Verify();
        CheckAndResizeBuffer(sizeHint);
        Debug.Assert(_buffer.Length > WrittenCount);
        return _buffer.AsSpan(WrittenCount);
    }

    public static T[] GetArray(PooledArrayBufferWriter<T> self) => self._buffer;

    private void CheckAndResizeBuffer(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeHint, 0, nameof(sizeHint));

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > FreeCapacity)
        {
            int currentLength = _buffer.Length;

            int growBy = Math.Max(sizeHint, currentLength);

            if (currentLength == 0)
            {
                growBy = Math.Max(growBy, DefaultInitialBufferSize);
            }

            int newSize = currentLength + growBy;

            if ((uint)newSize > int.MaxValue)
            {
                uint needed = (uint)(currentLength - FreeCapacity + sizeHint);
                Debug.Assert(needed > currentLength);

                if (needed > ArrayMaxLength)
                {
                    ThrowOutOfMemoryException(needed);
                }

                newSize = ArrayMaxLength;
            }

            Resize(ref _buffer, newSize, _pool);
        }

        Debug.Assert(FreeCapacity > 0 && FreeCapacity >= sizeHint);
    }

    private static void Resize(ref T[] array, int newSize, ArrayPool<T> pool)
    {
        T[] newArray = pool.Rent(newSize);

        array.AsSpan().CopyTo(newArray.AsSpan());

        pool.Return(array, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        array = newArray;
    }

    private static void ThrowInvalidOperationException_AdvancedTooFar(int capacity)
    {
        throw new InvalidOperationException($"Cannot advance past the end of the buffer, which has a size of {capacity}.");
    }

    private static void ThrowOutOfMemoryException(uint capacity)
    {
        throw new OutOfMemoryException($"Cannot allocate a buffer of size {capacity}.");
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _pool.Return(_buffer, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
            _buffer = [];

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
