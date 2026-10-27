using Beutl.Graphics;

namespace Beutl.Engine;

/// <summary>Editable points in the owning drawable's local logical coordinates.</summary>
/// <remarks>The host applies the drawable transform and commits edits to its history on release.</remarks>
public interface IEditingHandleProvider
{
    IReadOnlyList<EditingHandle> GetEditingHandles(MediaTime time);
    void MoveEditingHandle(string id, Point position, MediaTime time);
}

public sealed record EditingHandle(string Id, Point Position, string Label, uint Color = 0xff1e90ff);
