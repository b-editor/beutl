namespace Beutl.Serialization;

internal static class DefaultValueHelpers
{
    public static T? DefaultOrOptional<T>()
    {
        Type expectType = typeof(T);
        if (expectType.IsGenericType
            && expectType.GetGenericTypeDefinition() == typeof(Optional<>)
            && expectType.GetGenericArguments().FirstOrDefault() is Type uType)
        {
            // TがOptional<U>で明示的にnullが指定されている場合、
            // HasValueをtrueにValueをdefaultにする
            return (T?)Activator.CreateInstance(expectType, uType.IsValueType ? Activator.CreateInstance(uType) : null);
        }
        else
        {
            return default;
        }
    }

    public static object? GetDefault(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
