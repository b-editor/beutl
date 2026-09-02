namespace Beutl.Graphics.Rendering;

internal sealed class RenderResourceRegistration
{
    private object? _rawValue;

    public RenderResourceRegistration(
        object rawValue,
        RenderResourceOwnershipMode mode)
    {
        _rawValue = rawValue;
        Mode = mode;
        State = mode == RenderResourceOwnershipMode.Owned
            ? RenderResourceOwnershipState.Pending
            : RenderResourceOwnershipState.BorrowedPending;
    }

    public object RawValue
        => _rawValue ?? throw new InvalidOperationException(
            "The render resource slot no longer retains its raw value.");

    public object TakeRawValue()
    {
        object value = RawValue;
        _rawValue = null;
        return value;
    }

    public RenderResourceOwnershipMode Mode { get; }

    public List<RenderResource> Tokens { get; } = [];

    public int PendingRegistrations { get; set; }

    public int CommittedRegistrations { get; set; }

    public RenderResourceOwnershipState State { get; set; }

    public void UpdateStableState()
    {
        if (State is RenderResourceOwnershipState.Discharged
            or RenderResourceOwnershipState.ReleasedToken
            or RenderResourceOwnershipState.LeasedToCallback)
        {
            return;
        }

        State = Mode switch
        {
            RenderResourceOwnershipMode.Owned when CommittedRegistrations > 0
                => RenderResourceOwnershipState.RequestOwned,
            RenderResourceOwnershipMode.Owned
                => RenderResourceOwnershipState.Pending,
            RenderResourceOwnershipMode.Borrowed when CommittedRegistrations > 0
                => RenderResourceOwnershipState.RequestBorrowed,
            _ => RenderResourceOwnershipState.BorrowedPending,
        };
    }
}
