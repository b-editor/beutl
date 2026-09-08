using System.Collections;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Beutl.ProjectSystem;

internal static class RecoveredCollectionFactory
{
    public static object? RebuildDictionary(IDictionary source, DictionaryEntry[] entries)
    {
        Type wrapperType = source.GetType();
        if (wrapperType.IsGenericType
            && (wrapperType.GetGenericTypeDefinition() == typeof(ImmutableDictionary<,>)
                || wrapperType.GetGenericTypeDefinition() == typeof(ImmutableSortedDictionary<,>)))
        {
            return typeof(RecoveredCollectionFactory)
                .GetMethod(nameof(RebuildImmutableDictionary), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(wrapperType.GetGenericArguments()).Invoke(null, [source, entries]);
        }
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
        if (definition == typeof(ImmutableArray<>) || definition == typeof(ImmutableList<>)
            || definition == typeof(ImmutableHashSet<>) || definition == typeof(ImmutableSortedSet<>)
            || definition == typeof(ImmutableQueue<>) || definition == typeof(ImmutableStack<>))
        {
            return typeof(RecoveredCollectionFactory)
                .GetMethod(nameof(RebuildImmutableEnumerable), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(sourceType.GetGenericArguments()).Invoke(null, [source, items]);
        }
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

    private static object? RebuildImmutableEnumerable<T>(IEnumerable source, object?[] items)
    {
        IEnumerable<T> values = items.Cast<T>();
        return source switch
        {
            ImmutableArray<T> => ImmutableArray.CreateRange(values),
            ImmutableList<T> => ImmutableList.CreateRange(values),
            ImmutableHashSet<T> set => ImmutableHashSet.CreateRange(set.KeyComparer, values),
            ImmutableSortedSet<T> set => ImmutableSortedSet.CreateRange(set.KeyComparer, values),
            ImmutableQueue<T> => ImmutableQueue.CreateRange(values),
            ImmutableStack<T> => ImmutableStack.CreateRange(values.Reverse()),
            _ => null,
        };
    }

    private static object? RebuildImmutableDictionary<TKey, TValue>(IDictionary source, DictionaryEntry[] entries)
        where TKey : notnull
    {
        var values = entries.Select(entry => new KeyValuePair<TKey, TValue>((TKey)entry.Key, (TValue)entry.Value!));
        return source switch
        {
            ImmutableDictionary<TKey, TValue> dictionary => ImmutableDictionary.CreateRange(
                dictionary.KeyComparer, dictionary.ValueComparer, values),
            ImmutableSortedDictionary<TKey, TValue> dictionary => ImmutableSortedDictionary.CreateRange(
                dictionary.KeyComparer, dictionary.ValueComparer, values),
            _ => null,
        };
    }
}
