using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal sealed record OpacityMaskRenderFragmentPayload(
    RenderResource<Brush.Resource> Mask,
    Rect BrushBounds,
    bool Invert);
