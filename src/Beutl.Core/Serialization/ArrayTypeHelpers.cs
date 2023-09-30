using System.Collections;

namespace Beutl.Serialization;

internal static class ArrayTypeHelpers
{
    public static object ConvertArrayType(List<object?> output, Type type, Type elementType)
    {
        if (type.IsArray)
        {
            var array = Array.CreateInstance(elementType, output.Count);
            for (int i = 0; i < output.Count; i++)
            {
                array.SetValue(output[i], i);
            }

            return array;
        }
        else if (Activator.CreateInstance(type) is IList list)
        {
            foreach (object? item in output)
            {
                list.Add(item);
            }

            return list;
        }
        else
        {
            return default!;
        }
    }

    public static object ConvertDictionaryType(List<KeyValuePair<string, object?>> output, Type type, Type valueType)
    {
        if (type.IsArray)
        {
            Type kvpType = typeof(KeyValuePair<,>).MakeGenericType(typeof(string), valueType);
            var array = Array.CreateInstance(kvpType, output.Count);
            for (int i = 0; i < output.Count; i++)
            {
                KeyValuePair<string, object?> item = output[i];
                array.SetValue(Activator.CreateInstance(kvpType, item.Key, item.Value), i);
            }

            return array;
        }
        else if (Activator.CreateInstance(type) is IDictionary dict)
        {
            foreach (KeyValuePair<string, object?> item in output)
            {
                dict.Add(item.Key, item.Value);
            }

            return dict;
        }
        else
        {
            return default!;
        }
    }

    public static Type? GetElementType(Type arrayType)
    {
        if (arrayType.IsArray && arrayType.GetElementType() is Type elementType)
        {
            return elementType;
        }
        else
        {
            Type[] interfaces = arrayType.GetInterfaces();
            if (interfaces.FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                 is { } interfaceType)
            {
                return interfaceType.GetGenericArguments()[0];
            }
        }

        return null;
    }

    public static (Type? Key, Type? Value) GetEntryType(Type dictType)
    {
        if (GetElementType(dictType) is Type elementType
            && elementType.IsGenericType
            && elementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            Type[] args = elementType.GetGenericArguments();
            return (args[0], args[1]);
        }

        return default;
    }
}
