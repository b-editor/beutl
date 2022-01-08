using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading.Tasks;

namespace BEditorNext;

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

    private Dictionary<int, object?>? _values = new();

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

    /// <summary>
    /// Gets the dynamic property values.
    /// </summary>
    protected Dictionary<int, object?> Values => _values ??= new();

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs when a property value is changing.
    /// </summary>
    public event PropertyChangingEventHandler? PropertyChanging;

    public static CorePropertyInitializationHelper<T, TOwner> ConfigureProperty<T, TOwner>(string name)
    {
        return new CorePropertyInitializationHelper<T, TOwner>(name);
    }

    public TValue GetValue<TValue>(CoreProperty<TValue> property)
    {
        Type ownerType = GetType();
        if (ValidateOwnerType(property, ownerType))
        {
            throw new ElementException("Owner does not match.");
        }

        if (property is StaticProperty<TValue> staticProperty)
        {
            return staticProperty.RouteGetTypedValue(this)!;
        }

        if (!Values.ContainsKey(property.Id))
        {
            return (TValue)property.GetMetadata(ownerType).DefaultValue!;
        }

        return (TValue)Values[property.Id]!;
    }

    public object? GetValue(CoreProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.RouteGetValue(this);
    }

    public void SetValue<TValue>(CoreProperty<TValue> property, TValue? value)
    {
        if (value != null && !value.GetType().IsAssignableTo(property.PropertyType))
            throw new ElementException($"{nameof(value)} of type {value.GetType().Name} cannot be assigned to type {property.PropertyType}.");

        Type ownerType = GetType();
        if (ValidateOwnerType(property, ownerType)) throw new ElementException("Owner does not match.");

        if (property is StaticProperty<TValue> staticProperty)
        {
            TValue? oldValue = staticProperty.RouteGetTypedValue(this);
            if (!EqualityComparer<TValue>.Default.Equals(oldValue, value))
            {
                staticProperty.RouteSetTypedValue(this, value);
            }
        }
        else
        {
            CorePropertyMetadata metadata = property.GetMetadata(ownerType);
            if (!AddIfNotExist(property, metadata, value))
            {
                object? oldValue = Values[property.Id];
                object? newValue = value;

                if (!RuntimeHelpers.Equals(oldValue, newValue))
                {
                    RaisePropertyChanging(property, metadata);
                    Values[property.Id] = newValue;
                    RaisePropertyChanged(property, metadata, value, (TValue?)oldValue);
                }
            }
        }
    }

    public void SetValue(CoreProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.RouteSetValue(this, value!);
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
                CorePropertyMetadata metadata = item.GetMetadata(ownerType);
                string? jsonName = metadata.GetValueOrDefault<string>(PropertyMetaTableKeys.JsonName);
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
            CorePropertyMetadata metadata = item.GetMetadata(ownerType);
            string? jsonName = metadata.GetValueOrDefault<string>(PropertyMetaTableKeys.JsonName);
            if (jsonName != null)
            {
                object? obj = GetValue(item);
                object? def = metadata.DefaultValue;

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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }

    protected void OnPropertyChanging([CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanging(new PropertyChangingEventArgs(propertyName));
    }

    protected void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    protected void OnPropertyChanging(PropertyChangingEventArgs args)
    {
        PropertyChanging?.Invoke(this, args);
    }

    protected bool SetAndRaise<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            OnPropertyChanging(propertyName);
            field = value;
            OnPropertyChanged(propertyName);

            return true;
        }
        else
        {
            return false;
        }
    }

    protected bool SetAndRaise<T>(CoreProperty<T> property, ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            CorePropertyMetadata metadata = property.GetMetadata(GetType());
            RaisePropertyChanging(property, metadata);

            T old = field;
            field = value;

            RaisePropertyChanged(property, metadata, value, old);

            return true;
        }
        else
        {
            return false;
        }
    }

    // オーナーの型が一致しない場合はtrue
    private static bool ValidateOwnerType(CoreProperty property, Type ownerType)
    {
        return !ownerType.IsAssignableTo(property.OwnerType);
    }

    // 追加した場合はtrue
    private bool AddIfNotExist<TValue>(CoreProperty<TValue> property, CorePropertyMetadata metadata, TValue? value)
    {
        if (!Values.ContainsKey(property.Id))
        {
            object? boxed = value;
            RaisePropertyChanging(property, metadata);

            Values.Add(property.Id, boxed);

            RaisePropertyChanged(property, metadata, value, default);

            return true;
        }

        return false;
    }

    private void RaisePropertyChanged<T>(CoreProperty<T> property, CorePropertyMetadata metadata, T? newValue, T? oldValue)
    {
        if (this is ILogicalElement logicalElement)
        {
            if (oldValue is ILogicalElement oldLogical)
            {
                oldLogical.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(logicalElement, null));
            }

            if (newValue is ILogicalElement newLogical)
            {
                newLogical.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(null, logicalElement));
            }
        }

        PropertyObservability observability = metadata.Observability;
        if (observability.HasFlag(PropertyObservability.Changed))
        {
            var eventArgs = new CorePropertyChangedEventArgs<T>(this, property, newValue, oldValue);
            property.NotifyChanged(eventArgs);

            OnPropertyChanged(eventArgs);
        }
    }

    private void RaisePropertyChanging(CoreProperty property, CorePropertyMetadata metadata)
    {
        PropertyObservability observability = metadata.Observability;
        if (observability.HasFlag(PropertyObservability.Changing))
        {
            OnPropertyChanging(property.Name);
        }
    }
}
