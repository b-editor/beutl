using System.Collections;
using Beutl.Editor.Operations;
using Beutl.Serialization;

namespace Beutl.Editor.Observers;

internal static class OperationObserverHelpers
{
    public static HashSet<string>? GetTrackedChildNames(HashSet<string>? propertyPathsToTrack, string propertyPath)
    {
        return propertyPathsToTrack?.Where(i => i.Contains(propertyPath))
            .Select(i => i.Substring(propertyPath.Length).TrimStart('.').Split('.').First())
            .Where(i => !string.IsNullOrEmpty(i))
            .ToHashSet();
    }

    public static string AppendPath(string propertyPath, string name)
    {
        return string.IsNullOrEmpty(propertyPath)
            ? name
            : $"{propertyPath}.{name}";
    }

    public static IOperationObserver CreateCollectionObserver(
        IObserver<ChangeOperation> operations,
        IList list,
        object owner,
        string propertyPath,
        OperationSequenceGenerator sequenceNumberGenerator,
        HashSet<string>? propertyPathsToTrack)
    {
        var elementType = ArrayTypeHelpers.GetElementType(list.GetType());
        if (elementType == null)
            throw new InvalidOperationException("Could not determine the element type of the list.");
        var observerType = typeof(CollectionOperationObserver<>).MakeGenericType(elementType);

        return (IOperationObserver)Activator.CreateInstance(observerType,
            operations, list, owner,
            propertyPath, sequenceNumberGenerator, propertyPathsToTrack)!;
    }
}
