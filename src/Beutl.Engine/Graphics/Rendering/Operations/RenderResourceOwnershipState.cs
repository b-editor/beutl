namespace Beutl.Graphics.Rendering;

internal enum RenderResourceOwnershipState : byte
{
    Pending,
    RequestOwned,
    BorrowedPending,
    RequestBorrowed,
    LeasedToCallback,
    Discharged,
    ReleasedToken,
}
