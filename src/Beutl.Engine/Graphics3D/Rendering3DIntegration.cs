using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3DレンダリングをBeutlのエンジンに統合するためのヘルパー
/// </summary>
public static class Rendering3DIntegration
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(Rendering3DIntegration));

    /// <summary>
    /// BeutlエンジンのICanvasを3D対応に拡張
    /// </summary>
    public static I3DCanvas? Create3DCanvas(ICanvas canvas, int width, int height)
    {
        try
        {
            var manager = Rendering3DManager.Instance;
            if (!manager.IsInitialized)
            {
                s_logger.LogWarning("3D rendering system not initialized. Call Rendering3DManager.Initialize() first.");
                return null;
            }

            // 2Dキャンバスをラップした3Dキャンバスを作成
            return new Canvas3DWrapper(canvas, manager, width, height);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to create 3D canvas");
            return null;
        }
    }

    /// <summary>
    /// 既存のRenderNodeシステムに3Dサポートを追加
    /// </summary>
    public static RenderNode Create3DRenderNode(I3DScene scene, I3DCamera camera)
    {
        return new Render3DNode(scene, camera);
    }
}
