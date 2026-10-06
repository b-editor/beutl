using System.Collections;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

internal static class SerializedGraphTraversal
{
    public static IEnumerable<object> Enumerate(object root)
    {
        var result = new List<object>();
        Visit(root, "$", (value, _) =>
        {
            result.Add(value);
            return false;
        });
        return result;
    }

    public static bool Visit(
        object? root,
        string rootPath,
        Func<object, string, bool> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return VisitCore(root, rootPath, visited, visitor);
    }

    private static bool VisitCore(
        object? value,
        string path,
        ISet<object> visited,
        Func<object, string, bool> visitor)
    {
        if (value is null or string)
        {
            return false;
        }

        if (value is IOptional optional)
        {
            return optional.HasValue
                   && VisitCore(optional.ToObject().Value, path, visited, visitor);
        }

        if (!value.GetType().IsValueType && !visited.Add(value))
        {
            return false;
        }

        if (value is CoreObject or IFallback)
        {
            if (visitor(value, path))
            {
                return true;
            }
        }

        if (value is CoreObject coreObject)
        {
            return VisitCoreObjectMembers(coreObject, path, visited, visitor);
        }

        if (value is IDictionary dictionary)
        {
            return VisitItems(dictionary.Values, path, visited, visitor);
        }

        if (value is IEnumerable enumerable)
        {
            return VisitItems(enumerable, path, visited, visitor);
        }

        return false;
    }

    private static bool VisitCoreObjectMembers(
        CoreObject coreObject,
        string path,
        ISet<object> visited,
        Func<object, string, bool> visitor)
    {
        switch (coreObject)
        {
            case Scene scene:
                for (int i = 0; i < scene.Children.Count; i++)
                {
                    if (VisitCore(scene.Children[i], $"{path}/Elements[{i}]", visited, visitor))
                    {
                        return true;
                    }
                }
                break;

            case Element element:
                for (int i = 0; i < element.Objects.Count; i++)
                {
                    if (VisitCore(element.Objects[i], $"{path}/Objects[{i}]", visited, visitor))
                    {
                        return true;
                    }
                }
                break;

            case EngineObject engineObject:
                if (VisitEngineProperties(engineObject, path, visited, visitor))
                {
                    return true;
                }

                break;
        }

        foreach (CoreProperty property in PropertyRegistry.GetRegistered(coreObject.GetType()))
        {
            if (property.GetMetadata<CorePropertyMetadata>(coreObject.GetType()).ShouldSerialize
                && VisitCore(coreObject.GetValue(property), $"{path}/{property.Name}", visited, visitor))
            {
                return true;
            }
        }

        return coreObject is IHierarchical hierarchical
               && VisitItems(hierarchical.HierarchicalChildren, $"{path}/HierarchicalChildren", visited, visitor);
    }

    private static bool VisitEngineProperties(
        EngineObject engineObject,
        string path,
        ISet<object> visited,
        Func<object, string, bool> visitor)
    {
        foreach (IProperty property in engineObject.Properties)
        {
            if (VisitCore(property.CurrentValue, $"{path}/{property.Name}", visited, visitor))
            {
                return true;
            }

            if (property.Animation is IKeyFrameAnimation animation)
            {
                int index = 0;
                foreach (IKeyFrame keyFrame in animation.KeyFrames)
                {
                    string keyFramePath
                        = $"{path}/Animations/{property.Name}/KeyFrames[{index}]";
                    if (VisitCore(keyFrame, keyFramePath, visited, visitor)
                        || VisitCore(keyFrame.Value, $"{keyFramePath}/Value", visited, visitor))
                    {
                        return true;
                    }

                    index++;
                }
            }
        }

        return false;
    }

    // Visits each item at "{pathPrefix}[{index}]".
    private static bool VisitItems(
        IEnumerable items,
        string pathPrefix,
        ISet<object> visited,
        Func<object, string, bool> visitor)
    {
        int index = 0;
        foreach (object? item in items)
        {
            if (VisitCore(item, $"{pathPrefix}[{index}]", visited, visitor))
            {
                return true;
            }

            index++;
        }

        return false;
    }
}
