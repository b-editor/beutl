namespace Beutl.Graphics.Rendering;

/// <summary>
/// レンダリング統計情報
/// </summary>
public class RenderingStatistics
{
    public string BackendName { get; init; } = string.Empty;
    public bool IsInitialized { get; init; }
    public long FrameCount { get; init; }
    public int DrawCalls { get; init; }
    public int Triangles { get; init; }
    public float FrameTime { get; init; } // ミリ秒
    public float FPS => FrameTime > 0 ? 1000f / FrameTime : 0f;
}
