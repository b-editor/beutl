using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Common;

internal sealed record ReferencePropertyDescriptor(string Name, Type ReferencedType);

// A scene reference is supplied through the Expressions form (ReferenceExpression), not a dedicated
// property type, so the toolkit cannot discover reference properties by type at runtime and lists
// them explicitly here. Schema hints and reference validation share this set.
internal static class ReferenceProperties
{
    private static readonly Dictionary<Type, ReferencePropertyDescriptor[]> s_byOwnerType = new()
    {
        [typeof(SceneDrawable)] = [new ReferencePropertyDescriptor(nameof(SceneDrawable.ReferencedScene), typeof(Scene))],
        [typeof(SceneSound)] = [new ReferencePropertyDescriptor(nameof(SceneSound.ReferencedScene), typeof(Scene))],
    };

    public static IReadOnlyList<ReferencePropertyDescriptor> ForOwner(Type ownerType)
        => s_byOwnerType.TryGetValue(ownerType, out ReferencePropertyDescriptor[]? descriptors) ? descriptors : [];

    public static ReferencePropertyDescriptor? Find(Type ownerType, string propertyName)
        => ForOwner(ownerType).FirstOrDefault(descriptor => descriptor.Name == propertyName);
}
