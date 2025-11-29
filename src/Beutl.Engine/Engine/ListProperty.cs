using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Beutl.Animation;
using Beutl.Collections;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Engine;

public class ListProperty<T> : IListProperty<T>
{
    private readonly CoreList<T> _items = [];
    private PropertyInfo? _propertyInfo;
    private string? _name;
    private EngineObject? _owner;

    public ListProperty()
    {
        _items.ResetBehavior = ResetBehavior.Remove;
        _items.Attached += item =>
        {
            if (_owner is IModifiableHierarchical ownerHierarchical && item is IHierarchical hierarchical)
            {
                ownerHierarchical.AddChild(hierarchical);
            }
            if (item is INotifyEdited edited)
            {
                edited.Edited += OnChildEdited;
            }
        };
        _items.Detached += item =>
        {
            if (_owner is IModifiableHierarchical ownerHierarchical && item is IHierarchical hierarchical)
            {
                ownerHierarchical.RemoveChild(hierarchical);
            }
            if (item is INotifyEdited edited)
            {
                edited.Edited -= OnChildEdited;
            }
        };
        _items.CollectionChanged += (s, e) => Edited?.Invoke(this, EventArgs.Empty);
    }

    public bool HasValidator => false;

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; } = typeof(ICoreList<T>);

    public Type ElementType { get; } = typeof(T);

    public bool IsAnimatable => false;

    public ICoreList<T> DefaultValue => null!;

    public ICoreList<T> CurrentValue
    {
        get => _items;
        set => _items.Replace(value);
    }

    public IAnimation<ICoreList<T>>? Animation
    {
        get => null;
        set { }
    }

    public bool HasLocalValue => true;

    public event EventHandler<PropertyValueChangedEventArgs<ICoreList<T>>>? ValueChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? Edited;

    public void operator <<= (ICoreList<T> value)
    {
        CurrentValue = value;
    }

    private void OnChildEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    public ICoreList<T> GetValue(TimeSpan time)
    {
        return CurrentValue;
    }

    public void SetPropertyInfo(PropertyInfo propertyInfo)
    {
        _propertyInfo = propertyInfo;
        _name = propertyInfo.Name;
    }

    public PropertyInfo? GetPropertyInfo() => _propertyInfo;

    public void SetOwnerObject(EngineObject? owner)
    {
        if (_owner == owner) return;

        if (owner is IModifiableHierarchical ownerHierarchical)
        {
            foreach (var item in _items)
            {
                if (item is IHierarchical hierarchical)
                    ownerHierarchical.AddChild(hierarchical);
            }
        }
        else if (_owner is IModifiableHierarchical oldOwnerHierarchical)
        {
            foreach (var item in _items)
            {
                if (item is IHierarchical hierarchical)
                    oldOwnerHierarchical.RemoveChild(hierarchical);
            }
        }

        _owner = owner;
    }

    public void DeserializeValue(ICoreSerializationContext context)
    {
        if (!context.Contains(Name)) return;
        T[]? list = context.GetValue<T[]>(Name);
        if (list == null) return;
        CurrentValue.Replace(list);
    }

    public void SerializeValue(ICoreSerializationContext context)
    {
        context.SetValue(Name, CurrentValue);
    }

    public EngineObject? GetOwnerObject()
    {
        return _owner;
    }

    public CoreList<T>.Enumerator GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    public IValidator CreateValidator(PropertyInfo propertyInfo)
    {
        return new MultipleValidator<CoreList<T>>([]);
    }

    public void SetValidator(IValidator validator)
    {
    }


    #region ICoreList<T> Members

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_items).GetEnumerator();

    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    public bool Remove(T item) => _items.Remove(item);

    public int Count => ((ICollection)_items).Count;

    public void Replace(IList<T> source) => _items.Replace(source);

    void ICoreList<T>.Move(int oldIndex, int newIndex) => _items.Move(oldIndex, newIndex);

    void ICoreList<T>.MoveRange(int oldIndex, int count, int newIndex) => _items.MoveRange(oldIndex, count, newIndex);

    void ICoreList<T>.RemoveRange(int index, int count) => _items.RemoveRange(index, count);

    void ICoreList<T>.RemoveAt(int index) => _items.RemoveAt(index);

    void ICoreList<T>.Clear() => _items.Clear();

    void ICoreList.Move(int oldIndex, int newIndex) => _items.Move(oldIndex, newIndex);

    void ICoreList.MoveRange(int oldIndex, int count, int newIndex) => _items.MoveRange(oldIndex, count, newIndex);

    void ICoreList.RemoveRange(int index, int count) => _items.RemoveRange(index, count);

    public int IndexOf(T item) => _items.IndexOf(item);

    public void Insert(int index, T item) => _items.Insert(index, item);

    void IList<T>.RemoveAt(int index) => _items.RemoveAt(index);

    public T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public event Action<T>? Attached
    {
        add => _items.Attached += value;
        remove => _items.Attached -= value;
    }

    public event Action<T>? Detached
    {
        add => _items.Detached += value;
        remove => _items.Detached -= value;
    }

    [Obsolete("Use 'GetMarshal'.")]
    public Span<T> AsSpan() => _items.AsSpan();

    public CoreListMarshal<T> GetMarshal() => _items.GetMarshal();

    public void AddRange(IEnumerable<T> items) => _items.AddRange(items);

    public void InsertRange(int index, IEnumerable<T> items) => _items.InsertRange(index, items);

    void ICoreList.RemoveAt(int index) => _items.RemoveAt(index);

    public void Add(T item) => _items.Add(item);

    void ICollection<T>.Clear() => _items.Clear();

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    void ICoreList.Clear() => _items.Clear();

    public bool IsSynchronized => ((ICollection)_items).IsSynchronized;

    public object SyncRoot => ((ICollection)_items).SyncRoot;

    public int Add(object? value) => ((IList)_items).Add(value);

    void IList.Clear() => ((IList)_items).Clear();

    public bool Contains(object? value) => ((IList)_items).Contains(value);

    public int IndexOf(object? value) => ((IList)_items).IndexOf(value);

    public void Insert(int index, object? value) => ((IList)_items).Insert(index, value);

    public void Remove(object? value) => ((IList)_items).Remove(value);

    public void EnsureCapacity(int capacity) => _items.EnsureCapacity(capacity);

    void IList.RemoveAt(int index) => ((IList)_items).RemoveAt(index);

    public bool IsFixedSize => ((IList)_items).IsFixedSize;

    public bool IsReadOnly => ((IList)_items).IsReadOnly;

    object? IList.this[int index]
    {
        get => ((IList)_items)[index];
        set => ((IList)_items)[index] = value;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => _items.CollectionChanged += value;
        remove => _items.CollectionChanged -= value;
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _items.PropertyChanged += value;
        remove => _items.PropertyChanged -= value;
    }

    #endregion
}
