namespace Beutl.Graphics.Rendering;

internal enum RenderOwnershipState : byte
{
    Pending,
    Discharged,
    CacheTransferred,
}
