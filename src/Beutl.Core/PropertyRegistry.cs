using System.Runtime.CompilerServices;

namespace Beutl;

public static class PropertyRegistry
{
    private static readonly Dictionary<int, CoreProperty> s_properties = [];
    private static readonly Dictionary<Type, Dictionary<int, CoreProperty>> s_registered = [];
    private static readonly Dictionary<Type, Dictionary<int, CoreProperty>> s_attached = [];
    private static readonly Dictionary<Type, List<CoreProperty>> s_registeredCache = [];
    private static readonly Dictionary<Type, List<CoreProperty>> s_attachedCache = [];

    public static IReadOnlyList<CoreProperty> GetRegistered(Type type)
    {
        lock (s_properties)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (s_registeredCache.TryGetValue(type, out List<CoreProperty>? result))
            {
                return result;
            }

            Type? t = type;
            result = [];

            while (t != null)
            {
                RuntimeHelpers.RunClassConstructor(t.TypeHandle);

                if (s_registered.TryGetValue(t, out Dictionary<int, CoreProperty>? registered))
                {
                    result.AddRange(registered.Values);
                }

                t = t.BaseType;
            }

            s_registeredCache.Add(type, result);
            return result;
        }
    }

    public static IReadOnlyList<CoreProperty> GetRegisteredAttached(Type type)
    {
        lock (s_properties)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (s_attachedCache.TryGetValue(type, out List<CoreProperty>? result))
            {
                return result;
            }

            Type? t = type;
            result = [];

            while (t != null)
            {
                if (s_attached.TryGetValue(t, out Dictionary<int, CoreProperty>? attached))
                {
                    result.AddRange(attached.Values);
                }

                t = t.BaseType;
            }

            s_attachedCache.Add(type, result);
            return result;
        }
    }

    public static CoreProperty? FindRegistered(Type type, string name)
    {
        lock (s_properties)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(name);
            if (name.Contains('.'))
            {
                throw new InvalidOperationException("Attached properties not supported.");
            }

            IReadOnlyList<CoreProperty> registered = GetRegistered(type);
            int registeredCount = registered.Count;

            for (int i = 0; i < registeredCount; i++)
            {
                CoreProperty x = registered[i];

                if (x.Name == name)
                {
                    return x;
                }
            }

            return null;
        }
    }

    public static CoreProperty? FindRegistered(ICoreObject o, string name)
    {
        lock (s_properties)
        {
            ArgumentNullException.ThrowIfNull(o);
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or empty.", nameof(name));
            }

            return FindRegistered(o.GetType(), name);
        }
    }

    public static CoreProperty? FindRegistered(int id)
    {
        lock (s_properties)
        {
            return id < s_properties.Count ? s_properties[id] : null;
        }
    }

    public static bool IsRegistered(Type type, CoreProperty property)
    {
        static bool ContainsProperty(IReadOnlyList<CoreProperty> properties, CoreProperty property)
        {
            int propertiesCount = properties.Count;

            for (int i = 0; i < propertiesCount; i++)
            {
                if (properties[i] == property)
                {
                    return true;
                }
            }

            return false;
        }

        lock (s_properties)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(property);

            return ContainsProperty(GetRegistered(type), property) ||
                   ContainsProperty(GetRegisteredAttached(type), property);
        }
    }

    public static bool IsRegistered(object o, CoreProperty property)
    {
        lock (s_properties)
        {
            ArgumentNullException.ThrowIfNull(o);
            ArgumentNullException.ThrowIfNull(property);

            return IsRegistered(o.GetType(), property);
        }
    }

    public static void Register(Type type, CoreProperty property)
    {
        lock (s_properties)
        {
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(type);

            if (!s_registered.TryGetValue(type, out Dictionary<int, CoreProperty>? inner))
            {
                inner = new Dictionary<int, CoreProperty>
                {
                    { property.Id, property },
                };
                s_registered.Add(type, inner);
            }
            else
            {
                inner.TryAdd(property.Id, property);
            }

            s_properties.TryAdd(property.Id, property);

            s_registeredCache.Clear();
        }
    }

    //public static void RegisterAttached(Type type, CoreProperty property)
    //{
    //    if (!property.IsAttached)
    //    {
    //        throw new InvalidOperationException("Cannot register a non-attached property as attached.");
    //    }

    //    if (!s_attached.TryGetValue(type, out Dictionary<int, CoreProperty>? inner))
    //    {
    //        inner = new Dictionary<int, CoreProperty>
    //        {
    //            { property.Id, property },
    //        };
    //        s_attached.Add(type, inner);
    //    }
    //    else
    //    {
    //        inner.Add(property.Id, property);
    //    }

    //    if (!s_properties.ContainsKey(property.Id))
    //    {
    //        s_properties.Add(property.Id, property);
    //    }

    //    s_attachedCache.Clear();
    //}
}
