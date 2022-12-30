namespace Beutl.Rendering;

// ProjectSystem側のLayerをRendering専用に抽象化
public interface IRenderLayer
{
    RenderLayerSpan? this[TimeSpan timeSpan] { get; }

    void AddNode(RenderLayerSpan node);

    void RemoveNode(RenderLayerSpan node);

    bool ContainsNode(RenderLayerSpan node);

    void RenderGraphics(IRenderer renderer, TimeSpan timeSpan);

    void RenderAudio(IRenderer renderer, TimeSpan timeSpan);
}
