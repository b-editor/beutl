using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Vulkan を必要とする単体テスト用の共有初期化ヘルパー。
/// SwiftShader/MoltenVK が利用可能ならテストを通し、利用不可なら <see cref="Assert.Ignore"/> を呼んでスキップする。
/// </summary>
internal static class VulkanTestEnvironment
{
    private static int s_deliberateValidationErrors;

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
    {
        int before = VulkanValidationErrorLog.Shared.Count;
        T result = RenderThread.Dispatcher.CheckAccess()
            ? func()
            : RenderThread.Dispatcher.InvokeAsync(func).GetAwaiter().GetResult();
        FailOnValidationErrorsSince(before);
        return result;
    }

    public static void InvokeOnRenderThread(Action action)
    {
        int before = VulkanValidationErrorLog.Shared.Count;
        if (RenderThread.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            RenderThread.Dispatcher.InvokeAsync(action).GetAwaiter().GetResult();
        }

        FailOnValidationErrorsSince(before);
    }

    /// <summary>Records a validation error the suite raised on purpose, so the suite gate discounts it.</summary>
    /// <remarks>
    /// Only the gate's own probe needs this. Anything else writing to the shared log is the unattributed
    /// error <see cref="AssertNoUnattributedValidationErrors"/> exists to report.
    /// </remarks>
    public static void RecordDeliberateValidationError(string message)
    {
        Interlocked.Increment(ref s_deliberateValidationErrors);
        VulkanValidationErrorLog.Shared.Record(message);
    }

    /// <summary>Fails when the shared log holds an error no invocation was wrapped to read.</summary>
    /// <remarks>
    /// <see cref="InvokeOnRenderThread(Action)"/> reads the log by taking a count before the call it
    /// dispatches, so an error raised by GPU work invoked straight through <c>RenderThread.Dispatcher</c>
    /// falls before the next snapshot and is never attributed to anything. The suite reads the log once at
    /// the end so that error still fails the run rather than leaving the validation job green.
    /// <para>
    /// A failure here belongs to no test, so <c>dotnet test</c> still prints <c>Passed!</c> and reports it
    /// only as "TearDown failed for test fixture" plus a non-zero exit code. Read the exit code.
    /// </para>
    /// </remarks>
    public static void AssertNoUnattributedValidationErrors()
    {
        int unattributed = VulkanValidationErrorLog.Shared.Count
                           - Volatile.Read(ref s_deliberateValidationErrors);
        if (unattributed <= 0)
            return;

        Assert.Fail(
            "Vulkan validation errors were reported by work no test was wrapped to observe. Route the "
            + "GPU-backed invocation through VulkanTestEnvironment.InvokeOnRenderThread so the failure "
            + "lands on the test that caused it. "
            + VulkanValidationErrorLog.Format(unattributed, VulkanValidationErrorLog.Shared.Messages));
    }

    /// <summary>Fails when the preceding render work reported a Vulkan validation error.</summary>
    /// <remarks>
    /// Submission-time errors may be attributed to a later invocation. Ordinary runs record nothing.
    /// </remarks>
    private static void FailOnValidationErrorsSince(int previousCount)
    {
        string report = VulkanValidationErrorLog.Shared.DescribeSince(previousCount);
        if (report.Length != 0)
            Assert.Fail(report);
    }
}
