using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Beutl.Collections;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl;

public class ListProperty<T> : IListProperty<T>
{
    private readonly CoreList<T> _items;
    private Attribute[]? _attributes;
    private string? _name;
    private CoreObject? _owner;

    public ListProperty()
    {
        _items = new OwnedList(this) { ResetBehavior = ResetBehavior.Remove };
        _items.Attached += item =>
        {
            if (_owner is IModifiableHierarchical ownerHierarchical && item is IHierarchical hierarchical)
            {
                ownerHierarchical.AddChild(hierarchical);
            }
            if (AffectsRender && item is INotifyEdited edited)
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
            if (AffectsRender && item is INotifyEdited edited)
            {
                edited.Edited -= OnChildEdited;
            }
        };
        _items.CollectionChanged += (s, e) =>
        {
            if (AffectsRender)
                Edited?.Invoke(this, EventArgs.Empty);
        };
    }

    // False for a list whose owner reports its changes, like GraphModel.Nodes: changing it or an item raises no Edited.
    internal bool AffectsRender { get; init; } = true;

    // Brings a list its owner updates lazily up to date before it is used, like GraphNode.NestedInputPorts.
    internal Action? BeforeAccess { get; set; }

    private CoreList<T> Items
    {
        get
        {
            BeforeAccess?.Invoke();
            return _items;
        }
    }

    public bool HasValidator => false;

    public string Name => _name ?? throw new InvalidOperationException("Property is not initialized.");

    public Type ValueType { get; } = typeof(ICoreList<T>);

    public Type ElementType { get; } = typeof(T);

    public ICoreList<T> DefaultValue => null!;

    public ICoreList<T> Value
    {
        get => Items;
        set => Items.Replace(value);
    }

    public void SetValueEvenIfEqual(ICoreList<T> value)
        => Items.Replace(value);

    public bool IsValueSet => true;

    public event EventHandler<PropertyValueChangedEventArgs<ICoreList<T>>>? ValueChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? Edited;

    public void operator <<=(ICoreList<T> value)
    {
        Value = value;
    }

    private void OnChildEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    public void SetAttributes(string name, Attribute[] attributes)
    {
        _name = name;
        _attributes = attributes;
    }

    public Attribute[]? GetAttributes() => _attributes;

    public void SetOwnerObject(CoreObject? owner)
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
        _items.WriteOwner = owner as IHierarchical;
    }

    public void DeserializeValue(ICoreSerializationContext context)
    {
        string name = FormerPersistedNames.ResolveMemberKey(context, Name, _attributes);
        if (!context.Contains(name)) return;
        T[]? list = context.GetValue<T[]>(name);
        if (list == null) return;
        Value.Replace(list);
    }

    public void SerializeValue(ICoreSerializationContext context)
    {
        context.SetValue(Name, Value);
    }

    public CoreObject? GetOwnerObject()
    {
        return _owner;
    }

    public CoreList<T>.Enumerator GetEnumerator()
    {
        return Items.GetEnumerator();
    }

    internal Span<T> AsSpan() => Items.AsSpan();

    public IValidator CreateValidator(Attribute[] attributes)
    {
        return new MultipleValidator<CoreList<T>>([]);
    }

    public void SetValidator(IValidator validator)
    {
    }

    public IValidator? GetValidator() => null;

    #region ICoreList<T> Members

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public void CopyTo(Array array, int index) => ((ICollection)Items).CopyTo(array, index);

    public bool Remove(T item) => Items.Remove(item);

    public int Count => ((ICollection)Items).Count;

    public void Replace(IList<T> source) => Items.Replace(source);

    void ICoreList<T>.Move(int oldIndex, int newIndex) => Items.Move(oldIndex, newIndex);

    void ICoreList<T>.MoveRange(int oldIndex, int count, int newIndex) => Items.MoveRange(oldIndex, count, newIndex);

    void ICoreList<T>.RemoveRange(int index, int count) => Items.RemoveRange(index, count);

    void ICoreList<T>.RemoveAt(int index) => Items.RemoveAt(index);

    void ICoreList<T>.Clear() => Items.Clear();

    void ICoreList.Move(int oldIndex, int newIndex) => Items.Move(oldIndex, newIndex);

    void ICoreList.MoveRange(int oldIndex, int count, int newIndex) => Items.MoveRange(oldIndex, count, newIndex);

    void ICoreList.RemoveRange(int index, int count) => Items.RemoveRange(index, count);

    public int IndexOf(T item) => Items.IndexOf(item);

    public void Insert(int index, T item) => Items.Insert(index, item);

    void IList<T>.RemoveAt(int index) => Items.RemoveAt(index);

    public T this[int index]
    {
        get => Items[index];
        set => Items[index] = value;
    }

    public event Action<T>? Attached
    {
        add => Items.Attached += value;
        remove => Items.Attached -= value;
    }

    public event Action<T>? Detached
    {
        add => Items.Detached += value;
        remove => Items.Detached -= value;
    }

    public void AddRange(IEnumerable<T> items) => Items.AddRange(items);

    internal void RemoveAll(IEnumerable<T> items) => Items.RemoveAll(items);

    public void InsertRange(int index, IEnumerable<T> items) => Items.InsertRange(index, items);

    void ICoreList.RemoveAt(int index) => Items.RemoveAt(index);

    public void Add(T item) => Items.Add(item);

    void ICollection<T>.Clear() => Items.Clear();

    public bool Contains(T item) => Items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => Items.CopyTo(array, arrayIndex);

    void ICoreList.Clear() => Items.Clear();

    public bool IsSynchronized => ((ICollection)Items).IsSynchronized;

    public object SyncRoot => ((ICollection)Items).SyncRoot;

    public int Add(object? value) => ((IList)Items).Add(value);

    void IList.Clear() => ((IList)Items).Clear();

    public bool Contains(object? value) => ((IList)Items).Contains(value);

    public int IndexOf(object? value) => ((IList)Items).IndexOf(value);

    public void Insert(int index, object? value) => ((IList)Items).Insert(index, value);

    public void Remove(object? value) => ((IList)Items).Remove(value);

    public void EnsureCapacity(int capacity) => Items.EnsureCapacity(capacity);

    void IList.RemoveAt(int index) => ((IList)Items).RemoveAt(index);

    public bool IsFixedSize => ((IList)Items).IsFixedSize;

    public bool IsReadOnly => ((IList)Items).IsReadOnly;

    object? IList.this[int index]
    {
        get => ((IList)Items)[index];
        set => ((IList)Items)[index] = value;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => Items.CollectionChanged += value;
        remove => Items.CollectionChanged -= value;
    }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => Items.PropertyChanged += value;
        remove => Items.PropertyChanged -= value;
    }

    #endregion

    // An item that holds the property's owner would become its own ancestor, as in HierarchicalList<T>.
    private sealed class OwnedList(ListProperty<T> property) : CoreList<T>
    {
        private protected override void VerifyInsert(T item)
        {
            if (item is not IHierarchical hierarchical) return;

            for (IHierarchical? owner = property._owner as IHierarchical; owner != null; owner = owner.HierarchicalParent)
            {
                if (ReferenceEquals(owner, hierarchical))
                    throw new InvalidOperationException("An object cannot be added to a list inside it.");
            }
        }
    }
}

internal static class ListPropertyExtensions
{
    // The items as a span, as CoreList<T>.AsSpan gives them.
    public static Span<T> AsSpan<T>(this IListProperty<T> property)
        => property is ListProperty<T> list ? list.AsSpan() : property.ToArray();
}
