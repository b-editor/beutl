namespace BeUtl.Rendering;

// ProjectSystem側のLayerをRendering専用に抽象化
public interface ILayerContext
{
    LayerNode? this[TimeSpan timeSpan] { get; }

    LayerNode? First { get; }

    LayerNode? Last { get; }

    int Count { get; }

    TimeSpan Duration { get; }

    void AddAfter(LayerNode node, LayerNode newNode);

    void AddBefore(LayerNode node, LayerNode newNode);

    void AddFirst(LayerNode node);

    void AddLast(LayerNode node);

    void Remove(LayerNode node);

    bool ContainsNode(LayerNode node);
}
