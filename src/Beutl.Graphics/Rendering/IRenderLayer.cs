namespace Beutl.Rendering;

// ProjectSystem側のLayerをRendering専用に抽象化
public interface IRenderLayer
{
    RenderLayerSpan? this[TimeSpan timeSpan] { get; }

    IRenderer? Renderer { get; }

    void AddNode(RenderLayerSpan node);

    void RemoveNode(RenderLayerSpan node);

    bool ContainsNode(RenderLayerSpan node);

    void RenderGraphics();

    void RenderAudio();

    void AttachToRenderer(IRenderer renderer);

    void DetachFromRenderer();
}
