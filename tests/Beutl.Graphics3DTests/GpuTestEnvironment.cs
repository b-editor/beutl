using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Shared initialization helper for tests that require Vulkan/3D rendering.
/// Lets the tests run when SwiftShader/MoltenVK is available, and calls
/// <see cref="Assert.Ignore"/> to skip them when it is not.
/// </summary>
/// <remarks>
/// This pattern is duplicated from <c>VulkanTestEnvironment</c> under tests/Beutl.UnitTests.
/// That helper is referenced from 10+ call sites inside Beutl.UnitTests, so it is duplicated here
/// rather than relocated. Follow-up: consider consolidating both into a shared test helper.
/// </remarks>
internal static class GpuTestEnvironment
{
    private static readonly object s_lock = new();
    private static bool s_initialized;
    private static bool s_isAvailable;
    private static string? s_unavailableReason;

    public static IGraphicsContext SharedContext { get; private set; } = null!;

    /// <summary>Whether a 3D-rendering-capable shared graphics context could be created.</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return s_isAvailable;
        }
    }

    /// <summary>
    /// Call at the start of a test that requires 3D rendering. Throws <see cref="Assert.Ignore"/> to skip
    /// when 3D rendering is unavailable — unless <c>BEUTL_REQUIRE_GPU</c> is set, in which case the skip
    /// becomes a hard <see cref="Assert.Fail"/> so a misprovisioned GPU job cannot pass silently.
    /// </summary>
    public static IGraphicsContext EnsureAvailable()
    {
        EnsureInitialized();

        if (!s_isAvailable)
        {
            string reason = s_unavailableReason ?? "Vulkan/3D rendering is unavailable on this environment.";

            // Mirror the GpuGoldenSuiteCanaryTests gate: the solution-wide canary lives in Beutl.UnitTests and
            // does not cover a project-scoped run of this suite, so enforce the env var here too. Otherwise a
            // broken Vulkan/SwiftShader provisioning would be reported as a successful (skipped) run.
            if (IsGpuRequired())
            {
                Assert.Fail(
                    "BEUTL_REQUIRE_GPU is set but Vulkan/3D rendering is unavailable, so the Graphics3D suite is "
                    + "being silently skipped (provision MoltenVK / lavapipe / SwiftShader on this job): " + reason);
            }

            Assert.Ignore(reason);
        }

        return SharedContext;
    }

    private static bool IsGpuRequired()
    {
        string? require = Environment.GetEnvironmentVariable("BEUTL_REQUIRE_GPU");
        return string.Equals(require, "1", StringComparison.Ordinal)
               || string.Equals(require, "true", StringComparison.OrdinalIgnoreCase);
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
                        + "Vulkan/MoltenVK could not be initialized on this environment.";
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
