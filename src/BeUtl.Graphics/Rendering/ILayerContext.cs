namespace BeUtl.Rendering;

// ProjectSystem側のLayerをRendering専用に抽象化
public interface ILayerContext
{
    LayerNode? this[TimeSpan timeSpan] { get; }

    void AddNode(LayerNode node);

    void RemoveNode(LayerNode node);

    bool ContainsNode(LayerNode node);
}
