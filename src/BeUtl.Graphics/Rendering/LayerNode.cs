namespace BeUtl.Rendering;

public sealed class LayerNode
{
    public LayerNode? Next { get; internal set; }

    public LayerNode? Previous { get; internal set; }

    public ILayerContext? Parent { get; internal set; }

    public TimeSpan Offset { get; set; }

    public TimeSpan Duration { get; set; }

    public IRenderable? Value { get; set; }
}
