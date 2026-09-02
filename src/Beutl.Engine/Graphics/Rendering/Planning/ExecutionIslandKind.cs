namespace Beutl.Graphics.Rendering;

internal enum ExecutionIslandKind : byte
{
    ShaderRun,
    Compatibility,
    Target,
    Readback,
}
