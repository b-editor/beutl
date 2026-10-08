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

    /// <summary>
    /// Renames <paramref name="element"/>. The host commits the rename as one history entry; this default,
    /// kept for replacements written before the member existed, only writes the name.
    /// </summary>
    void SetName(Element element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Name = name;
    }
}
