using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeUtl;

public interface ICoreObject : INotifyPropertyChanged, INotifyPropertyChanging, IJsonSerializable
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    string Name { get; set; }

    void BeginBatchUpdate();

    void EndBatchUpdate();

    void ClearValue<TValue>(CoreProperty<TValue> property);

    void ClearValue(CoreProperty property);

    TValue GetValue<TValue>(CoreProperty<TValue> property);

    object? GetValue(CoreProperty property);

    void SetValue<TValue>(CoreProperty<TValue> property, TValue? value);

    void SetValue(CoreProperty property, object? value);
}

public abstract class CoreObject : ICoreObject
{
    /// <summary>
    /// Defines the <see cref="Id"/> property.
    /// </summary>
    public static readonly CoreProperty<Guid> IdProperty;

    /// <summary>
    /// Defines the <see cref="Name"/> property.
    /// </summary>
    public static readonly CoreProperty<string> NameProperty;

    /// <summary>
    /// The last JsonNode that was used.
    /// </summary>
    protected JsonNode? JsonNode;

    private Dictionary<int, IEntry>? _values;
    private Dictionary<int, IBatchEntry>? _batchChanges;
    private bool _batchApplying;

    private int _batchUpdateCount;

    internal interface IEntry
    {
    }

    internal interface IBatchEntry
    {
        void ApplyTo(CoreObject obj, CoreProperty property);
    }

    internal sealed class Entry<T> : IEntry
    {
        public T? Value;
    }

    internal sealed class BatchEntry<T> : IBatchEntry
    {
        public T? OldValue;
        public T? NewValue;

        public void ApplyTo(CoreObject obj, CoreProperty property)
        {
            obj.SetValue(property, NewValue);
        }
    }

    static CoreObject()
    {
        IdProperty = ConfigureProperty<Guid, CoreObject>(nameof(Id))
            .Observability(PropertyObservability.ChangingAndChanged)
            .DefaultValue(Guid.Empty)
            .Register();

        NameProperty = ConfigureProperty<string, CoreObject>(nameof(Name))
            .Observability(PropertyObservability.ChangingAndChanged)
            .DefaultValue(string.Empty)
            .Register();
    }

    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public Guid Id
    {
        get => GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name
    {
        get => GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    private bool BatchUpdate => _batchUpdateCount > 0;

    private Dictionary<int, IEntry> Values => _values ??= new();

    private Dictionary<int, IBatchEntry> BatchChanges => _batchChanges ??= new();

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs when a property value is changing.
    /// </summary>
    public event PropertyChangingEventHandler? PropertyChanging;

    public static CorePropertyBuilder<T, TOwner> ConfigureProperty<T, TOwner>(string name)
    {
        return new CorePropertyBuilder<T, TOwner>(name);
    }

    public void BeginBatchUpdate()
    {
        _batchUpdateCount++;
    }

    public void EndBatchUpdate()
    {
        _batchUpdateCount--;
        if (_batchUpdateCount < 0)
            throw new InvalidOperationException();

        if (_batchUpdateCount == 0)
        {
            if (_batchChanges != null)
            {
                try
                {
                    _batchApplying = true;
                    foreach (KeyValuePair<int, IBatchEntry> item in _batchChanges)
                    {
                        if (PropertyRegistry.FindRegistered(item.Key) is CoreProperty property)
                        {
                            item.Value.ApplyTo(this, property);
                        }
                    }
                    _batchChanges.Clear();
                }
                finally
                {
                    _batchApplying = false;
                }
            }
        }
    }

    public TValue GetValue<TValue>(CoreProperty<TValue> property)
    {
        Type ownerType = GetType();
        if (ValidateOwnerType(property, ownerType))
        {
            throw new InvalidOperationException("Owner does not match.");
        }

        if (property is StaticProperty<TValue> staticProperty)
        {
            return staticProperty.RouteGetTypedValue(this)!;
        }

        if (BatchUpdate)
        {
            if (_batchChanges?.TryGetValue(property.Id, out IBatchEntry? entry) == true &&
               entry is BatchEntry<TValue> entryT)
            {
                return entryT.NewValue!;
            }
            else
            {
                goto ReturnDefault;
            }
        }
        else if (_values?.TryGetValue(property.Id, out IEntry? entry) == true &&
            entry is Entry<TValue> entryT)
        {
            return entryT.Value!;
        }

    ReturnDefault:
        return property.GetMetadata<CorePropertyMetadata<TValue>>(ownerType).DefaultValue!;
    }

    public object? GetValue(CoreProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.RouteGetValue(this);
    }

    public void SetValue<TValue>(CoreProperty<TValue> property, TValue? value)
    {
        if (value != null && !value.GetType().IsAssignableTo(property.PropertyType))
            throw new InvalidOperationException($"{nameof(value)} of type {value.GetType().Name} cannot be assigned to type {property.PropertyType}.");

        Type ownerType = GetType();
        if (ValidateOwnerType(property, ownerType)) throw new InvalidOperationException("Owner does not match.");

        if (property is StaticProperty<TValue> staticProperty)
        {
            staticProperty.RouteSetTypedValue(this, value);
        }
        else if (BatchUpdate)
        {
            if (_batchChanges != null &&
                _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry) &&
                oldEntry is BatchEntry<TValue> entryT)
            {
                entryT.NewValue = value;
            }
            else
            {
                entryT = new BatchEntry<TValue>
                {
                    NewValue = value,
                };
                BatchChanges[property.Id] = entryT;
            }
        }
        else
        {
            if (_values != null &&
                _values.TryGetValue(property.Id, out IEntry? oldEntry) &&
                oldEntry is Entry<TValue> entryT)
            {
                TValue? oldValue = entryT.Value;
                if (!EqualityComparer<TValue>.Default.Equals(oldValue, value))
                {
                    CorePropertyMetadata metadata = property.GetMetadata<CorePropertyMetadata>(ownerType);
                    RaisePropertyChanging(property, metadata);
                    
                    entryT.Value = value;
                    RaisePropertyChanged(property, metadata, value, oldValue);
                }
            }
            else
            {
                CorePropertyMetadata<TValue> metadata = property.GetMetadata<CorePropertyMetadata<TValue>>(ownerType);

                if (!EqualityComparer<TValue>.Default.Equals(metadata.DefaultValue, value))
                {
                    RaisePropertyChanging(property, metadata);

                    entryT = new Entry<TValue>
                    {
                        Value = value,
                    };
                    Values[property.Id] = entryT;
                    RaisePropertyChanged(property, metadata, value, metadata.DefaultValue);
                }
            }
        }
    }

    public void SetValue(CoreProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.RouteSetValue(this, value!);
    }

    public void ClearValue<TValue>(CoreProperty<TValue> property)
    {
        SetValue(property, property.GetMetadata<CorePropertyMetadata<TValue>>(GetType()));
    }

    public void ClearValue(CoreProperty property)
    {
        SetValue(property, property.GetMetadata<CorePropertyMetadata>(GetType()).GetDefaultValue());
    }

    [MemberNotNull("JsonNode")]
    public virtual void FromJson(JsonNode json)
    {
        JsonNode = json;
        Type ownerType = GetType();

        // Todo: 例外処理
        if (json is JsonObject obj)
        {
            IReadOnlyList<CoreProperty> list = PropertyRegistry.GetRegistered(GetType());
            for (int i = 0; i < list.Count; i++)
            {
                CoreProperty item = list[i];
                CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(ownerType);
                string? jsonName = metadata.SerializeName;
                Type type = item.PropertyType;

                if (jsonName != null &&
                    obj.TryGetPropertyValue(jsonName, out JsonNode? jsonNode) &&
                    jsonNode != null)
                {
                    if (type.IsAssignableTo(typeof(IJsonSerializable)))
                    {
                        var sobj = (IJsonSerializable?)Activator.CreateInstance(type);
                        if (sobj != null)
                        {
                            sobj.FromJson(jsonNode!);
                            SetValue(item, sobj);
                        }
                    }
                    else
                    {
                        object? value = JsonSerializer.Deserialize(jsonNode, type, JsonHelper.SerializerOptions);
                        if (value != null)
                        {
                            SetValue(item, value);
                        }
                    }
                }
            }
        }
    }

    [MemberNotNull("JsonNode")]
    public virtual JsonNode ToJson()
    {
        JsonObject? json;
        Type ownerType = GetType();

        if (JsonNode is JsonObject jsonNodeObj)
        {
            json = jsonNodeObj;
        }
        else
        {
            json = new JsonObject();
            JsonNode = json;
        }

        IReadOnlyList<CoreProperty> list = PropertyRegistry.GetRegistered(GetType());
        for (int i = 0; i < list.Count; i++)
        {
            CoreProperty item = list[i];
            CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(ownerType);
            string? jsonName = metadata.SerializeName;
            if (jsonName != null)
            {
                object? obj = GetValue(item);
                object? def = metadata.GetDefaultValue();

                // デフォルトの値と取得した値が同じ場合、保存しない
                if (RuntimeHelpers.Equals(def, obj))
                {
                    json.Remove(jsonName);
                    continue;
                }

                if (obj is IJsonSerializable child)
                {
                    json[jsonName] = child.ToJson();
                }
                else
                {
                    json[jsonName] = JsonSerializer.SerializeToNode(obj, item.PropertyType, JsonHelper.SerializerOptions);
                }
            }
        }

        return json;
    }

    protected void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    protected void OnPropertyChanging(PropertyChangingEventArgs args)
    {
        PropertyChanging?.Invoke(this, args);
    }

    protected bool SetAndRaise<T>(CoreProperty<T> property, ref T field, T value)
    {
        bool result = true;
        if (BatchUpdate)
        {
            if (_batchChanges != null &&
                _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry) &&
                oldEntry is BatchEntry<T> entryT)
            {
                entryT.NewValue = value;
                result = !EqualityComparer<T>.Default.Equals(field, value);
            }
            else
            {
                entryT = new BatchEntry<T>
                {
                    OldValue = field,
                    NewValue = value,
                };
                BatchChanges[property.Id] = entryT;
            }

            field = value;
        }
        else if (_batchApplying)
        {
            // バッチ適用中
            if (_batchChanges != null &&
                _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry) &&
                oldEntry is BatchEntry<T> entryT)
            {
                result = !EqualityComparer<T>.Default.Equals(entryT.OldValue, value);
                CorePropertyMetadata<T>? metadata = null;
                if (result)
                {
                    metadata = property.GetMetadata<CorePropertyMetadata<T>>(GetType());
                    RaisePropertyChanging(property, metadata);
                }

                field = value;
                if (result)
                {
                    RaisePropertyChanged(property, metadata!, value, entryT.OldValue);
                }
            }
        }
        else if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            CorePropertyMetadata metadata = property.GetMetadata<CorePropertyMetadata>(GetType());
            RaisePropertyChanging(property, metadata);

            T old = field;
            field = value;

            RaisePropertyChanged(property, metadata, value, old);

            result = true;
        }
        else
        {
            result = false;
        }
        return result;
    }

    // オーナーの型が一致しない場合はtrue
    private static bool ValidateOwnerType(CoreProperty property, Type ownerType)
    {
        return !ownerType.IsAssignableTo(property.OwnerType);
    }

    private void RaisePropertyChanged<T>(CoreProperty<T> property, CorePropertyMetadata metadata, T? newValue, T? oldValue)
    {
        if (this is ILogicalElement logicalElement)
        {
            if (oldValue is ILogicalElement oldLogical)
            {
                oldLogical.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(logicalElement));
            }

            if (newValue is ILogicalElement newLogical)
            {
                newLogical.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(logicalElement));
            }
        }

        bool hasChangedFlag = metadata.Observability.HasFlag(PropertyObservability.Changed);
        CorePropertyChangedEventArgs<T>? eventArgs = property.HasObservers || hasChangedFlag
            ? new CorePropertyChangedEventArgs<T>(this, property, newValue, oldValue)
            : null;

        if (property.HasObservers)
        {
            property.NotifyChanged(eventArgs!);
        }

        if (hasChangedFlag)
        {
            OnPropertyChanged(eventArgs!);
        }
    }

    private void RaisePropertyChanging(CoreProperty property, CorePropertyMetadata metadata)
    {
        PropertyObservability observability = metadata.Observability;
        if (observability.HasFlag(PropertyObservability.Changing))
        {
            OnPropertyChanging(new PropertyChangingEventArgs(property.Name));
        }
    }
}
