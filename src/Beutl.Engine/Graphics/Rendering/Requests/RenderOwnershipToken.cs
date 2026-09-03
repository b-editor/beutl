namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderOwnershipToken
{
    public RenderOwnershipToken(RenderRequestOwner owner, Action cleanup)
    {
        Owner = owner;
        Cleanup = cleanup;
    }

    public RenderRequestOwner Owner { get; }

    public Action Cleanup { get; }

    public RenderOwnershipState State { get; set; }
}
