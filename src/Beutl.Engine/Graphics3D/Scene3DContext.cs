namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dシーンコンテキスト
/// </summary>
public class Scene3DContext
{
    public I3DRenderer? Renderer { get; set; }
    public bool ShowDebugInfo { get; set; } = false;
}
