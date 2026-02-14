using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl;

/// <summary>
/// Provides helper methods for working with CoreObject instances.
/// </summary>
internal static class CoreObjectHelper
{
    /// <summary>
    /// Gets the display name for a CoreObject, combining the parent element name and type name.
    /// </summary>
    /// <param name="obj">The CoreObject to get the display name for.</param>
    /// <returns>A string representing the display name in the format "ElementName - TypeName" or just "TypeName" if no parent element exists.</returns>
    public static string GetDisplayName(CoreObject obj)
    {
        var element = (obj as IHierarchical)?.FindHierarchicalParent<Element>();
        var typeName = TypeDisplayHelpers.GetLocalizedName(obj.GetType());

        return element != null ? $"{element.Name} - {typeName}" : typeName;
    }

    /// <summary>
    /// Gets the owner element for a CoreObject by traversing the hierarchy.
    /// </summary>
    /// <param name="obj">The CoreObject to get the owner element for.</param>
    /// <returns>The parent Element if found, otherwise null.</returns>
    public static Element? GetOwnerElement(CoreObject obj)
    {
        return (obj as IHierarchical)?.FindHierarchicalParent<Element>();
    }
}
