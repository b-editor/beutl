using System.Buffers;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Beutl.Collections;

public readonly ref struct CoreListMarshal<T>
{
    private readonly ListReflection<T> _refAs;
    private readonly int _version;
    private readonly Span<T> _value;

    public CoreListMarshal(CoreList<T> list)
    {
        _refAs = list.GetReflection();
        _version = _refAs.Version;
        _value = _refAs.Items.AsSpan().Slice(0, _refAs.Count);
    }

    public Span<T> Value
    {
        get
        {
            if (_refAs.Version != _version)
            {
                throw new InvalidOperationException();
            }

            return _value;
        }
    }
}

public class CoreList<T> : ICoreList<T>
{
    private static readonly PropertyChangedEventArgs s_countPropertyChanged = new(nameof(CoreList<object>.Count));
    private static readonly NotifyCollectionChangedEventArgs s_resetCollectionChanged = new(NotifyCollectionChangedAction.Reset);

    public CoreList()
    {
        Inner = [];
    }

    public CoreList(int capacity)
    {
        Inner = new List<T>(capacity);
    }

    public CoreList(IEnumerable<T> items)
    {
        Inner = new List<T>(items);
    }

    public CoreList(params T[] items)
    {
        Inner = new List<T>(items);
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<T>? Attached;

    public event Action<T>? Detached;

    public int Count => Inner.Count;

    public ResetBehavior ResetBehavior { get; set; }

    protected List<T> Inner { get; }

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    int ICollection.Count => Inner.Count;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    bool ICollection<T>.IsReadOnly => false;

    public T this[int index]
    {
        get => Inner[index];
        set
        {
            T old = Inner[index];

            if (!EqualityComparer<T>.Default.Equals(old, value))
            {
                Detached?.Invoke(old);
                Attached?.Invoke(value);
                Inner[index] = value;

                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    value,
                    old,
                    index));
            }
        }
    }

    object? IList.this[int index]
    {
        get { return this[index]; }
        set { this[index] = (T)value!; }
    }

    public int Capacity
    {
        get => Inner.Capacity;
        set => Inner.Capacity = value;
    }

    [Obsolete("Use 'GetMarshal'.")]
    public Span<T> AsSpan()
    {
        return CollectionsMarshal.AsSpan(Inner);
    }

    public virtual void Add(T item)
    {
        int index = Inner.Count;
        Inner.Add(item);
        NotifyAdd(item, index);
    }

    public virtual void AddRange(IEnumerable<T> items)
    {
        InsertRange(Inner.Count, items);
    }

    public virtual void AddRange(ReadOnlySpan<T> items)
    {
        InsertRange(Inner.Count, items);
    }

    public virtual void AddRange(T[] items)
    {
        InsertRange(Inner.Count, items);
    }

    public virtual void Clear()
    {
        if (Count > 0)
        {
            List<T>? items = null;
            NotifyCollectionChangedEventArgs? eventArgs = null;

            if ((CollectionChanged != null && ResetBehavior == ResetBehavior.Remove)
                || Detached != null)
            {
                items = [.. Inner];
            }

            if (CollectionChanged != null)
            {
                eventArgs = ResetBehavior == ResetBehavior.Reset
                    ? s_resetCollectionChanged
                    : new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, items, 0);
            }

            Inner.Clear();

            foreach (T item in CollectionsMarshal.AsSpan(items))
            {
                Detached?.Invoke(item);
            }

            if (eventArgs != null)
            {
                CollectionChanged?.Invoke(this, eventArgs);
            }

            NotifyCountChanged();
        }
    }

    public virtual void Replace(IList<T> source)
    {
        Span<T> span = CollectionsMarshal.AsSpan(Inner);
        T[] oldItems = Count > 0 ? span.ToArray() : [];
        if (!oldItems.SequenceEqual(source))
        {
            Inner.Clear();
            foreach (T? item in oldItems)
            {
                Detached?.Invoke(item);
            }

            Inner.AddRange(source);
            span = CollectionsMarshal.AsSpan(Inner);

            foreach (T? item in span)
            {
                Attached?.Invoke(item);
            }

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace,
                (IList)source,
                oldItems,
                0));
        }
    }

    public bool Contains(T item)
    {
        return Inner.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        Inner.CopyTo(array, arrayIndex);
    }

    public void CopyTo(Span<T> array)
    {
        Span<T> span = CollectionsMarshal.AsSpan(Inner);
        span.CopyTo(array);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return new Enumerator(Inner);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return new Enumerator(Inner);
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(Inner);
    }

    public IEnumerable<T> GetRange(int index, int count)
    {
        return Inner.GetRange(index, count);
    }

    public int IndexOf(T item)
    {
        return Inner.IndexOf(item);
    }

    public virtual void Insert(int index, T item)
    {
        Inner.Insert(index, item);
        NotifyAdd(item, index);
    }

    public virtual void InsertRange(int index, IEnumerable<T> items)
    {
        _ = items ?? throw new ArgumentNullException(nameof(items));

        bool willRaiseCollectionChanged = CollectionChanged != null;
        if (items.TryGetNonEnumeratedCount(out int count))
        {
            EnsureCapacity(Inner.Count + count);
        }

        if (items is IList list)
        {
            if (list.Count > 0)
            {
                if (list is ICollection<T> collection)
                {
                    Inner.InsertRange(index, collection);
                    NotifyAdd(list, index);
                }
                else
                {
                    using (IEnumerator<T> en = items.GetEnumerator())
                    {
                        int insertIndex = index;

                        while (en.MoveNext())
                        {
                            Inner.Insert(insertIndex++, en.Current);
                        }
                    }

                    NotifyAdd(list, index);
                }
            }
        }
        else
        {
            using (IEnumerator<T> en = items.GetEnumerator())
            {
                if (en.MoveNext())
                {
                    // Avoid allocating list for collection notification if there is no event subscriptions.
                    List<T>? notificationItems = willRaiseCollectionChanged ? [] : null;

                    int insertIndex = index;

                    do
                    {
                        T item = en.Current;
                        Inner.Insert(insertIndex++, item);

                        notificationItems?.Add(item);

                    } while (en.MoveNext());

                    if (notificationItems is not null)
                        NotifyAdd(notificationItems, index);
                }
            }
        }
    }

    public virtual void InsertRange(int index, ReadOnlySpan<T> items)
    {
        if (items.Length > 0)
        {
            EnsureCapacity(Inner.Count + items.Length);

            ReadOnlySpan<T>.Enumerator en = items.GetEnumerator();
            int insertIndex = index;

            while (en.MoveNext())
            {
                Inner.Insert(insertIndex++, en.Current);
            }

            NotifyAdd(items, index);
        }
    }

    public virtual void InsertRange(int index, T[] items)
    {
        if (items.Length > 0)
        {
            EnsureCapacity(Inner.Count + items.Length);

            int insertIndex = index;

            // Inner.InsertRangeを使わない理由:
            // CoreList<object[]> のとき、items に string[] が指定されるとArrayTypeMismatchExceptionが発生するから
            for (int i = 0; i < items.Length; i++)
            {
                Inner.Insert(insertIndex++, items[i]);
            }

            NotifyAdd((IList)items, index);
        }
    }

    public void Move(int oldIndex, int newIndex)
    {
        T item = this[oldIndex];
        Inner.RemoveAt(oldIndex);
        Inner.Insert(newIndex, item);

        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Move,
            item,
            newIndex,
            oldIndex));
    }

    public void MoveRange(int oldIndex, int count, int newIndex)
    {
        List<T> items = Inner.GetRange(oldIndex, count);
        Inner.RemoveRange(oldIndex, count);

        if (newIndex > oldIndex)
        {
            newIndex -= count;
        }

        Inner.InsertRange(newIndex, items);

        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Move,
            items,
            newIndex,
            oldIndex));
    }

    public void EnsureCapacity(int capacity)
    {
        int currentCapacity = Inner.Capacity;

        if (currentCapacity < capacity)
        {
            int newCapacity = currentCapacity == 0 ? 4 : currentCapacity * 2;

            if (newCapacity < capacity)
            {
                newCapacity = capacity;
            }

            Inner.Capacity = newCapacity;
        }
    }

    public virtual bool Remove(T item)
    {
        int index = Inner.IndexOf(item);

        if (index != -1)
        {
            Inner.RemoveAt(index);
            NotifyRemove(item, index);
            return true;
        }

        return false;
    }

    public virtual void RemoveAll(IEnumerable<T> items)
    {
        _ = items ?? throw new ArgumentNullException(nameof(items));

        var hItems = new HashSet<T>(items);

        int counter = 0;
        for (int i = Inner.Count - 1; i >= 0; --i)
        {
            if (hItems.Contains(Inner[i]))
            {
                counter += 1;
            }
            else if (counter > 0)
            {
                RemoveRange(i + 1, counter);
                counter = 0;
            }
        }

        if (counter > 0)
            RemoveRange(0, counter);
    }

    public virtual void RemoveAt(int index)
    {
        T item = Inner[index];
        Inner.RemoveAt(index);
        NotifyRemove(item, index);
    }

    public virtual void RemoveRange(int index, int count)
    {
        if (count > 0)
        {
            List<T> list = Inner.GetRange(index, count);
            Inner.RemoveRange(index, count);
            NotifyRemove(list, index);
        }
    }

    public CoreListMarshal<T> GetMarshal()
    {
        return new CoreListMarshal<T>(this);
    }

    internal ListReflection<T> GetReflection()
    {
        return Unsafe.As<ListReflection<T>>(Inner);
    }

    int IList.Add(object? value)
    {
        int index = Count;
        Add((T)value!);
        return index;
    }

    bool IList.Contains(object? value)
    {
        return Contains((T)value!);
    }

    void IList.Clear()
    {
        Clear();
    }

    int IList.IndexOf(object? value)
    {
        return IndexOf((T)value!);
    }

    void IList.Insert(int index, object? value)
    {
        Insert(index, (T)value!);
    }

    void IList.Remove(object? value)
    {
        Remove((T)value!);
    }

    void IList.RemoveAt(int index)
    {
        RemoveAt(index);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (array.Rank != 1)
        {
            throw new ArgumentException("Multi-dimensional arrays are not supported.");
        }

        if (array.GetLowerBound(0) != 0)
        {
            throw new ArgumentException("Non-zero lower bounds are not supported.");
        }

        if (index < 0)
        {
            throw new ArgumentException("Invalid index.");
        }

        if (array.Length - index < Count)
        {
            throw new ArgumentException("The target array is too small.");
        }

        if (array is T[] tArray)
        {
            Inner.CopyTo(tArray, index);
        }
        else
        {
            //
            // Catch the obvious case assignment will fail.
            // We can't find all possible problems by doing the check though.
            // For example, if the element type of the Array is derived from T,
            // we can't figure out if we can successfully copy the element beforehand.
            //
            Type targetType = array.GetType().GetElementType()!;
            Type sourceType = typeof(T);
            if (!(targetType.IsAssignableFrom(sourceType) || sourceType.IsAssignableFrom(targetType)))
            {
                throw new ArgumentException("Invalid array type");
            }

            //
            // We can't cast array of value type to object[], so we don't support
            // widening of primitive types here.
            //
            if (array is not object?[] objects)
            {
                throw new ArgumentException("Invalid array type");
            }

            int count = Inner.Count;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    objects[index++] = Inner[i];
                }
            }
            catch (ArrayTypeMismatchException)
            {
                throw new ArgumentException("Invalid array type");
            }
        }
    }

    private void NotifyAdd(IList t, int index)
    {
        for (int i = 0; i < t.Count; i++)
        {
            if (t[i] is T item)
            {
                Attached?.Invoke(item);
            }
        }

        if (CollectionChanged != null)
        {
            var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, t, index);
            CollectionChanged(this, e);
        }

        NotifyCountChanged();
    }

    private void NotifyAdd(ReadOnlySpan<T> t, int index)
    {
        for (int i = 0; i < t.Length; i++)
        {
            Attached?.Invoke(t[i]);
        }

        if (CollectionChanged != null)
        {
            T[] array = ArrayPool<T>.Shared.Rent(t.Length);
            t.CopyTo(array.AsSpan());

            var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, array, index);
            try
            {
                CollectionChanged(this, e);
            }
            finally
            {
                ArrayPool<T>.Shared.Return(array);
            }
        }

        NotifyCountChanged();
    }

    private void NotifyAdd(T item, int index)
    {
        Attached?.Invoke(item);

        if (CollectionChanged != null)
        {
            var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new[] { item }, index);
            CollectionChanged(this, e);
        }

        NotifyCountChanged();
    }

    private void NotifyCountChanged()
    {
        PropertyChanged?.Invoke(this, s_countPropertyChanged);
    }

    private void NotifyRemove(IList t, int index)
    {
        for (int i = 0; i < t.Count; i++)
        {
            if (t[i] is T item)
            {
                Detached?.Invoke(item);
            }
        }

        if (CollectionChanged != null)
        {
            var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, t, index);
            CollectionChanged(this, e);
        }

        NotifyCountChanged();
    }

    private void NotifyRemove(T item, int index)
    {
        Detached?.Invoke(item);

        if (CollectionChanged != null)
        {
            var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, new[] { item }, index);
            CollectionChanged(this, e);
        }

        NotifyCountChanged();
    }

    public struct Enumerator(List<T> inner) : IEnumerator<T>
    {
        private List<T>.Enumerator _innerEnumerator = inner.GetEnumerator();

        public bool MoveNext()
        {
            return _innerEnumerator.MoveNext();
        }

        readonly void IEnumerator.Reset()
        {
            ((IEnumerator)_innerEnumerator).Reset();
        }

        public T Current => _innerEnumerator.Current;

        object? IEnumerator.Current => Current;

        public void Dispose()
        {
            _innerEnumerator.Dispose();
        }
    }
}
