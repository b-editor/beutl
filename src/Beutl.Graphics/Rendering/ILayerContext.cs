namespace Beutl.Rendering;

// ProjectSystem側のLayerをRendering専用に抽象化
public interface ILayerContext
{
    LayerNode? this[TimeSpan timeSpan] { get; }

    void AddNode(LayerNode node);

    void RemoveNode(LayerNode node);

    bool ContainsNode(LayerNode node);

    void RenderGraphics(IRenderer renderer, TimeSpan timeSpan);

    void RenderAudio(IRenderer renderer, TimeSpan timeSpan);
}
