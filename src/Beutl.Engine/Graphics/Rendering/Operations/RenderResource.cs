namespace Beutl.Graphics.Rendering;

/// <summary>
/// Identifies a request-scoped resource without exposing its raw value.
/// </summary>
public abstract class RenderResource
{
    private RenderResourceRegistration? _slot;
    private RenderResourceOwnershipState _terminalState;

    internal RenderResource(RenderRequestResourceRegistry registry, RenderResourceRegistration slot)
    {
        Registry = registry;
        _slot = slot;
    }

    internal RenderRequestResourceRegistry Registry { get; }

    internal abstract Type ValueType { get; }

    internal RenderResourceRegistration Slot => GetActiveSlot();

    internal object SlotIdentity => GetActiveSlot();

    internal RenderResourceOwnershipState OwnershipState => _slot?.State ?? _terminalState;

    internal RenderResourceRegistrationState RegistrationState { get; set; }

    /// <summary>Gets or sets the recording that owns this registration while it is pending.</summary>
    /// <remarks>
    /// Ownership follows the registration: a nested recording that seals hands its still-pending
    /// registrations to the recording that absorbed them, so the scope is always the one whose rollback
    /// would discard them.
    /// </remarks>
    internal IRenderResourceRecordingScope? RecordingScope { get; set; }

    internal void Detach(RenderResourceOwnershipState terminalState)
    {
        _terminalState = terminalState;
        _slot = null;
    }

    private RenderResourceRegistration GetActiveSlot()
        => _slot ?? throw new InvalidOperationException(
            "A released render resource no longer retains its request-scoped slot.");
}

/// <summary>
/// Identifies a typed request-scoped resource without publicly exposing its raw value.
/// </summary>
/// <typeparam name="T">The raw resource type.</typeparam>
public sealed class RenderResource<T> : RenderResource
    where T : class
{
    internal RenderResource(RenderRequestResourceRegistry registry, RenderResourceRegistration slot)
        : base(registry, slot)
    {
    }

    internal override Type ValueType => typeof(T);
}

internal enum RenderResourceOwnershipMode : byte
{
    Owned,
    Borrowed,
}

internal sealed class OwnedResourceTombstone
{
    public static OwnedResourceTombstone Instance { get; } = new();

    private OwnedResourceTombstone()
    {
    }
}

internal sealed class BorrowedResourceTombstone
{
    public static BorrowedResourceTombstone Instance { get; } = new();

    private BorrowedResourceTombstone()
    {
    }
}
