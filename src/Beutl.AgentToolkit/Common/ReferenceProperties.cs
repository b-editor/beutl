using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Beutl.Engine;

namespace Beutl.AgentToolkit.Common;

internal sealed record ReferencePropertyDescriptor(string Name, Type ReferencedType);

// A ProjectItem is project-owned, so a property typed as one is a reference, not an inlined child.
internal static class ReferenceProperties
{
    // Weak-keyed so caching a plugin-defined owner type never roots its collectible AssemblyLoadContext.
    private static readonly ConditionalWeakTable<Type, ReferencePropertyDescriptor[]> s_byOwnerType = new();

    static ReferenceProperties()
    {
        TypeUnloadNotifier.TypesUnloading += Evict;
    }

    public static ReferencePropertyDescriptor? Describe(IProperty property)
        => IsReferenceValueType(property.ValueType, out Type? referencedType)
            ? new ReferencePropertyDescriptor(property.Name, referencedType)
            : null;

    public static IReadOnlyList<ReferencePropertyDescriptor> ForOwner(Type ownerType)
        => s_byOwnerType.GetValue(ownerType, static type =>
        {
            if (type.IsAbstract
                || !typeof(EngineObject).IsAssignableFrom(type)
                || Activator.CreateInstance(type) is not EngineObject engineObject)
            {
                return [];
            }

            return engineObject.Properties
                .Select(Describe)
                .OfType<ReferencePropertyDescriptor>()
                .ToArray();
        });

    private static bool IsReferenceValueType(Type valueType, [NotNullWhen(true)] out Type? referencedType)
    {
        Type type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (typeof(ProjectItem).IsAssignableFrom(type))
        {
            referencedType = type;
            return true;
        }

        referencedType = null;
        return false;
    }

    private static void Evict(Type[] types)
    {
        foreach (KeyValuePair<Type, ReferencePropertyDescriptor[]> entry in s_byOwnerType.ToArray())
        {
            if (Array.Exists(types, type => TypeUnloadNotifier.ContainsTypeRecursive(entry.Key, type)))
            {
                s_byOwnerType.Remove(entry.Key);
            }
        }
    }
}
