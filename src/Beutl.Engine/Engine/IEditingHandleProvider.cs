using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Engine;

/// <summary>Editable points in the owning drawable's local logical coordinates.</summary>
/// <remarks>The host applies the drawable transform and commits edits to its history on release.</remarks>
public interface IEditingHandleProvider
{
    IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time);

    /// <summary>Gets handles, with the host's local drawable bounds as fallback when the provider has none.</summary>
    IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time, Rect fallbackBounds)
        => GetEditingHandles(time);

    /// <summary>Non-interactive lines drawn beneath the handles, in the same local coordinates.</summary>
    IReadOnlyList<EditingGuideLine> GetEditingGuides(MediaTime time) => [];

    /// <summary>Gets guide lines, with the host's local drawable bounds as fallback when the provider has none.</summary>
    IReadOnlyList<EditingGuideLine> GetEditingGuides(MediaTime time, Rect fallbackBounds)
        => GetEditingGuides(time);

    /// <summary>Captures the mapping used to move a handle for the duration of one drag.</summary>
    /// <remarks>The host invokes the returned action inside its edit session.</remarks>
    Action<Point> BeginEditingHandleDrag(string id, MediaTime time)
        => position => MoveEditingHandle(id, position, time);

    /// <summary>Captures the mapping for one drag, with the host's local drawable bounds as fallback.</summary>
    Action<Point> BeginEditingHandleDrag(string id, MediaTime time, Rect fallbackBounds)
        => BeginEditingHandleDrag(id, time);

    /// <summary>Moves a handle without a bound <see cref="EditingHandle.Property"/>.</summary>
    /// <remarks>A handle with a bound property is written by the host instead, including its keyframes.</remarks>
    void MoveEditingHandle(string id, Point position, MediaTime time)
    {
    }
}

/// <summary>A line between two points in the owning drawable's local logical coordinates.</summary>
public readonly record struct EditingGuideLine(Point Start, Point End);

/// <summary>An editable point and how the host writes its edits.</summary>
public sealed record EditingHandle(string Id, Point Position, string Label)
{
    /// <summary>The handle's color, or null for the host's default handle color.</summary>
    public Color? Color { get; init; }

    /// <summary>The property the handle edits, offset by the drag the way transform handles move their values.</summary>
    /// <remarks>
    /// When set, the host writes the property and the keyframes surrounding the current time.
    /// Otherwise it calls <see cref="IEditingHandleProvider.MoveEditingHandle"/>.
    /// </remarks>
    public IProperty<Point>? Property { get; init; }
}
