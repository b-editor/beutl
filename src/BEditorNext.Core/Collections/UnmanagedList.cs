using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace BEditorNext.Collections;

#pragma warning disable CA2208

public sealed class UnmanagedList<T> : IList<T>, IReadOnlyList<T>, IDisposable
    where T : unmanaged
{
    private const int DefaultCapacity = 4;
    private UnmanagedArray<T> _items;
    private int _version;

    public UnmanagedList()
    {
        _items = new(DefaultCapacity);
    }

    public UnmanagedList(int capacity)
    {
        _items = new(capacity);
    }

    public UnmanagedList(IEnumerable<T> collection)
    {
        if (collection is null) throw new ArgumentNullException(nameof(collection));

        if (collection is IList<T> c)
        {
            int count = c.Count;

            _items = new(count);

            for (int i = 0; i < count; i++)
            {
                _items[i] = c[i];
            }

            Count = count;
        }
        else
        {
            _items = new(DefaultCapacity);
            using IEnumerator<T>? en = collection.GetEnumerator();

            while (en.MoveNext())
            {
                Add(en.Current);
            }
        }
    }

    ~UnmanagedList()
    {
        Dispose();
    }

    public int Capacity
    {
        get => _items.Length;
        set
        {
            if (value < Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value != _items.Length)
            {
                UnmanagedArray<T> oldItems = _items;
                if (value > 0)
                {
                    var newItems = new UnmanagedArray<T>(value);
                    if (Count > 0)
                    {
                        _items.AsSpan().Slice(0, value).CopyTo(newItems.AsSpan());
                    }

                    _items = newItems;
                }
                else
                {
                    _items = new(0);
                }

                oldItems.Dispose();
            }
        }
    }

    public int Count { get; private set; }

    public bool IsDisposed => _items.IsDisposed;

    bool ICollection<T>.IsReadOnly => false;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new IndexOutOfRangeException();

            return _items[index];
        }
        set
        {
            if ((uint)index >= (uint)Count) throw new IndexOutOfRangeException();

            _items[index] = value;
            _version++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        _version++;
        int size = Count;
        if ((uint)size < (uint)_items.Length)
        {
            Count = size + 1;
            _items[size] = item;
        }
        else
        {
            AddWithResize(item);
        }
    }

    public ReadOnlyCollection<T> AsReadOnly()
    {
        return new(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _version++;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            int size = Count;
            Count = 0;
            if (size > 0)
            {
                _items.AsSpan().Clear();
            }
        }
        else
        {
            Count = 0;
        }
    }

    public bool Contains(T item)
    {
        return Count != 0 && IndexOf(item) != -1;
    }

    public UnmanagedList<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
        where TOutput : unmanaged
    {
        if (converter == null) throw new ArgumentNullException(nameof(converter));

        var list = new UnmanagedList<TOutput>(Count);
        for (int i = 0; i < Count; i++)
        {
            list._items[i] = converter(_items[i]);
        }

        list.Count = Count;
        return list;
    }

    public Span<T> AsSpan()
    {
        return _items.AsSpan().Slice(0, Count);
    }

    public int EnsureCapacity(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        if (_items.Length < capacity)
        {
            Grow(capacity);
            _version++;
        }

        return _items.Length;
    }

    public bool Exists(Predicate<T> match)
    {
        return FindIndex(match) != -1;
    }

    public T? Find(Predicate<T> match)
    {
        if (match is null) throw new ArgumentNullException(nameof(match));

        for (int i = 0; i < Count; i++)
        {
            if (match(_items[i]))
            {
                return _items[i];
            }
        }

        return default;
    }

    public UnmanagedList<T> FindAll(Predicate<T> match)
    {
        if (match is null) throw new ArgumentNullException(nameof(match));

        var list = new UnmanagedList<T>();
        for (int i = 0; i < Count; i++)
        {
            if (match(_items[i]))
            {
                list.Add(_items[i]);
            }
        }

        return list;
    }

    public int FindIndex(Predicate<T> match)
    {
        return FindIndex(0, Count, match);
    }

    public int FindIndex(int startIndex, Predicate<T> match)
    {
        return FindIndex(startIndex, Count - startIndex, match);
    }

    public int FindIndex(int startIndex, int count, Predicate<T> match)
    {
        if ((uint)startIndex > (uint)Count) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (count < 0 || startIndex > Count - count) throw new ArgumentOutOfRangeException(nameof(count));
        if (match is null) throw new ArgumentNullException(nameof(match));

        int endIndex = startIndex + count;
        for (int i = startIndex; i < endIndex; i++)
        {
            if (match(_items[i])) return i;
        }

        return -1;
    }

    public T? FindLast(Predicate<T> match)
    {
        if (match is null) throw new ArgumentNullException(nameof(match));

        for (int i = Count - 1; i >= 0; i--)
        {
            if (match(_items[i]))
            {
                return _items[i];
            }
        }

        return default;
    }

    public int FindLastIndex(Predicate<T> match)
    {
        return FindLastIndex(Count - 1, Count, match);
    }

    public int FindLastIndex(int startIndex, Predicate<T> match)
    {
        return FindLastIndex(startIndex, startIndex + 1, match);
    }

    public int FindLastIndex(int startIndex, int count, Predicate<T> match)
    {
        if (match is null) throw new ArgumentNullException(nameof(match));

        if (Count == 0 && startIndex != -1)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }
        else if ((uint)startIndex >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        if (count < 0 || startIndex - count + 1 < 0) throw new ArgumentOutOfRangeException(nameof(count));

        int endIndex = startIndex - count;
        for (int i = startIndex; i > endIndex; i--)
        {
            if (match(_items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public void ForEach(Action<T> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));

        int version = _version;

        for (int i = 0; i < Count; i++)
        {
            if (version != _version)
            {
                break;
            }

            action(_items[i]);
        }

        if (version != _version)
        {
            throw new InvalidOperationException();
        }
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(this);
    }

    public UnmanagedList<T> GetRange(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        if (Count - index < count) throw new ArgumentException();

        var list = new UnmanagedList<T>(count);
        AsSpan().CopyTo(list.AsSpan());

        list.Count = count;
        return list;
    }

    public int IndexOf(T item)
    {
        for (int i = 0; i < Count; i++)
        {
            T elm = _items[i];
            if (elm.Equals(item)) return i;
        }

        return -1;
    }

    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));

        if (Count == _items.Length) Grow(Count + 1);
        if (index < Count)
        {
            Span<T> span = _items.AsSpan();
            int len = Count - index;
            span.Slice(index, len).CopyTo(span.Slice(index + 1, len));
        }

        _items[index] = item;
        Count++;
        _version++;
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }

        return false;
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        Count--;
        if (index < Count)
        {
            Span<T> span = _items.AsSpan();
            int len = Count - index;
            span.Slice(index + 1, len).CopyTo(span.Slice(index, len));
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items[Count] = default!;
        }

        _version++;
    }

    public void RemoveRange(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        if (Count - index < count) throw new ArgumentException();

        if (count > 0)
        {
            Count -= count;
            if (index < Count)
            {
                Span<T> span = _items.AsSpan();
                int len = Count - index;
                span.Slice(index + count, len).CopyTo(span.Slice(index, len));
            }

            _version++;
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _items.AsSpan().Slice(Count, count).Clear();
            }
        }
    }

    public void Reverse()
    {
        Reverse(0, Count);
    }

    public void Reverse(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        if (Count - index < count) throw new ArgumentException();

        if (count > 1)
        {
            AsSpan().Slice(index, count).Reverse();
        }

        _version++;
    }

    public T[] ToArray()
    {
        return AsSpan().ToArray();
    }

    public void TrimExcess()
    {
        int threshold = (int)(_items.Length * 0.9);
        if (Count < threshold)
        {
            Capacity = Count;
        }
    }

    public bool TrueForAll(Predicate<T> match)
    {
        if (match is null) throw new ArgumentNullException(nameof(match));
        for (int i = 0; i < Count; i++)
        {
            if (!match(_items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        AsSpan().CopyTo(array.AsSpan(arrayIndex, Count));
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            _items.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithResize(T item)
    {
        int size = Count;
        Grow(size + 1);
        Count = size + 1;
        _items[size] = item;
    }

    private void Grow(int capacity)
    {
        int newcapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;

        if ((uint)newcapacity > 0X7FFFFFC7) newcapacity = 0X7FFFFFC7;

        if (newcapacity < capacity) newcapacity = capacity;

        Capacity = newcapacity;
    }

    public struct Enumerator : IEnumerator<T>
    {
        private readonly UnmanagedList<T> _list;
        private readonly int _version;
        private int _index;
        private T _current;

        internal Enumerator(UnmanagedList<T> list)
        {
            _list = list;
            _index = 0;
            _version = list._version;
            _current = default;
        }

        public T Current => _current!;

        object? IEnumerator.Current
        {
            get
            {
                if (_index == 0 || _index == _list.Count + 1)
                {
                    throw new InvalidOperationException();
                }

                return Current;
            }
        }

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            UnmanagedList<T> localList = _list;

            if (_version == localList._version && ((uint)_index < (uint)localList.Count))
            {
                _current = localList._items[_index];
                _index++;
                return true;
            }

            return MoveNextRare();
        }

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException();
            }

            _index = 0;
            _current = default;
        }

        private bool MoveNextRare()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException();
            }

            _index = _list.Count + 1;
            _current = default;
            return false;
        }
    }
}

#pragma warning restore CA2208
