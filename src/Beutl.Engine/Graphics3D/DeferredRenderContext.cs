namespace Beutl.Graphics.Rendering;

/// <summary>
/// 遅延レンダリングコンテキスト
/// </summary>
public class DeferredRenderContext
{
    public required I3DScene Scene { get; init; }
    public required I3DCamera Camera { get; init; }
    public PostProcessSettings PostProcessSettings { get; init; } = new();
}
