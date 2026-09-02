namespace Beutl.Graphics.Rendering;

/// <summary>
/// Represents a declaration-owned resource address.
/// </summary>
/// <remarks>
/// This non-generic base exists only so a description can declare a heterogeneous set of typed slots.
/// It does not expose a raw resource type or value to callbacks.
/// </remarks>
public abstract class RenderResourceSlot
{
    internal RenderResourceSlot()
    {
    }

    internal abstract Type ValueType { get; }

    internal abstract bool Accepts(RenderResource resource);
}
