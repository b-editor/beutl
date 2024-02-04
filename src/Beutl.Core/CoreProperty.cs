using System.Diagnostics.CodeAnalysis;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Serialization;

namespace Beutl;

[JsonConverter(typeof(CorePropertyJsonConverter))]
public abstract class CoreProperty : ICoreProperty
{
    private static int s_nextId = 0;
    private readonly ICorePropertyMetadata _defaultMetadata;
    private readonly Dictionary<Type, ICorePropertyMetadata> _metadata = [];
    private readonly Dictionary<Type, ICorePropertyMetadata> _metadataCache = [];
    private readonly object _metadataLock = new();
    private bool _hasMetadataOverrides;
    private bool _isTryedToGetPropertyInfo;

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

    internal abstract bool HasObservers { get; }

    internal PropertyInfo? PropertyInfo { get; set; }

    internal PropertyInfo? GetPropertyInfo()
    {
        if (PropertyInfo == null
            && !_isTryedToGetPropertyInfo)
        {
            PropertyInfo = OwnerType.GetProperty(Name);
            _isTryedToGetPropertyInfo = true;
        }

        return PropertyInfo;
    }

    internal abstract void RouteSetValue(ICoreObject o, object? value);

    internal abstract object? RouteGetValue(ICoreObject o);

    internal abstract void NotifyChanged(CorePropertyChangedEventArgs e);

    internal abstract void RouteSerialize(ICoreSerializationContext context, object? value);

    internal abstract Optional<object?> RouteDeserialize(ICoreSerializationContext context);

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
        lock (_metadataLock)
        {
            if (!_hasMetadataOverrides)
            {
                return (TMetadata)_defaultMetadata;
            }

            return GetMetadataWithOverrides<TMetadata>(type) ?? throw new InvalidOperationException();
        }
    }

    public bool TryGetMetadata<T, TMetadata>([NotNullWhen(true)] out TMetadata? result)
        where T : ICoreObject
        where TMetadata : ICorePropertyMetadata
    {
        return TryGetMetadata(typeof(T), out result);
    }

    public bool TryGetMetadata<TMetadata>(Type type, [NotNullWhen(true)] out TMetadata? result)
        where TMetadata : ICorePropertyMetadata
    {
        lock (_metadataLock)
        {
            if (!_hasMetadataOverrides && _defaultMetadata is TMetadata metadata)
            {
                result = metadata;
                return true;
            }
            else
            {
                result = GetMetadataWithOverrides<TMetadata>(type);
                return result != null;
            }
        }
    }

    public void OverrideMetadata<T>(CorePropertyMetadata metadata)
         where T : ICoreObject
    {
        OverrideMetadata(typeof(T), metadata);
    }

    public void OverrideMetadata(Type type, CorePropertyMetadata metadata)
    {
        lock (_metadataLock)
        {
            _ = type ?? throw new ArgumentNullException(nameof(type));
            _ = metadata ?? throw new ArgumentNullException(nameof(metadata));

            if (metadata.PropertyType != PropertyType)
                throw new InvalidOperationException("Property type mismatch.");

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
    }

    private TMetadata? GetMetadataWithOverrides<TMetadata>(Type type)
        where TMetadata : ICorePropertyMetadata
    {
        lock (_metadataLock)
        {
            ArgumentNullException.ThrowIfNull(type);

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

            return _defaultMetadata is TMetadata metadata ? metadata : default;
        }
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

public class CoreProperty<T>(
    string name,
    Type ownerType,
    CorePropertyMetadata<T> metadata)
    : CoreProperty(name, typeof(T), ownerType, metadata)
{
    private readonly Subject<CorePropertyChangedEventArgs<T>> _changed = new();

    public new IObservable<CorePropertyChangedEventArgs<T>> Changed => _changed;

    public void OverrideDefaultValue<TOverride>(Optional<T> defaultValue)
         where TOverride : ICoreObject
    {
        OverrideMetadata<TOverride>(new CorePropertyMetadata<T>(defaultValue));
    }

    public void OverrideMetadata(Type type, Optional<T> defaultValue)
    {
        OverrideMetadata(type, new CorePropertyMetadata<T>(defaultValue));
    }

    internal override bool HasObservers => _changed.HasObservers;

    internal override void NotifyChanged(CorePropertyChangedEventArgs e)
    {
        _changed.OnNext((CorePropertyChangedEventArgs<T>)e);
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

    internal override void RouteSerialize(ICoreSerializationContext context, object? value)
    {
        CorePropertyMetadata<T> metadata = GetMetadata<CorePropertyMetadata<T>>(context.OwnerType);
        if (metadata.ShouldSerialize && (this is not IStaticProperty sprop || sprop.CanWrite))
        {
            if (context is IJsonSerializationContext jsonCtxt
                && metadata.JsonConverter is { }
                && value != null)
            {
                JsonSerializerOptions options = metadata.GetSerializerOptions();
                JsonNode? node = JsonSerializer.SerializeToNode(value, PropertyType, options);
                jsonCtxt.SetNode(Name, PropertyType, value.GetType(), node);
            }
            else
            {
                context.SetValue(Name, (T?)value);
            }
        }
    }

    internal override Optional<object?> RouteDeserialize(ICoreSerializationContext context)
    {
        CorePropertyMetadata<T> metadata = GetMetadata<CorePropertyMetadata<T>>(context.OwnerType);
        if (metadata.ShouldSerialize && (this is not IStaticProperty sprop || sprop.CanWrite))
        {
            if (context is IJsonSerializationContext jsonCtxt
                && metadata.JsonConverter is { })
            {
                Type type = PropertyType;
                JsonNode? node = jsonCtxt.GetNode(Name);

                JsonSerializerOptions options = metadata.GetSerializerOptions();
                return JsonSerializer.Deserialize(node, type, options);
            }

            if (context.Contains(Name))
            {
                return new Optional<object?>(context.GetValue<T>(Name));
            }
            else
            {
                return default;
            }
        }

        return default;
    }
}
