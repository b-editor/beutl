namespace Beutl.Graphics.Rendering.Requests;

internal enum RenderOwnershipState : byte
{
    Pending,
    Discharged,
    CacheTransferred,
}
