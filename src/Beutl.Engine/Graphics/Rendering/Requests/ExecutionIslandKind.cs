namespace Beutl.Graphics.Rendering.Requests;

internal enum ExecutionIslandKind : byte
{
    ShaderRun,
    Compatibility,
    Target,
    Readback,
}
