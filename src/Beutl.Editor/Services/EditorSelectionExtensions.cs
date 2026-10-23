using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public static class EditorSelectionExtensions
{
    // What Delete acts on, from the timeline or the menu: the selected elements and the rest of their groups.
    public static Element[] GetTargetElements(this IEditorSelection selection, Scene scene)
    {
        HashSet<Guid> ids = DuplicateHelper.ExpandWithGroupSiblings(
            selection.SelectedElements.Select(element => element.Id.Value), scene.Groups);
        return [.. scene.Children.Where(element => ids.Contains(element.Id.Value))];
    }
}
