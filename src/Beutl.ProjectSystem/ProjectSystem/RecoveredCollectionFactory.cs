using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Beutl.ProjectSystem;

internal static class RecoveredCollectionFactory
{
    public static object? RebuildDictionary(IDictionary source, DictionaryEntry[] entries)
    {
        Type wrapperType = source.GetType();
        if (!wrapperType.IsGenericType || wrapperType.GetGenericTypeDefinition() != typeof(ReadOnlyDictionary<,>))
            return null;

        // The protected framework property preserves the backing dictionary's comparison policy.
        if (wrapperType.GetProperty("Dictionary", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(source) is not IDictionary backing) return null;
        Type backingType = backing.GetType();
        if (!backingType.IsGenericType) return null;
        Type definition = backingType.GetGenericTypeDefinition();
        if (definition != typeof(Dictionary<,>) && definition != typeof(SortedDictionary<,>)
            && definition != typeof(SortedList<,>)) return null;
        object? comparer = backing.GetType().GetProperty("Comparer")?.GetValue(backing);
        object? copy = comparer is null
            ? Activator.CreateInstance(backing.GetType())
            : backing.GetType().GetConstructors()
                .FirstOrDefault(c => c.GetParameters() is [var p] && p.ParameterType.IsInstanceOfType(comparer))
                ?.Invoke([comparer]);
        if (copy is not IDictionary dictionary || dictionary.IsReadOnly) return null;
        foreach (DictionaryEntry entry in entries) dictionary.Add(entry.Key, entry.Value);
        return source.GetType().GetConstructors()
            .FirstOrDefault(c => c.GetParameters() is [var p] && p.ParameterType.IsInstanceOfType(dictionary))
            ?.Invoke([dictionary]);
    }

    public static object? RebuildEnumerable(IEnumerable source, object?[] items)
    {
        Type sourceType = source.GetType();
        // Arbitrary collection constructors may discard plugin state; those types opt in via IReferenceRewritable.
        if (!sourceType.IsGenericType) return null;
        Type definition = sourceType.GetGenericTypeDefinition();
        if (definition != typeof(HashSet<>) && definition != typeof(SortedSet<>)
            && definition != typeof(Queue<>) && definition != typeof(Stack<>)) return null;
        Type? elementType = sourceType.GetInterfaces()
            .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
        if (elementType == null) return null;
        Array array = Array.CreateInstance(elementType, items.Length);
        bool reverse = sourceType.IsGenericType && sourceType.GetGenericTypeDefinition() == typeof(Stack<>);
        for (int i = 0; i < items.Length; i++) array.SetValue(items[reverse ? items.Length - i - 1 : i], i);
        object? comparer = sourceType.GetProperty("Comparer")?.GetValue(source)
                           ?? sourceType.GetProperty("KeyComparer")?.GetValue(source);
        foreach (ConstructorInfo constructor in sourceType.GetConstructors())
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (comparer != null && parameters is [var values, var comparison]
                && values.ParameterType.IsInstanceOfType(array)
                && comparison.ParameterType.IsInstanceOfType(comparer))
                return constructor.Invoke([array, comparer]);
            if (comparer == null && parameters is [var value] && value.ParameterType.IsInstanceOfType(array))
                return constructor.Invoke([array]);
        }

        return null;
    }
}
