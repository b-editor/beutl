namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3DレンダリングのためのRenderNode（簡略版）
/// </summary>
internal class Render3DNode : RenderNode
{
    private readonly I3DScene _scene;
    private readonly I3DCamera _camera;

    public Render3DNode(I3DScene scene, I3DCamera camera)
    {
        _scene = scene;
        _camera = camera;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        // 3DシーンをRenderNodeとして処理
        return [];
    }
}
