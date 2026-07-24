namespace Beutl.Graphics.Rendering;

public enum RenderIntent
{
    Preview,
    Delivery,
}

public enum RenderRequestPurpose
{
    Frame,
    HitTest,
    Bounds,
    CacheWarmup,
    Auxiliary,
}
