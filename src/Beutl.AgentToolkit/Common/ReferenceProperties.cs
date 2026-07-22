using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Beutl.Engine;

namespace Beutl.AgentToolkit.Common;

internal sealed record ReferencePropertyDescriptor(string Name, Type ReferencedType);

// A ProjectItem is project-owned, so a property typed as one is a reference, not an inlined child.
internal static class ReferenceProperties
{
    private static readonly ConcurrentDictionary<Type, ReferencePropertyDescriptor[]> s_byOwnerType = new();

    public static ReferencePropertyDescriptor? Describe(IProperty property)
        => IsReferenceValueType(property.ValueType, out Type? referencedType)
            ? new ReferencePropertyDescriptor(property.Name, referencedType)
            : null;

    public static IReadOnlyList<ReferencePropertyDescriptor> ForOwner(Type ownerType)
        => s_byOwnerType.GetOrAdd(ownerType, static type =>
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
}
