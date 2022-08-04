using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeUtl;

public interface ICoreObject : INotifyPropertyChanged, IJsonSerializable
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
    //protected JsonNode? JsonNode;

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
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .DefaultValue(Guid.Empty)
            .Register();

        NameProperty = ConfigureProperty<string, CoreObject>(nameof(Name))
            .PropertyFlags(PropertyFlags.NotifyChanged)
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
            return;
        }

        CorePropertyMetadata<TValue>? metadata = property.GetMetadata<CorePropertyMetadata<TValue>>(ownerType);
        if (metadata.Validator != null)
        {
            value = metadata.Validator.Coerce(this, value);
        }

        if (BatchUpdate)
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
                    entryT.Value = value;
                    RaisePropertyChanged(property, metadata, value, oldValue);
                }
            }
            else
            {
                if (!EqualityComparer<TValue>.Default.Equals(metadata.DefaultValue, value))
                {
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

    public virtual void WriteToJson(ref JsonNode json)
    {
        if (json is JsonObject jsonObject)
        {
            Type ownerType = GetType();

            IReadOnlyList<CoreProperty> list = PropertyRegistry.GetRegistered(GetType());
            for (int i = 0; i < list.Count; i++)
            {
                CoreProperty item = list[i];
                CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(ownerType);
                string? jsonName = metadata.SerializeName;
                if (jsonName != null)
                {
                    jsonObject[jsonName] = item.RouteWriteToJson(this, GetValue(item));
                }
            }
        }
    }

    public virtual void ReadFromJson(JsonNode json)
    {
        Type ownerType = GetType();

        if (json is JsonObject obj)
        {
            IReadOnlyList<CoreProperty> list = PropertyRegistry.GetRegistered(GetType());
            for (int i = 0; i < list.Count; i++)
            {
                CoreProperty item = list[i];
                CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(ownerType);
                string? jsonName = metadata.SerializeName;

                if (jsonName != null
                    && obj.TryGetPropertyValue(jsonName, out JsonNode? jsonNode)
                    && jsonNode != null)
                {
                    if (item.RouteReadFromJson(this, jsonNode) is { } value)
                    {
                        SetValue(item, value);
                    }
                }
            }
        }
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        if (args is CorePropertyChangedEventArgs coreArgs)
        {
            bool hasChangedFlag = coreArgs.PropertyMetadata.PropertyFlags.HasFlag(PropertyFlags.NotifyChanged);
            if (coreArgs.Property.HasObservers)
            {
                coreArgs.Property.NotifyChanged(coreArgs);
            }

            if (hasChangedFlag)
            {
                PropertyChanged?.Invoke(this, args);
            }
        }
        else
        {
            PropertyChanged?.Invoke(this, args);
        }
    }

    protected bool SetAndRaise<T>(CoreProperty<T> property, ref T field, T value)
    {
        CorePropertyMetadata<T>? metadata = property.GetMetadata<CorePropertyMetadata<T>>(GetType());
        if (metadata.Validator != null)
        {
            value = metadata.Validator.Coerce(this, value)!;
        }

        bool result = !EqualityComparer<T>.Default.Equals(field, value);
        if (BatchUpdate)
        {
            if (_batchChanges != null &&
                _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry) &&
                oldEntry is BatchEntry<T> entryT)
            {
                entryT.NewValue = value;
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
        else if (_batchApplying
            && _batchChanges != null
            && _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry)
            && oldEntry is BatchEntry<T> entryT)
        {
            // バッチ適用中
            field = value;

            if (!EqualityComparer<T>.Default.Equals(entryT.OldValue, value))
            {
                RaisePropertyChanged(property, metadata!, value, entryT.OldValue);
            }
        }
        else if (result)
        {
            T old = field;
            field = value;

            RaisePropertyChanged(property, metadata, value, old);
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
        var eventArgs = new CorePropertyChangedEventArgs<T>(this, property, metadata, newValue, oldValue);

        OnPropertyChanged(eventArgs);
    }
}
