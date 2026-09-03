namespace Beutl.Graphics.Rendering.Requests;

internal enum RenderTargetPoolRegistrationStage : byte
{
    OwnedSlot,
    KnownTarget,
    KnownSurface,
}
