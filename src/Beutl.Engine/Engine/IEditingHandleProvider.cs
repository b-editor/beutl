using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Engine;

/// <summary>Editable points in the owning drawable's local logical coordinates.</summary>
/// <remarks>The host applies the drawable transform and commits edits to its history on release.</remarks>
public interface IEditingHandleProvider
{
    IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time);

    /// <summary>Moves a handle without a bound <see cref="EditingHandle.Property"/>.</summary>
    /// <remarks>A handle with a bound property is written by the host instead, including its keyframes.</remarks>
    void MoveEditingHandle(string id, Point position, MediaTime time)
    {
    }
}

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
