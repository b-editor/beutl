using System.Diagnostics.CodeAnalysis;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl;

public abstract class CoreProperty : ICoreProperty
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

    internal abstract bool HasObservers { get; }

    internal abstract void RouteSetValue(ICoreObject o, object? value);

    internal abstract object? RouteGetValue(ICoreObject o);

    internal abstract void NotifyChanged(CorePropertyChangedEventArgs e);

    internal abstract JsonNode? RouteWriteToJson(CorePropertyMetadata metadata, object? value, out bool isDefault);

    internal abstract object? RouteReadFromJson(CorePropertyMetadata metadata, JsonNode node);

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

        return GetMetadataWithOverrides<TMetadata>(type) ?? throw new InvalidOperationException();
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

    public void OverrideMetadata<T>(CorePropertyMetadata metadata)
         where T : ICoreObject
    {
        OverrideMetadata(typeof(T), metadata);
    }

    public void OverrideMetadata(Type type, CorePropertyMetadata metadata)
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

    private TMetadata? GetMetadataWithOverrides<TMetadata>(Type type)
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

        return _defaultMetadata is TMetadata metadata ? metadata : default;
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

    internal override JsonNode? RouteWriteToJson(CorePropertyMetadata metadata, object? value, out bool isDefault)
    {
        var typedMetadata = (CorePropertyMetadata<T>)metadata;
        object? def = typedMetadata.GetDefaultValue();
        // デフォルトの値と取得した値が同じ場合、保存しない
        isDefault = RuntimeHelpers.Equals(def, value);
        if (typedMetadata.JsonConverter is { } jsonConverter)
        {
            var options = new JsonSerializerOptions(JsonHelper.SerializerOptions);

            options.Converters.Add(jsonConverter);
            return JsonSerializer.SerializeToNode(value, PropertyType, options);
        }
        else if (value is IJsonSerializable child)
        {
            JsonNode jsonNode = new JsonObject();
            child.WriteToJson(ref jsonNode!);

            Type objType = value.GetType();
            if (objType != PropertyType && jsonNode is JsonObject)
            {
                jsonNode["@type"] = TypeFormat.ToString(objType);
            }

            return jsonNode;
        }
        else
        {
            var options = new JsonSerializerOptions(JsonHelper.SerializerOptions);
            return JsonSerializer.SerializeToNode(value, PropertyType, options);
        }
    }

    internal override object? RouteReadFromJson(CorePropertyMetadata metadata, JsonNode node)
    {
        var typedMetadata = (CorePropertyMetadata<T>)metadata;
        Type type = PropertyType;

        if (typedMetadata.JsonConverter is { } jsonConverter)
        {
            var options = new JsonSerializerOptions(JsonHelper.SerializerOptions);

            options.Converters.Add(jsonConverter);
            return JsonSerializer.Deserialize(node, type, options);
        }
        else if (node is JsonObject jsonObject
            && jsonObject.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
            && atTypeNode is JsonValue atTypeValue
            && atTypeValue.TryGetValue(out string? atTypeStr)
            && TypeFormat.ToType(atTypeStr) is Type realType
            && realType.IsAssignableTo(typeof(IJsonSerializable)))
        {
            var sobj = (IJsonSerializable?)Activator.CreateInstance(realType);
            sobj?.ReadFromJson(node!);

            return sobj;
        }
        else if (type.IsAssignableTo(typeof(IJsonSerializable)))
        {
            var sobj = (IJsonSerializable?)Activator.CreateInstance(type);
            if (sobj != null)
            {
                sobj.ReadFromJson(node!);
            }

            return sobj;
        }
        else
        {
            var options = new JsonSerializerOptions(JsonHelper.SerializerOptions);
            return JsonSerializer.Deserialize(node, type, options);
        }
    }

    protected override IObservable<CorePropertyChangedEventArgs> GetChanged()
    {
        return Changed;
    }
}
