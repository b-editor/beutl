namespace Beutl.Graphics.Rendering;

/// <summary>
/// Binds a declared resource slot to a request-scoped resource token.
/// </summary>
/// <remarks>
/// Bindings can only be created by <see cref="RenderResourceSlot{T}.Bind(RenderResource{T})"/>, which
/// prevents pairing a slot with a fabricated or differently typed token.
/// </remarks>
public sealed class RenderResourceBinding
{
    internal RenderResourceBinding(RenderResourceSlot slot, RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(resource);
        if (!slot.Accepts(resource))
        {
            throw new ArgumentException(
                "A render resource binding must use a token whose type matches its slot.",
                nameof(resource));
        }

        Slot = slot;
        Resource = resource;
    }

    internal RenderResourceSlot Slot { get; }

    internal RenderResource Resource { get; }

    internal static RenderResourceBinding CreateEngineBinding(RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.Registry.ValidateBinding(resource);
        return new RenderResourceBinding(new EngineRenderResourceSlot(resource.ValueType), resource);
    }
}
