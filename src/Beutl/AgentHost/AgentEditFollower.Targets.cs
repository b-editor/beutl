using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Infrastructure;
using Beutl.Editor.Operations;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentHost;

public sealed partial class AgentEditFollower
{
    private static EditTarget? ResolveTarget(Scene scene, IReadOnlyList<ChangeOperation> operations)
    {
        // apply_edit deserializes the nested objects (brushes, transforms, effects) of every element
        // it writes, so its transaction also swaps untouched values for equal copies, and the copies
        // then take on their element's time range and layer. Skip both to find the real change.
        var swappedIn = new HashSet<CoreObject>(ReferenceEqualityComparer.Instance);
        EditTarget? removed = null;
        foreach (ChangeOperation operation in operations)
        {
            switch (operation)
            {
                case IUpdatePropertyValueOperation update:
                    if (IsWithin(update.Object, swappedIn))
                        break;
                    if (update.NewValue is CoreObject replacement)
                    {
                        swappedIn.Add(replacement);
                        if (IsEqualCopy(update.OldValue, replacement))
                            break;
                    }

                    if (CreateTarget(scene, update.Object, PropertyNameOf(update.PropertyPath)) is { } updated)
                        return updated;
                    break;

                case ICollectionChangeOperation collection:
                    foreach (object? item in collection.Items)
                    {
                        // Inserted items are attached; removed ones are not.
                        if (item is CoreObject child && CreateTarget(scene, child, null) is { } inserted)
                            return inserted;
                        if (item is Element gone)
                            removed ??= new EditTarget(null, null, null, null, gone.Range, gone.ZIndex.Value);
                    }

                    if (CreateTarget(scene, collection.Object, PropertyNameOf(collection.PropertyPath)) is { } owner)
                        return owner;
                    break;
            }
        }

        return removed;
    }

    private static bool IsWithin(CoreObject obj, HashSet<CoreObject> roots)
    {
        if (roots.Contains(obj))
            return true;

        for (IHierarchical? current = (obj as IHierarchical)?.HierarchicalParent; current is not null; current = current.HierarchicalParent)
        {
            if (current is CoreObject core && roots.Contains(core))
                return true;
        }

        return false;
    }

    private static bool IsEqualCopy(object? original, CoreObject copy)
        => original is CoreObject source
           && source.GetType() == copy.GetType()
           && JsonNode.DeepEquals(CoreSerializer.SerializeToJsonObject(source), CoreSerializer.SerializeToJsonObject(copy));

    private static EditTarget? CreateTarget(Scene scene, CoreObject obj, string? propertyName)
    {
        Element? element = obj as Element
                           ?? (obj as IHierarchical)?.EnumerateAncestors<Element>().FirstOrDefault();
        if (element is null || !ReferenceEquals(element.HierarchicalParent, scene))
            return null;

        return new EditTarget(element, obj, propertyName, KeyFrameTime(obj));
    }

    internal static MediaTime? KeyFrameTime(CoreObject obj)
    {
        if (obj is not IKeyFrame keyFrame
            || keyFrame.EnumerateAncestors<IKeyFrameAnimation>().FirstOrDefault() is not { } animation)
        {
            return null;
        }

        return animation.ToScene(keyFrame.KeyTime.Value);
    }

    private static string PropertyNameOf(string propertyPath) => PropertyPathHelper.Parse(propertyPath).Name;

    internal static MediaTime Clamp(MediaTime value, MediaTime min, MediaTime max)
        => max < min || value < min ? min : value > max ? max : value;
}
