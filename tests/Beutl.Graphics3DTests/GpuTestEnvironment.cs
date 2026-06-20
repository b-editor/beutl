using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Vulkan/3D を必要とするテスト用の共有初期化ヘルパー。
/// SwiftShader/MoltenVK が利用可能ならテストを通し、利用不可なら <see cref="Assert.Ignore"/> を呼んでスキップする。
/// </summary>
/// <remarks>
/// このパターンは tests/Beutl.UnitTests の <c>VulkanTestEnvironment</c> から複製したもの。
/// 当該ヘルパーは Beutl.UnitTests 内部の 10+ 箇所から参照されているため、ここでは移設せず複製している。
/// follow-up: 共有テストヘルパーへの統合 (重複解消) を検討する。
/// </remarks>
internal static class GpuTestEnvironment
{
    private static readonly object s_lock = new();
    private static bool s_initialized;
    private static bool s_isAvailable;
    private static string? s_unavailableReason;

    public static IGraphicsContext SharedContext { get; private set; } = null!;

    /// <summary>3D レンダリング可能な共有グラフィクスコンテキストを作成できたか。</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return s_isAvailable;
        }
    }

    /// <summary>
    /// 3D レンダリングを必要とするテストの先頭で呼び出す。利用できなければ <see cref="Assert.Ignore"/> を投げてスキップする。
    /// </summary>
    public static IGraphicsContext EnsureAvailable()
    {
        EnsureInitialized();

        if (!s_isAvailable)
        {
            Assert.Ignore(s_unavailableReason ?? "Vulkan/3D rendering is unavailable on this environment.");
        }

        return SharedContext;
    }

    private static void EnsureInitialized()
    {
        if (s_initialized) return;

        lock (s_lock)
        {
            if (s_initialized) return;

            try
            {
                // Use the factory-owned shared singleton (GetOrCreateShared) rather than a test-local
                // CreateContext: the factory owns the lifetime, so there is no GPU device handle to leak
                // on the unavailable path (the common path on headless CI / SwiftShader).
                IGraphicsContext? context = RenderThread.Dispatcher.Invoke(GraphicsContextFactory.GetOrCreateShared);
                if (context == null)
                {
                    s_isAvailable = false;
                    s_unavailableReason = "GraphicsContextFactory.GetOrCreateShared returned null. "
                        + "Vulkan/MoltenVK が初期化できない環境です。";
                }
                else if (!context.Supports3DRendering)
                {
                    s_isAvailable = false;
                    s_unavailableReason =
                        $"Graphics backend '{context.Backend}' does not support 3D rendering on this environment.";
                }
                else
                {
                    SharedContext = context;
                    s_isAvailable = true;
                }
            }
            catch (Exception ex)
            {
                s_isAvailable = false;
                s_unavailableReason = $"Vulkan/3D initialization threw: {ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                s_initialized = true;
            }
        }
    }

    public static T InvokeOnRenderThread<T>(Func<T> func)
        => RenderThread.Dispatcher.Invoke(func);

    public static void InvokeOnRenderThread(Action action)
        => RenderThread.Dispatcher.Invoke(action);
}
