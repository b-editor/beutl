using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.Editor.Components.Helpers;

internal static class CoreObjectHelper
{
    // "ElementName - TypeName", or only the type name when the object has no owner element.
    public static string GetDisplayName(CoreObject obj)
    {
        var element = GetOwnerElement(obj);
        var typeName = TypeDisplayHelpers.GetLocalizedName(obj.GetType());

        return element != null ? $"{element.Name.Value} - {typeName}" : typeName;
    }

    public static Element? GetOwnerElement(CoreObject obj)
    {
        return (obj as IHierarchical)?.FindHierarchicalParent<Element>();
    }
}
