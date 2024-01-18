using System.Collections;

namespace Beutl.Serialization;

internal static class ArrayTypeHelpers
{
    private static readonly Dictionary<Type, Type> s_elementTypes = [];
    private static readonly Dictionary<Type, (Type Key, Type Value)> s_genericArgsTypes = [];

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
        Type? elementType = null;
        if (arrayType.IsArray)
        {
            elementType = arrayType.GetElementType();
        }
        else
        {
            lock (s_elementTypes)
            {
                if (!s_elementTypes.TryGetValue(arrayType, out elementType))
                {
                    Type[] interfaces = arrayType.GetInterfaces();
                    if (interfaces.FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                         is { } interfaceType)
                    {
                        elementType = interfaceType.GetGenericArguments()[0];
                        s_elementTypes.Add(arrayType, elementType);
                    }
                }
            }
        }

        return elementType;
    }

    public static (Type? Key, Type? Value) GetEntryType(Type dictType)
    {
        if (GetElementType(dictType) is Type elementType)
        {
            lock (s_genericArgsTypes)
            {
                if (!s_genericArgsTypes.TryGetValue(elementType, out (Type Key, Type Value) result))
                {
                    if (elementType.IsGenericType
                        && elementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    {
                        Type[] args = elementType.GetGenericArguments();
                        result = (args[0], args[1]);
                        s_genericArgsTypes.Add(elementType, result);
                    }
                }

                return result;
            }
        }

        return default;
    }
}
