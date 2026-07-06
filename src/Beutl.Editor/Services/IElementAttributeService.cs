using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Single-element attribute writes (boolean flags, accent color, etc.).
/// Distinct from <see cref="IElementStructureService"/>: one property on one
/// <see cref="Element"/>, no file IO or scene-graph traversal. New attributes
/// belong here, not on the structure service.
/// </summary>
public interface IElementAttributeService
{
    void SetEnabled(Element element, bool isEnabled);

    void SetAccentColor(Element element, Color color);

    void SetLocked(Element element, bool isLocked);
}
