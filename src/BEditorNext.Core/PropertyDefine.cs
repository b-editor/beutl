using System.Reactive.Subjects;

namespace BEditorNext;

public abstract class PropertyDefine
{
    private static readonly object s_lock = new();
    private static int s_nextId = 0;

    public PropertyDefine(IDictionary<string, object> metaTable)
    {
        MetaTable = metaTable;

        lock (s_lock)
        {
            this.SetKeyValue(PropertyMetaTableKeys.Id, s_nextId);

            s_nextId++;
        }
    }

    public string Name
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.Name, out object? val) &&
                val is string result)
            {
                return result;
            }

            throw new KeyNotFoundException();
        }
    }

    public Type PropertyType
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.PropertyType, out object? val) &&
                val is Type result)
            {
                return result;
            }

            throw new KeyNotFoundException();
        }
    }

    public Type OwnerType
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.OwnerType, out object? val) &&
                val is Type result)
            {
                return result;
            }

            throw new KeyNotFoundException();
        }
    }

    public bool IsAttached
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.IsAttached, out object? val) &&
                val is bool result)
            {
                return result;
            }

            return false;
        }
    }

    public bool HasGetter => MetaTable.ContainsKey(PropertyMetaTableKeys.Getter) || MetaTable.ContainsKey(PropertyMetaTableKeys.GenericsGetter);

    public bool HasSetter => MetaTable.ContainsKey(PropertyMetaTableKeys.Setter) || MetaTable.ContainsKey(PropertyMetaTableKeys.GenericsSetter);

    public int Id
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.Id, out object? val) &&
                val is int result)
            {
                return result;
            }

            return -1;
        }
    }

    public IObservable<ElementPropertyChangedEventArgs> Changed => GetChanged();

    public IDictionary<string, object> MetaTable { get; }

    internal abstract void RouteSetValue(Element o, object? value);
    
    internal abstract object? RouteGetValue(Element o);

    protected abstract IObservable<ElementPropertyChangedEventArgs> GetChanged();
}

public class PropertyDefine<T> : PropertyDefine
{
    private readonly Subject<ElementPropertyChangedEventArgs<T>> _changed;

    public PropertyDefine(IDictionary<string, object> metaTable)
        : base(metaTable)
    {
        this.SetKeyValue(PropertyMetaTableKeys.PropertyType, typeof(T));
        _changed = new();
    }

    public new IObservable<ElementPropertyChangedEventArgs<T>> Changed => _changed;

    internal void NotifyChanged(ElementPropertyChangedEventArgs<T> e)
    {
        _changed.OnNext(e);
    }

    internal override void RouteSetValue(Element o, object? value)
    {
        if (value is T typed)
        {
            o.SetValue<T>(this, typed);
        }
        else
        {
            o.SetValue<T>(this, default);
        }
    }

    internal override object? RouteGetValue(Element o)
    {
        return o.GetValue<T>(this);
    }

    protected override IObservable<ElementPropertyChangedEventArgs> GetChanged()
    {
        return Changed;
    }
}
