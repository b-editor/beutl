using System.Buffers;
using System.Collections;

namespace BeUtl.Collections;

public struct PooledArray<T> : IDisposable, IEnumerable<T>, ICloneable
{
    private readonly T[] _array;

    public PooledArray(int length)
    {
        _array = ArrayPool<T>.Shared.Rent(length);
        Length = length;
        IsDisposed = false;
    }

    public int Length { get; }

    public bool IsDisposed { get; private set; }

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

    public object Clone()
    {
        ThrowIfDisposed();
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            ArrayPool<T>.Shared.Return(_array, true);
            IsDisposed = true;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        ThrowIfDisposed();
        return new ArrayEnumerator(this);
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
        ThrowIfDisposed();
        return _array.GetEnumerator();
    }

    private sealed class ArrayEnumerator : IEnumerator<T>, ICloneable
    {
        private PooledArray<T> _array;
        private int _index;
        private T? _currentElement;

        public ArrayEnumerator(PooledArray<T> array)
        {
            _array = array;
            _index = -1;
        }

        public T Current
        {
            get
            {
                if (_index == -1)
                    throw new InvalidOperationException();
                if (_index >= _array!.Length)
                    throw new InvalidOperationException();
                return _currentElement!;
            }
        }

        object? IEnumerator.Current => Current;

        public object Clone()
        {
            return MemberwiseClone();
        }

        public bool MoveNext()
        {
            if (_index < (_array!.Length - 1))
            {
                _index++;
                _currentElement = _array[_index];
                return true;
            }
            else
            {
                _index = _array.Length;
            }

            return false;
        }

        public void Dispose()
        {
            _index = _array.Length;
            _array = default;
        }

        public void Reset()
        {
            _currentElement = default;
            _index = -1;
        }
    }
}
