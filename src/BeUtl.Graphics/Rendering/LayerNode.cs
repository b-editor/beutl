namespace BeUtl.Rendering;

public sealed class LayerNode
{
    public TimeSpan Start { get; set; }

    public TimeSpan Duration { get; set; }

    public Renderable? Value { get; set; }
}
