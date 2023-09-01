using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Beutl.Utilities;

public sealed class PooledArrayBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private const int ArrayMaxLength = 0x7FFFFFC7;

    private const int DefaultInitialBufferSize = 256;

    private readonly ArrayPool<T> _pool = ArrayPool<T>.Shared;
    private T[] _buffer;
    private int _index;


    public PooledArrayBufferWriter(ArrayPool<T>? pool = null)
    {
        _pool = pool ?? ArrayPool<T>.Shared;
        _buffer = _pool.Rent(0);
        _index = 0;
    }

    public PooledArrayBufferWriter(int initialCapacity, ArrayPool<T>? pool = null)
    {
        if (initialCapacity <= 0)
            throw new ArgumentException(null, nameof(initialCapacity));

        _pool = pool ?? ArrayPool<T>.Shared;
        _buffer = _pool.Rent(initialCapacity);
        _index = 0;
    }

    ~PooledArrayBufferWriter()
    {
        Dispose();
    }

    public ReadOnlyMemory<T> WrittenMemory
    {
        get
        {
            Verify();
            return _buffer.AsMemory(0, _index);
        }
    }

    public ReadOnlySpan<T> WrittenSpan
    {
        get
        {
            Verify();
            return _buffer.AsSpan(0, _index);
        }
    }

    public int WrittenCount => _index;

    public int Capacity => _buffer.Length;

    public int FreeCapacity => _buffer.Length - _index;

    public bool IsDisposed { get; private set; }

    private void Verify()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(PooledArrayBufferWriter<T>));
    }

    public void Clear()
    {
        Verify();
        Debug.Assert(_buffer.Length >= _index);
        _buffer.AsSpan(0, _index).Clear();
        _index = 0;
    }

    public void Advance(int count)
    {
        Verify();
        if (count < 0)
            throw new ArgumentException(null, nameof(count));

        if (_index > _buffer.Length - count)
            ThrowInvalidOperationException_AdvancedTooFar(_buffer.Length);

        _index += count;
    }

    public Memory<T> GetMemory(int sizeHint = 0)
    {
        Verify();
        CheckAndResizeBuffer(sizeHint);
        Debug.Assert(_buffer.Length > _index);
        return _buffer.AsMemory(_index);
    }

    public Span<T> GetSpan(int sizeHint = 0)
    {
        Verify();
        CheckAndResizeBuffer(sizeHint);
        Debug.Assert(_buffer.Length > _index);
        return _buffer.AsSpan(_index);
    }

    public static T[] GetArray(PooledArrayBufferWriter<T> self) => self._buffer;

    private void CheckAndResizeBuffer(int sizeHint)
    {
        if (sizeHint < 0)
            throw new ArgumentException(nameof(sizeHint));

        if (sizeHint == 0)
        {
            sizeHint = 1;
        }

        if (sizeHint > FreeCapacity)
        {
            int currentLength = _buffer.Length;

            // Attempt to grow by the larger of the sizeHint and double the current size.
            int growBy = Math.Max(sizeHint, currentLength);

            if (currentLength == 0)
            {
                growBy = Math.Max(growBy, DefaultInitialBufferSize);
            }

            int newSize = currentLength + growBy;

            if ((uint)newSize > int.MaxValue)
            {
                // Attempt to grow to ArrayMaxLength.
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
            _buffer = Array.Empty<T>();

            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
