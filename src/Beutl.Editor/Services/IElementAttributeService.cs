using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Single-element attribute writes (boolean flags, accent color, etc.).
/// Distinct from <see cref="IElementStructureService"/>: these operations
/// only mutate a single property on a single <see cref="Element"/> and
/// touch nothing else — no file IO, no scene-graph traversal. A plugin
/// that adds a new attribute belongs here, not on the structure service.
/// </summary>
public interface IElementAttributeService
{
    void SetEnabled(Element element, bool isEnabled);

    void SetAccentColor(Element element, Color color);
}
