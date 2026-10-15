using System.Buffers;
using System.Collections;

namespace Beutl.Collections;

public struct PooledArray<T> : IDisposable, IEnumerable<T>
{
    internal readonly T[] _array;

    public PooledArray(int length)
    {
        _array = ArrayPool<T>.Shared.Rent(length);
        Length = length;
        IsDisposed = false;
    }

    public int Length { get; }

    public bool IsDisposed { get; private set; }

    public Span<T> Span
    {
        get
        {
            ThrowIfDisposed();
            return _array.AsSpan(0, Length);
        }
    }

    public T[] Array
    {
        get
        {
            ThrowIfDisposed();
            return _array;
        }
    }

    public ref T this[int index]
    {
        get
        {
            ThrowIfDisposed();
            if (index is < 0 || index >= Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return ref _array[index];
        }
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            ArrayPool<T>.Shared.Return(_array, true);
            IsDisposed = true;
        }
    }

    public ArrayEnumerator GetEnumerator()
    {
        ThrowIfDisposed();
        return new ArrayEnumerator(this);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(PooledArray<T>));
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public struct ArrayEnumerator(PooledArray<T> array) : IEnumerator<T>
    {
        private int _index = -1;

        public ref T Current
        {
            get
            {
                if (_index == -1)
                    throw new InvalidOperationException();
                if (_index >= array.Length)
                    throw new InvalidOperationException();
                return ref array[_index];
            }
        }

        T IEnumerator<T>.Current => Current;

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index < (array.Length - 1))
            {
                _index++;
                return true;
            }
            else
            {
                _index = array.Length;
            }

            return false;
        }

        public void Dispose()
        {
            _index = array.Length;
            array = default;
        }

        public void Reset()
        {
            _index = -1;
        }
    }
}
