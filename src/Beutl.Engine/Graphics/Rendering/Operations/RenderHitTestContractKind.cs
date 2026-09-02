namespace Beutl.Graphics.Rendering;

internal enum RenderHitTestContractKind : byte
{
    Uninitialized,
    None,
    OutputBounds,
    AnyInput,
    Custom,
}
