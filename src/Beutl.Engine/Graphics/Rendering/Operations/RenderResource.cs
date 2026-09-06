namespace Beutl.Graphics.Rendering;

/// <summary>
/// Identifies a request-scoped resource without exposing its raw value.
/// </summary>
public abstract class RenderResource
{
    private object? _rawValue;

    internal RenderResource(
        RenderRequestResourceRegistry registry,
        object rawValue,
        RenderResourceOwnershipMode mode)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _rawValue = rawValue ?? throw new ArgumentNullException(nameof(rawValue));
        Mode = mode;
        OwnershipState = mode == RenderResourceOwnershipMode.Owned
            ? RenderResourceOwnershipState.Pending
            : RenderResourceOwnershipState.BorrowedPending;
    }

    internal RenderRequestResourceRegistry Registry { get; }

    internal abstract Type ValueType { get; }

    internal object RawValue
        => _rawValue ?? throw new InvalidOperationException(
            "The render resource no longer retains its raw value.");

    internal RenderResourceOwnershipMode Mode { get; }

    internal RenderResourceOwnershipState OwnershipState { get; set; }

    internal RenderResourceRegistrationState RegistrationState { get; set; }

    /// <summary>Gets or sets the recording that owns this registration while it is pending.</summary>
    /// <remarks>
    /// Ownership follows the registration: a nested recording that seals hands its still-pending
    /// registrations to the recording that absorbed them, so the scope is always the one whose rollback
    /// would discard them.
    /// </remarks>
    internal IRenderResourceRecordingScope? RecordingScope { get; set; }

    internal object Detach(RenderResourceOwnershipState terminalState)
    {
        object value = RawValue;
        _rawValue = null;
        OwnershipState = terminalState;
        RegistrationState = RenderResourceRegistrationState.Released;
        RecordingScope = null;
        return value;
    }
}

/// <summary>
/// Identifies a typed request-scoped resource without publicly exposing its raw value.
/// </summary>
/// <typeparam name="T">The raw resource type.</typeparam>
public sealed class RenderResource<T> : RenderResource
    where T : class
{
    internal RenderResource(
        RenderRequestResourceRegistry registry,
        T rawValue,
        RenderResourceOwnershipMode mode)
        : base(registry, rawValue, mode)
    {
    }

    internal override Type ValueType => typeof(T);
}

internal enum RenderResourceOwnershipMode : byte
{
    Owned,
    Borrowed,
}
