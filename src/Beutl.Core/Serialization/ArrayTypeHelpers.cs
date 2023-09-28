using System.Collections;

namespace Beutl.Serialization;

internal static class ArrayTypeHelpers
{
    public static object ConvertArrayType(List<object?> output, Type type, Type elementType)
    {
        if (type.IsArray)
        {
            var array = Array.CreateInstance(elementType, output.Count);
            output.CopyTo((object?[])array);
            return output.ToArray();
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
}
