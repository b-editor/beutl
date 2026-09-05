namespace Beutl.Graphics.Rendering;

/// <summary>
/// Binds a declared resource slot to a request-scoped resource token.
/// </summary>
/// <remarks>
/// Bindings can only be created by <see cref="RenderResourceSlot{T}.Bind(RenderResource{T})"/>, which
/// prevents pairing a slot with a fabricated or differently typed token.
/// </remarks>
public readonly struct RenderResourceBinding
{
    private readonly object? _slotIdentity;
    private readonly RenderResource? _resource;

    internal RenderResourceBinding(object slotIdentity, RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(slotIdentity);
        ArgumentNullException.ThrowIfNull(resource);

        _slotIdentity = slotIdentity;
        _resource = resource;
    }

    internal object SlotIdentity
        => _slotIdentity
           ?? throw new InvalidOperationException("The render resource binding is uninitialized.");

    internal RenderResource Resource
        => _resource
           ?? throw new InvalidOperationException("The render resource binding is uninitialized.");

    internal bool IsInitialized => _slotIdentity is not null && _resource is not null;

    internal static RenderResourceBinding CreateEngineBinding(RenderResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.Registry.ValidateBinding(resource);
        return new RenderResourceBinding(resource, resource);
    }
}
