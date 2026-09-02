namespace Beutl.Graphics.Rendering;

internal enum RenderTargetPoolRegistrationStage : byte
{
    OwnedSlot,
    KnownTarget,
    KnownSurface,
}
