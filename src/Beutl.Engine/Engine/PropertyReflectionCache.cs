using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.Validation;
using LinqExpression = System.Linq.Expressions.Expression;

namespace Beutl.Engine;

public class PropertyCacheEntry
{
    public readonly ConcurrentDictionary<PropertyInfo, Func<object, IProperty?>> Accessors = new();
    public readonly ConcurrentDictionary<string, Attribute[]> Attributes = new();
    public readonly ConcurrentDictionary<string, IValidator> Validators = new();
}

public static class PropertyReflectionCache
{
    public static readonly ConditionalWeakTable<Type, PropertyCacheEntry> Cache = [];

    static PropertyReflectionCache()
    {
        TypeUnloadNotifier.TypesUnloading += Unregister;
    }

    public static Func<object, IProperty?> GetOrCreateAccessor(Type type, PropertyInfo pi)
    {
        var accessors = Cache.GetValue(type, _ => new PropertyCacheEntry()).Accessors;
        if (!accessors.TryGetValue(pi, out var func))
        {
            var param = LinqExpression.Parameter(typeof(object), "o");
            var cast = LinqExpression.Convert(param, type);
            var propertyAccess = LinqExpression.Property(cast, pi);
            var convertResult = LinqExpression.Convert(propertyAccess, typeof(IProperty));
            var lambda = LinqExpression.Lambda<Func<object, IProperty?>>(convertResult, param);
            func = lambda.Compile();
            accessors[pi] = func;
        }

        return func;
    }

    public static Attribute[] GetOrCreateAttributes(Type type, string name, Func<Attribute[]> factory)
    {
        var attrs = Cache.GetValue(type, _ => new PropertyCacheEntry()).Attributes;
        return attrs.GetOrAdd(name, _ => factory());
    }

    public static IValidator GetOrCreateValidator(Type type, string name, Func<IValidator> factory)
    {
        var validators = Cache.GetValue(type, _ => new PropertyCacheEntry()).Validators;
        return validators.GetOrAdd(name, _ => factory());
    }

    private static void Unregister(Type[] types)
    {
        foreach (Type type in types)
        {
            Cache.Remove(type);
        }

        foreach (KeyValuePair<Type, PropertyCacheEntry> kvp in Cache.ToArray())
        {
            foreach (Type t in types)
            {
                if (TypeUnloadNotifier.ContainsTypeRecursive(kvp.Key, t))
                {
                    Cache.Remove(kvp.Key);
                }
            }
        }
    }
}
