namespace Beutl.Graphics.Rendering;

internal enum RenderCacheResolutionKind : byte
{
    Bypass,
    Hit,
    MissCapture,
    Superseded,
}
