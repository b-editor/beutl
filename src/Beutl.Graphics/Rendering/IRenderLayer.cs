namespace Beutl.Rendering;

// ProjectSystem側のLayerをRendering専用に抽象化
public interface IRenderLayer
{
    RenderLayerSpan? this[TimeSpan timeSpan] { get; }

    IRenderer? Renderer { get; }

    void AddSpan(RenderLayerSpan node);

    void RemoveSpan(RenderLayerSpan node);

    bool ContainsSpan(RenderLayerSpan node);

    void RenderGraphics();

    void RenderAudio();

    void AttachToRenderer(IRenderer renderer);

    void DetachFromRenderer();
}
