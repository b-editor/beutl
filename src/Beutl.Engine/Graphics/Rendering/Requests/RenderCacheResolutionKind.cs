namespace Beutl.Graphics.Rendering.Requests;

internal enum RenderCacheResolutionKind : byte
{
    Bypass,
    Hit,
    MissCapture,
    Superseded,
}
