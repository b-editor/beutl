using System.Reactive.Subjects;

namespace BeUtl;

public abstract class CoreProperty
{
    private static int s_nextId = 0;
    private readonly ICorePropertyMetadata _defaultMetadata;
    private readonly Dictionary<Type, ICorePropertyMetadata> _metadata = new();
    private readonly Dictionary<Type, ICorePropertyMetadata> _metadataCache = new();
    private bool _hasMetadataOverrides;

    protected CoreProperty(
        string name,
        Type propertyType,
        Type ownerType,
        CorePropertyMetadata metadata)
    {
        _ = name ?? throw new ArgumentNullException(nameof(name));

        if (name.Contains('.'))
        {
            throw new ArgumentException("'name' may not contain periods.");
        }

        Name = name;
        PropertyType = propertyType ?? throw new ArgumentNullException(nameof(propertyType));
        OwnerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
        Id = s_nextId++;

        _metadata.Add(ownerType, metadata ?? throw new ArgumentNullException(nameof(metadata)));
        _defaultMetadata = metadata;
    }

    public string Name { get; }

    public Type PropertyType { get; }

    public Type OwnerType { get; }

    public int Id { get; }

    public IObservable<CorePropertyChangedEventArgs> Changed => GetChanged();

    internal abstract void RouteSetValue(ICoreObject o, object? value);

    internal abstract object? RouteGetValue(ICoreObject o);

    protected abstract IObservable<CorePropertyChangedEventArgs> GetChanged();

    public TMetadata GetMetadata<T, TMetadata>()
        where T : ICoreObject
        where TMetadata : ICorePropertyMetadata
    {
        return GetMetadata<TMetadata>(typeof(T));
    }

    public TMetadata GetMetadata<TMetadata>(Type type)
        where TMetadata : ICorePropertyMetadata
    {
        if (!_hasMetadataOverrides)
        {
            return (TMetadata)_defaultMetadata;
        }

        return GetMetadataWithOverrides<TMetadata>(type);
    }

    public void OverrideMetadata<T>(CorePropertyMetadata metadata)
         where T : ICoreObject
    {
        OverrideMetadata(typeof(T), metadata);
    }

    public void OverrideMetadata(Type type, CorePropertyMetadata metadata)
    {
        _ = type ?? throw new ArgumentNullException(nameof(type));
        _ = metadata ?? throw new ArgumentNullException(nameof(metadata));

        if (_metadata.ContainsKey(type))
        {
            throw new InvalidOperationException(
                $"Metadata is already set for {Name} on {type}.");
        }

        CorePropertyMetadata? baseMetadata = GetMetadata<CorePropertyMetadata>(type);
        metadata.Merge(baseMetadata, this);
        _metadata.Add(type, metadata);
        _metadataCache.Clear();

        _hasMetadataOverrides = true;
    }

    private TMetadata GetMetadataWithOverrides<TMetadata>(Type type)
        where TMetadata : ICorePropertyMetadata
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (_metadataCache.TryGetValue(type, out ICorePropertyMetadata? result) && result is TMetadata resultT)
        {
            return resultT;
        }

        Type? currentType = type;

        while (currentType != null)
        {
            if (_metadata.TryGetValue(currentType, out result) && result is TMetadata resultT1)
            {
                _metadataCache[type] = result;

                return resultT1;
            }

            currentType = currentType.BaseType;
        }

        _metadataCache[type] = _defaultMetadata;

        return (TMetadata)_defaultMetadata;
    }

    public override bool Equals(object? obj)
    {
        return obj is CoreProperty property && Id == property.Id;
    }

    public override int GetHashCode()
    {
        return Id;
    }
}

public class CoreProperty<T> : CoreProperty
{
    private readonly Subject<CorePropertyChangedEventArgs<T>> _changed;

    public CoreProperty(
        string name,
        Type ownerType,
        CorePropertyMetadata<T> metadata)
        : base(name, typeof(T), ownerType, metadata)
    {
        _changed = new();
    }

    public new IObservable<CorePropertyChangedEventArgs<T>> Changed => _changed;

    internal bool HasObservers => _changed.HasObservers;

    internal void NotifyChanged(CorePropertyChangedEventArgs<T> e)
    {
        _changed.OnNext(e);
    }

    internal override void RouteSetValue(ICoreObject o, object? value)
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

    internal override object? RouteGetValue(ICoreObject o)
    {
        return o.GetValue<T>(this);
    }

    protected override IObservable<CorePropertyChangedEventArgs> GetChanged()
    {
        return Changed;
    }
}
