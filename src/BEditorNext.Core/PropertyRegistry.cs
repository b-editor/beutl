using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace BEditorNext;

public static class PropertyRegistry
{
    private static readonly Dictionary<int, PropertyDefine> _properties = new();
    private static readonly Dictionary<Type, Dictionary<int, PropertyDefine>> _registered = new();
    private static readonly Dictionary<Type, Dictionary<int, PropertyDefine>> _attached = new();
    private static readonly Dictionary<Type, List<PropertyDefine>> _registeredCache = new();
    private static readonly Dictionary<Type, List<PropertyDefine>> _attachedCache = new();

    public static IReadOnlyList<PropertyDefine> GetRegistered(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (_registeredCache.TryGetValue(type, out var result))
        {
            return result;
        }

        var t = type;
        result = new List<PropertyDefine>();

        while (t != null)
        {
            RuntimeHelpers.RunClassConstructor(t.TypeHandle);

            if (_registered.TryGetValue(t, out var registered))
            {
                result.AddRange(registered.Values);
            }

            t = t.BaseType;
        }

        _registeredCache.Add(type, result);
        return result;
    }

    public static IReadOnlyList<PropertyDefine> GetRegisteredAttached(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (_attachedCache.TryGetValue(type, out var result))
        {
            return result;
        }

        var t = type;
        result = new List<PropertyDefine>();

        while (t != null)
        {
            if (_attached.TryGetValue(t, out var attached))
            {
                result.AddRange(attached.Values);
            }

            t = t.BaseType;
        }

        _attachedCache.Add(type, result);
        return result;
    }

    public static PropertyDefine? FindRegistered(Type type, string name)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (name.Contains('.'))
        {
            throw new InvalidOperationException("Attached properties not supported.");
        }

        var registered = GetRegistered(type);
        var registeredCount = registered.Count;

        for (var i = 0; i < registeredCount; i++)
        {
            var x = registered[i];

            if (x.Name == name)
            {
                return x;
            }
        }

        return null;
    }

    public static PropertyDefine? FindRegistered(IElement o, string name)
    {
        if (o is null) throw new ArgumentNullException(nameof(o));
        if (string.IsNullOrEmpty(name)) throw new ArgumentException($"'{nameof(name)}' cannot be null or empty.", nameof(name));
        return FindRegistered(o.GetType(), name);
    }

    public static PropertyDefine? FindRegistered(int id)
    {
        return id < _properties.Count ? _properties[id] : null;
    }

    public static bool IsRegistered(Type type, PropertyDefine property)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (property is null) throw new ArgumentNullException(nameof(property));

        static bool ContainsProperty(IReadOnlyList<PropertyDefine> properties, PropertyDefine property)
        {
            var propertiesCount = properties.Count;

            for (var i = 0; i < propertiesCount; i++)
            {
                if (properties[i] == property)
                {
                    return true;
                }
            }

            return false;
        }

        return ContainsProperty(GetRegistered(type), property) ||
               ContainsProperty(GetRegisteredAttached(type), property);
    }

    public static bool IsRegistered(object o, PropertyDefine property)
    {
        if (o is null) throw new ArgumentNullException(nameof(o));
        if (property is null) throw new ArgumentNullException(nameof(property));
        return IsRegistered(o.GetType(), property);
    }

    public static void Register(Type type, PropertyDefine property)
    {
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (!_registered.TryGetValue(type, out var inner))
        {
            inner = new Dictionary<int, PropertyDefine>
            {
                { property.Id, property },
            };
            _registered.Add(type, inner);
        }
        else if (!inner.ContainsKey(property.Id))
        {
            inner.Add(property.Id, property);
        }

        if (!_properties.ContainsKey(property.Id))
        {
            _properties.Add(property.Id, property);
        }

        _registeredCache.Clear();
    }

    public static void RegisterAttached(Type type, PropertyDefine property)
    {
        if (!property.IsAttached)
        {
            throw new InvalidOperationException("Cannot register a non-attached property as attached.");
        }

        if (!_attached.TryGetValue(type, out var inner))
        {
            inner = new Dictionary<int, PropertyDefine>
            {
                { property.Id, property },
            };
            _attached.Add(type, inner);
        }
        else
        {
            inner.Add(property.Id, property);
        }

        if (!_properties.ContainsKey(property.Id))
        {
            _properties.Add(property.Id, property);
        }

        _attachedCache.Clear();
    }
}
