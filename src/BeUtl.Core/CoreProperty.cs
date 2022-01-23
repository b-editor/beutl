using System;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Subjects;
using System.Reflection;

namespace BeUtl;

public abstract class CoreProperty
{
    private static int s_nextId = 0;
    private readonly CorePropertyMetadata _defaultMetadata;
    private readonly Dictionary<Type, CorePropertyMetadata> _metadata = new();
    private readonly Dictionary<Type, CorePropertyMetadata> _metadataCache = new();
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

    public CorePropertyMetadata GetMetadata<T>() where T : ICoreObject
    {
        return GetMetadata(typeof(T));
    }

    public CorePropertyMetadata GetMetadata(Type type)
    {
        if (!_hasMetadataOverrides)
        {
            return _defaultMetadata;
        }

        return GetMetadataWithOverrides(type);
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

        CorePropertyMetadata? baseMetadata = GetMetadata(type);
        metadata.Merge(baseMetadata, this);
        _metadata.Add(type, metadata);
        _metadataCache.Clear();

        _hasMetadataOverrides = true;
    }

    private CorePropertyMetadata GetMetadataWithOverrides(Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        if (_metadataCache.TryGetValue(type, out CorePropertyMetadata? result))
        {
            return result;
        }

        Type? currentType = type;

        while (currentType != null)
        {
            if (_metadata.TryGetValue(currentType, out result))
            {
                _metadataCache[type] = result;

                return result;
            }

            currentType = currentType.BaseType;
        }

        _metadataCache[type] = _defaultMetadata;

        return _defaultMetadata;
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
        CorePropertyMetadata metadata)
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
