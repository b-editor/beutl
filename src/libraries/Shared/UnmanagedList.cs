// UnmanagedList.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

#pragma warning disable CA2208
namespace BEditor
{
    internal class UnmanagedList<T> : IList<T>, IReadOnlyList<T>, IDisposable
        where T : unmanaged
    {
        private const int DefaultCapacity = 4;

        private UnmanagedArray<T> _items;
        private int _size;
        private int _version;

        public UnmanagedList()
        {
            _items = new(0);
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
                var count = c.Count;

                _items = new(count);

                for (var i = 0; i < count; i++)
                {
                    _items[i] = c[i];
                }

                _size = count;
            }
            else
            {
                _items = new(0);
                using var en = collection!.GetEnumerator();

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
                if (value < _size) throw new ArgumentOutOfRangeException(nameof(value));
                if (value != _items.Length)
                {
                    var oldItems = _items;
                    if (value > 0)
                    {
                        var newItems = new UnmanagedArray<T>(value);
                        if (_size > 0)
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

        public int Count => _size;

        public bool IsDisposed => _items.IsDisposed;

        bool ICollection<T>.IsReadOnly => false;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_size) throw new IndexOutOfRangeException();

                return _items[index];
            }
            set
            {
                if ((uint)index >= (uint)_size) throw new IndexOutOfRangeException();

                _items[index] = value;
                _version++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            _version++;
            var array = _items;
            var size = _size;
            if ((uint)size < (uint)array.Length)
            {
                _size = size + 1;
                array[size] = item;
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
                var size = _size;
                _size = 0;
                if (size > 0)
                {
                    _items.AsSpan().Clear();
                }
            }
            else
            {
                _size = 0;
            }
        }

        public bool Contains(T item)
        {
            return _size != 0 && IndexOf(item) != -1;
        }

        public UnmanagedList<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
            where TOutput : unmanaged
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            var list = new UnmanagedList<TOutput>(_size);
            for (var i = 0; i < _size; i++)
            {
                list._items[i] = converter(_items[i]);
            }

            list._size = _size;
            return list;
        }

        public Span<T> AsSpan()
        {
            return _items.AsSpan().Slice(0, _size);
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

            for (var i = 0; i < _size; i++)
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
            for (var i = 0; i < _size; i++)
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
            return FindIndex(0, _size, match);
        }

        public int FindIndex(int startIndex, Predicate<T> match)
        {
            return FindIndex(startIndex, _size - startIndex, match);
        }

        public int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            if ((uint)startIndex > (uint)_size) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (count < 0 || startIndex > _size - count) throw new ArgumentOutOfRangeException(nameof(count));
            if (match is null) throw new ArgumentNullException(nameof(match));

            var endIndex = startIndex + count;
            for (var i = startIndex; i < endIndex; i++)
            {
                if (match(_items[i])) return i;
            }

            return -1;
        }

        public T? FindLast(Predicate<T> match)
        {
            if (match is null) throw new ArgumentNullException(nameof(match));

            for (var i = _size - 1; i >= 0; i--)
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
            return FindLastIndex(_size - 1, _size, match);
        }

        public int FindLastIndex(int startIndex, Predicate<T> match)
        {
            return FindLastIndex(startIndex, startIndex + 1, match);
        }

        public int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            if (match is null) throw new ArgumentNullException(nameof(match));

            if (_size == 0 && startIndex != -1)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }
            else if ((uint)startIndex >= (uint)_size)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            if (count < 0 || startIndex - count + 1 < 0) throw new ArgumentOutOfRangeException(nameof(count));

            var endIndex = startIndex - count;
            for (var i = startIndex; i > endIndex; i--)
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

            var version = _version;

            for (var i = 0; i < _size; i++)
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

            if (_size - index < count) throw new ArgumentException();

            var list = new UnmanagedList<T>(count);
            AsSpan().CopyTo(list.AsSpan());

            list._size = count;
            return list;
        }

        public int IndexOf(T item)
        {
            for (var i = 0; i < _size; i++)
            {
                var elm = _items[i];
                if (elm.Equals(item)) return i;
            }

            return -1;
        }

        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));

            if (_size == _items.Length) Grow(_size + 1);
            if (index < _size)
            {
                var span = _items.AsSpan();
                var len = _size - index;
                span.Slice(index, len).CopyTo(span.Slice(index + 1, len));
            }

            _items[index] = item;
            _size++;
            _version++;
        }

        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }

            return false;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            _size--;
            if (index < _size)
            {
                var span = _items.AsSpan();
                var len = _size - index;
                span.Slice(index + 1, len).CopyTo(span.Slice(index, len));
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _items[_size] = default!;
            }

            _version++;
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));

            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            if (_size - index < count) throw new ArgumentException();

            if (count > 0)
            {
                _size -= count;
                if (index < _size)
                {
                    var span = _items.AsSpan();
                    var len = _size - index;
                    span.Slice(index + count, len).CopyTo(span.Slice(index, len));
                }

                _version++;
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    _items.AsSpan().Slice(_size, count).Clear();
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

            if (_size - index < count) throw new ArgumentException();

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
            var threshold = (int)(((double)_items.Length) * 0.9);
            if (_size < threshold)
            {
                Capacity = _size;
            }
        }

        public bool TrueForAll(Predicate<T> match)
        {
            if (match is null) throw new ArgumentNullException(nameof(match));
            for (var i = 0; i < _size; i++)
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
            AsSpan().CopyTo(array.AsSpan(arrayIndex, _size));
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
            var size = _size;
            Grow(size + 1);
            _size = size + 1;
            _items[size] = item;
        }

        private void Grow(int capacity)
        {
            var newcapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;

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
                    if (_index == 0 || _index == _list._size + 1)
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
                var localList = _list;

                if (_version == localList._version && ((uint)_index < (uint)localList._size))
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

                _index = _list._size + 1;
                _current = default;
                return false;
            }
        }
    }
#pragma warning restore CA2208
}