using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>Opens the editor for the transition at one edge of an element.</summary>
public interface ITransitionEditorService
{
    /// <summary>
    /// Shows the transition at <paramref name="edge"/> of <paramref name="element"/> in the transition tool,
    /// opening the tool when it is not open.
    /// </summary>
    void Edit(Element element, ElementEdge edge);
}
