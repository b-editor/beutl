using System.Runtime.CompilerServices;
using Beutl.Engine;

namespace Beutl.AgentToolkit.Common;

internal sealed record ReferencePropertyDescriptor(string Name, Type ReferencedType);

// The properties a type declares as references (Property.CreateReference<T>()), which point at an existing object.
internal static class ReferenceProperties
{
    // Weak-keyed so caching a plugin-defined owner type never roots its collectible AssemblyLoadContext.
    private static readonly ConditionalWeakTable<Type, ReferencePropertyDescriptor[]> s_byOwnerType = new();

    static ReferenceProperties()
    {
        TypeUnloadNotifier.TypesUnloading += Evict;
    }

    public static ReferencePropertyDescriptor? Describe(IProperty property)
        => property is IExpressionProperty { IsReference: true }
            ? new ReferencePropertyDescriptor(property.Name, Nullable.GetUnderlyingType(property.ValueType) ?? property.ValueType)
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
