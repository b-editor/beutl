using System.Collections.Concurrent;

namespace Beutl.Serialization;

internal static class DefaultValueHelpers
{
    private static readonly ConcurrentDictionary<Type, Type> s_optionalToGenericTypeCache = new();

    private static Type? GetOptionalGenericType(Type type)
    {
        if (!s_optionalToGenericTypeCache.TryGetValue(type, out Type? genericType))
        {
            if (type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(Optional<>)
                && type.GetGenericArguments().FirstOrDefault() is { } uType)
            {
                genericType = uType;
                s_optionalToGenericTypeCache.TryAdd(type, genericType);
            }
        }

        return genericType;
    }

    public static T? DefaultOrOptional<T>()
    {
        Type expectType = typeof(T);
        if (GetOptionalGenericType(expectType) is { } genericType)
        {
            return (T?)Activator.CreateInstance(expectType, GetDefault(genericType));
        }

        return default;
    }

    public static object? GetDefault(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    public static object? GetDefaultOrOptional(Type type)
    {
        if (GetOptionalGenericType(type) is { } genericType)
        {
            return Activator.CreateInstance(type, GetDefault(genericType));
        }

        return GetDefault(type);
    }
}
