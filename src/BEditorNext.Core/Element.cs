using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
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
                ParentChanging?.Invoke(this, new ParentChangingEventArgs(_parent, value));
                _parent = value;
                ParentChanged?.Invoke(this, EventArgs.Empty);
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
            { PropertyMetaTableKeys.Setter, new Action<PropertyDefine, object, object>((props, owner, obj) =>
                {
                    var accessor = (Action<TOwner, T>)props.MetaTable[PropertyMetaTableKeys.GenericsSetter];
                    accessor((TOwner)owner, (T)obj);
                })
            },
            { PropertyMetaTableKeys.Getter, new Func<PropertyDefine, object, object?>((props, owner) =>
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
            { PropertyMetaTableKeys.Getter, new Func<PropertyDefine, object, object?>((props, owner) =>
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
            { PropertyMetaTableKeys.IsAutomatic, true },
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

        if (!property.IsAutomatic)
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
        if (CheckOwnerType(property))
        {
            throw new ElementException("Owner does not match.");
        }

        if (!property.IsAutomatic)
        {
            return property.GetGetter().Invoke(property, this) ?? property.GetDefaultValue();
        }

        if (!Values.ContainsKey(property.Id))
        {
            return property.GetDefaultValue();
        }

        return Values[property.Id];
    }

    public void SetValue<TValue>(PropertyDefine<TValue> property, TValue value)
    {
        if (value != null && CheckValueType(property, value))
            throw new ElementException($"{nameof(value)} of type {value.GetType().Name} cannot be assigned to type {property.PropertyType}.");

        if (CheckOwnerType(property)) throw new ElementException("Owner does not match.");

        object? old = null;
        if (!property.IsAutomatic && property.MetaTable.ContainsKey(PropertyMetaTableKeys.Setter))
        {
            old = property.GetGetter().Invoke(property, this);
            if (!RuntimeHelpers.Equals(old, value))
            {
                property.GetSetter().Invoke(property, this, value!);
            }
        }
        else if (!AddIfNotExist(property, value))
        {
            old = Values[property.Id];
            if (!RuntimeHelpers.Equals(old, value))
            {
                RaisePropertyChanging(property);
                Values[property.Id] = value;
                RaisePropertyChanged(property);
            }
        }
    }

    public void SetValue(PropertyDefine property, object? value)
    {
        if (value != null && CheckValueType(property, value))
            throw new ElementException($"{nameof(value)} of type {value.GetType().Name} cannot be assigned to type {property.PropertyType}.");

        if (CheckOwnerType(property)) throw new ElementException("Owner does not match.");

        object? old = null;
        if (!property.IsAutomatic && property.MetaTable.ContainsKey(PropertyMetaTableKeys.Setter))
        {
            old = property.GetGetter().Invoke(property, this);
            if (!RuntimeHelpers.Equals(old, value))
            {
                property.GetSetter().Invoke(property, this, value!);
            }
        }
        else if (!AddIfNotExist(property, value))
        {
            old = Values[property.Id];
            if (!RuntimeHelpers.Equals(old, value))
            {
                RaisePropertyChanging(property);
                Values[property.Id] = value;
                RaisePropertyChanged(property);
            }
        }
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
                if (obj.TryGetPropertyValue(item.GetJsonName()!, out JsonNode? jsonNode))
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
                        // Todo: ここキャッシュする
                        MethodInfo mi = typeof(JsonNode).GetMethod("GetValue")!;
                        mi = mi.MakeGenericMethod(type);

                        object? sobj = mi.Invoke(jsonNode, null);

                        SetValue(item, sobj);
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

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected void OnPropertyChanging([CallerMemberName] string? propertyName = null)
    {
        PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
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
            if (property.GetNotifyPropertyChanging())
            {
                OnPropertyChanging(property.Name);
            }

            field = value;

            if (property.GetNotifyPropertyChanged())
            {
                OnPropertyChanged(property.Name);
            }

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
    private bool AddIfNotExist(PropertyDefine property, object? value)
    {
        if (!Values.ContainsKey(property.Id))
        {
            RaisePropertyChanging(property);

            Values.Add(property.Id, value);

            RaisePropertyChanged(property);

            return true;
        }

        return false;
    }

    private bool AddIfNotExist<TValue>(PropertyDefine property, TValue value)
    {
        if (!Values.ContainsKey(property.Id))
        {
            RaisePropertyChanging(property);

            Values.Add(property.Id, value);

            RaisePropertyChanged(property);

            return true;
        }

        return false;
    }

    private void RaisePropertyChanged(PropertyDefine property)
    {
        if (property.GetNotifyPropertyChanged())
        {
            OnPropertyChanged(property.Name);
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
