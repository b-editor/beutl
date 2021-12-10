using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using BEditorNext.Collections;
using BEditorNext.Utilities;

namespace BEditorNext;

/// <summary>
/// Provides the base class for all hierarchal elements.
/// </summary>
public interface IElement : ILogicalElement, IJsonSerializable, INotifyPropertyChanged, INotifyPropertyChanging
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets the parent element.
    /// </summary>
    IElement? Parent { get; }

    /// <summary>
    /// Gets the children.
    /// </summary>
    IElementList Children { get; }
}

/// <summary>
/// Provides the base class for all hierarchal elements.
/// </summary>
public abstract class Element : IElement
{
    /// <summary>
    /// Defines the <see cref="Id"/> property.
    /// </summary>
    public static readonly PropertyDefine<Guid> IdProperty;

    /// <summary>
    /// Defines the <see cref="Name"/> property.
    /// </summary>
    public static readonly PropertyDefine<string> NameProperty;

    /// <summary>
    /// The last JsonNode that was used.
    /// </summary>
    protected JsonNode? JsonNode;
    private readonly ElementList _children;
    private Dictionary<int, object?>? _values = new();
    private Element? _parent;

    static Element()
    {
        IdProperty = RegisterProperty<Guid, Element>(nameof(Id))
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .DefaultValue(Guid.Empty);

        NameProperty = RegisterProperty<string, Element>(nameof(Name))
            .NotifyPropertyChanging(true)
            .NotifyPropertyChanged(true)
            .DefaultValue(string.Empty)
            .JsonName("name");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Element"/> class.
    /// </summary>
    protected Element()
    {
        _children = new(this);
        Id = Guid.NewGuid();
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
    /// Gets or sets the parent element.
    /// </summary>
    public Element? Parent
    {
        get => _parent;
        set
        {
            if (_parent != value)
            {
                RaiseParentChanging(_parent, value);
                _parent = value;
                RaiseParentChanged();
            }
        }
    }

    /// <summary>
    /// Gets the children.
    /// </summary>
    public IElementList Children => _children;

    /// <summary>
    /// Gets the dynamic property values.
    /// </summary>
    protected Dictionary<int, object?> Values => _values ??= new();

    ILogicalElement? ILogicalElement.LogicalParent => Parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Children;

    IElement? IElement.Parent => Parent;

    /// <summary>
    /// Occurs while changing the parent element
    /// </summary>
    public event EventHandler<ParentChangingEventArgs>? ParentChanging;

    /// <summary>
    /// Occurs when the parent element changes.
    /// </summary>
    public event EventHandler? ParentChanged;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs when a property value is changing.
    /// </summary>
    public event PropertyChangingEventHandler? PropertyChanging;

    /// <summary>
    /// Registers a property by specifying a getter and a setter.
    /// </summary>
    /// <typeparam name="T">The type of the property's value.</typeparam>
    /// <typeparam name="TOwner">The type of property's owner.</typeparam>
    /// <param name="name">The name of property.</param>
    /// <param name="setter">Delegate to set the value of a property.</param>
    /// <param name="getter">Delegate to get the value of a property.</param>
    /// <returns>Returns the property define.</returns>
    public static PropertyDefine<T> RegisterProperty<T, TOwner>(string name, Action<TOwner, T> setter, Func<TOwner, T> getter)
    {
        var metaTable = new Dictionary<string, object>
        {
            { PropertyMetaTableKeys.Name, name },
            { PropertyMetaTableKeys.OwnerType, typeof(TOwner) },
            { PropertyMetaTableKeys.GenericsSetter, setter },
            { PropertyMetaTableKeys.GenericsGetter, getter },
            { PropertyMetaTableKeys.Setter, new Action<PropertyDefine, object, T>((props, owner, obj) =>
                {
                    var accessor = (Action<TOwner, T>)props.MetaTable[PropertyMetaTableKeys.GenericsSetter];
                    accessor((TOwner)owner, obj);
                })
            },
            { PropertyMetaTableKeys.Getter, new Func<PropertyDefine, object, T>((props, owner) =>
                {
                    var accessor = (Func<TOwner, T>)props.MetaTable[PropertyMetaTableKeys.GenericsGetter];
                    return accessor((TOwner)owner);
                })
            },
        };

        var define = new PropertyDefine<T>(metaTable);
        PropertyRegistry.Register(define.OwnerType, define);
        return define;
    }

    /// <summary>
    /// Registers a property by specifying a getter.
    /// </summary>
    /// <typeparam name="T">The type of the property's value.</typeparam>
    /// <typeparam name="TOwner">The type of property's owner.</typeparam>
    /// <param name="name">The name of property.</param>
    /// <param name="getter">Delegate to get the value of a property.</param>
    /// <returns>Returns the property define.</returns>
    public static PropertyDefine<T> RegisterProperty<T, TOwner>(string name, Func<TOwner, T> getter)
    {
        var metaTable = new Dictionary<string, object>
        {
            { PropertyMetaTableKeys.Name, name },
            { PropertyMetaTableKeys.OwnerType, typeof(TOwner) },
            { PropertyMetaTableKeys.GenericsGetter, getter },
            { PropertyMetaTableKeys.Getter, new Func<PropertyDefine, object, T>((props, owner) =>
                {
                    var accessor = (Func<TOwner, T>)props.MetaTable[PropertyMetaTableKeys.GenericsGetter];
                    return accessor((TOwner)owner);
                })
            },
        };

        var define = new PropertyDefine<T>(metaTable);
        PropertyRegistry.Register(define.OwnerType, define);
        return define;
    }

    /// <summary>
    /// Registers a dynamic property.
    /// </summary>
    /// <typeparam name="T">The type of the property's value.</typeparam>
    /// <typeparam name="TOwner">The type of property's owner.</typeparam>
    /// <param name="name">The name of property.</param>
    /// <returns>Returns the property define.</returns>
    public static PropertyDefine<T> RegisterProperty<T, TOwner>(string name)
    {
        var metaTable = new Dictionary<string, object>
        {
            { PropertyMetaTableKeys.Name, name },
            { PropertyMetaTableKeys.OwnerType, typeof(TOwner) },
        };

        var define = new PropertyDefine<T>(metaTable);
        PropertyRegistry.Register(define.OwnerType, define);
        return define;
    }

    public TValue GetValue<TValue>(PropertyDefine<TValue> property)
    {
        if (CheckOwnerType(property))
        {
            throw new ElementException("Owner does not match.");
        }

        if (property.HasGetter)
        {
            return (TValue?)property.GetGetter().Invoke(property, this) ?? property.GetDefaultValue()!;
        }

        if (!Values.ContainsKey(property.Id))
        {
            return property.GetDefaultValue()!;
        }

        return (TValue)Values[property.Id]!;
    }

    public object? GetValue(PropertyDefine property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.RouteGetValue(this);
    }

    public void SetValue<TValue>(PropertyDefine<TValue> property, TValue? value)
    {
        if (value != null && CheckValueType(property, value))
            throw new ElementException($"{nameof(value)} of type {value.GetType().Name} cannot be assigned to type {property.PropertyType}.");

        if (CheckOwnerType(property)) throw new ElementException("Owner does not match.");

        object? oldValue = null;
        object? newValue = value;
        if (property.HasSetter)
        {
            oldValue = property.GetGetter().Invoke(property, this);
            if (!RuntimeHelpers.Equals(oldValue, newValue))
            {
                property.GetSetter().Invoke(property, this, value!);
            }
        }
        else if (!AddIfNotExist(property, value))
        {
            oldValue = Values[property.Id];

            if (!RuntimeHelpers.Equals(oldValue, newValue))
            {
                RaisePropertyChanging(property);
                Values[property.Id] = newValue;
                RaisePropertyChanged(property, value, (TValue?)oldValue);
            }
        }
    }

    public void SetValue(PropertyDefine property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.RouteSetValue(this, value!);
    }

    [MemberNotNull("JsonNode")]
    public virtual void FromJson(JsonNode json)
    {
        JsonNode = json;

        // Todo: 例外処理
        if (json is JsonObject obj)
        {
            IEnumerable<PropertyDefine> list = PropertyRegistry.GetRegistered(GetType())
                .Where(p => p.GetJsonName() != null);

            foreach (PropertyDefine item in list)
            {
                if (obj.TryGetPropertyValue(item.GetJsonName()!, out JsonNode? jsonNode) && jsonNode != null)
                {
                    Type type = item.PropertyType;

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

        if (JsonNode is JsonObject jsonNodeObj)
        {
            json = jsonNodeObj;
        }
        else
        {
            json = new JsonObject();
            JsonNode = json;
        }

        IEnumerable<PropertyDefine> list = PropertyRegistry.GetRegistered(GetType())
            .Where(p => p.GetJsonName() != null);

        foreach (PropertyDefine item in list)
        {
            object? obj = GetValue(item);
            object? def = item.GetDefaultValue();

            // デフォルトの値と取得した値が同じ場合、保存しない
            if (obj != null && def != null && RuntimeHelpers.Equals(def, obj))
            {
                continue;
            }

            if (obj is IJsonSerializable child)
            {
                json[item.GetJsonName()!] = child.ToJson();
            }
            else
            {
                json[item.GetJsonName()!] = JsonValue.Create(obj);
            }
        }

        return json;
    }

    internal void RaiseParentChanging(Element? oldValue, Element? newValue)
    {
        ParentChanging?.Invoke(this, new ParentChangingEventArgs(oldValue, newValue));
    }

    internal void RaiseParentChanged()
    {
        ParentChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetParent(Element? parent)
    {
        _parent = parent;
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

    protected bool SetAndRaise<T>(PropertyDefine<T> property, ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            RaisePropertyChanging(property);

            T old = field;
            field = value;

            RaisePropertyChanged(property, value, old);

            return true;
        }
        else
        {
            return false;
        }
    }

    // 値の型が一致しない場合はtrue
    private static bool CheckValueType(PropertyDefine property, object value)
    {
        Type valueType = value.GetType();

        return !valueType.IsAssignableTo(property.PropertyType);
    }

    // オーナーの型が一致しない場合はtrue
    private bool CheckOwnerType(PropertyDefine property)
    {
        Type ownerType = GetType();

        return !ownerType.IsAssignableTo(property.OwnerType);
    }

    // 追加した場合はtrue
    private bool AddIfNotExist<TValue>(PropertyDefine<TValue> property, TValue? value)
    {
        if (!Values.ContainsKey(property.Id))
        {
            object? boxed = value;
            RaisePropertyChanging(property);

            Values.Add(property.Id, boxed);

            RaisePropertyChanged(property, value, default);

            return true;
        }

        return false;
    }

    private void RaisePropertyChanged<T>(PropertyDefine<T> property, T? newValue, T? oldValue)
    {
        if (property.GetNotifyPropertyChanged())
        {
            var eventArgs = new ElementPropertyChangedEventArgs<T>(this, property, newValue, oldValue);
            property.NotifyChanged(eventArgs);

            OnPropertyChanged(eventArgs);
        }
    }

    private void RaisePropertyChanging(PropertyDefine property)
    {
        if (property.GetNotifyPropertyChanging())
        {
            OnPropertyChanging(property.Name);
        }
    }
}
