using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Vulkan を必要とする単体テスト用の共有初期化ヘルパー。
/// SwiftShader/MoltenVK が利用可能ならテストを通し、利用不可なら <see cref="Assert.Ignore"/> を呼んでスキップする。
/// </summary>
internal static class VulkanTestEnvironment
{
    private static readonly object s_lock = new();
    private static bool s_initialized;
    private static bool s_isAvailable;
    private static string? s_unavailableReason;

    public static IGraphicsContext SharedContext { get; private set; } = null!;

    /// <summary>
    /// Vulkan 共有コンテキストを作成できたか。<see cref="Assert.Ignore"/> による「成功」に見える
    /// GPU ゴールデン群全体のサイレントスキップを非ゲートの canary で検出するために公開する。
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return s_isAvailable;
        }
    }

    /// <summary><see cref="IsAvailable"/> が false の理由（利用可能なら null）。</summary>
    public static string? UnavailableReason
    {
        get
        {
            EnsureInitialized();
            return s_unavailableReason;
        }
    }

    /// <summary>
    /// Vulkan を必要とするテストの先頭で呼び出す。利用できなければ <see cref="Assert.Ignore"/> を投げてスキップする。
    /// </summary>
    public static IGraphicsContext EnsureAvailable()
    {
        EnsureInitialized();

        if (!s_isAvailable)
        {
            Assert.Ignore(s_unavailableReason ?? "Vulkan is unavailable on this environment.");
        }

        return SharedContext;
    }

    /// <summary>
    /// 共有 Vulkan コンテキストを初期化済みにする。スレッド競合を避けるため最初の呼び出しのみが実初期化を行う。
    /// </summary>
    public static void EnsureInitialized()
    {
        if (s_initialized) return;

        lock (s_lock)
        {
            if (s_initialized) return;

            try
            {
                SharedContext = RenderThread.Dispatcher.Invoke(GraphicsContextFactory.GetOrCreateShared)!;
                if (SharedContext == null)
                {
                    s_isAvailable = false;
                    s_unavailableReason = "GraphicsContextFactory.GetOrCreateShared returned null. "
                        + "Vulkan/MoltenVK が初期化できない環境です。";
                }
                else
                {
                    s_isAvailable = true;
                }
            }
            catch (Exception ex)
            {
                s_isAvailable = false;
                s_unavailableReason = $"Vulkan initialization threw: {ex.GetType().Name}: {ex.Message}";
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

    /// <summary>
    /// True when BEUTL_REQUIRE_GPU is set: a GPU-capable CI job that wants a silent GPU/compute skip turned into a
    /// hard failure. Shared by <see cref="GpuGoldenSuiteCanaryTests"/> and <see cref="RequireComputeCapable"/>.
    /// </summary>
    public static bool IsGpuRequired
    {
        get
        {
            string? require = Environment.GetEnvironmentVariable("BEUTL_REQUIRE_GPU");
            return string.Equals(require, "1", StringComparison.Ordinal)
                   || string.Equals(require, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Compute-capability gate for a Vulkan-compute test: returns when <paramref name="context"/> supports 3D
    /// rendering, otherwise <see cref="Assert.Fail(string)"/>'s under BEUTL_REQUIRE_GPU (so a compute-incapable CI
    /// job no longer silently drops the runtime compute counter/gate surface) and <see cref="Assert.Ignore(string)"/>'s
    /// otherwise. Mirrors <see cref="GpuGoldenSuiteCanaryTests"/> for device PRESENCE, but for compute CAPABILITY.
    /// </summary>
    public static void RequireComputeCapable(IGraphicsContext context, string what)
    {
        if (context.Supports3DRendering)
            return;

        string reason = $"{what} requires a Vulkan compute-capable context (Supports3DRendering == false).";
        if (IsGpuRequired)
        {
            Assert.Fail(
                "BEUTL_REQUIRE_GPU is set but " + reason + " The runtime compute counter/gate surface "
                + "(FlushSyncs, K-dispatch GpuPasses, compute alloc/scratch failure) is being silently skipped.");
        }

        Assert.Ignore(reason + " Set BEUTL_REQUIRE_GPU=1 on a compute-capable CI job to enforce it.");
    }
}
