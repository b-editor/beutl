using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.HeadlessUITests;

// Mirrors tests/Beutl.UnitTests/.../VulkanTestEnvironment (internal, so not referenceable here).
// Probes the shared GPU context once on the render thread; tests that need a real GPU call
// EnsureAvailable() and self-skip with a logged reason when Vulkan/SwiftShader/MoltenVK is absent.
internal static class GpuTestGate
{
    private static readonly object s_lock = new();
    private static volatile bool s_initialized;
    private static bool s_isAvailable;
    private static string? s_reason;

    public static bool IsAvailable
    {
        get
        {
            EnsureInitialized();
            return s_isAvailable;
        }
    }

    public static string? UnavailableReason
    {
        get
        {
            EnsureInitialized();
            return s_reason;
        }
    }

    public static void EnsureAvailable()
    {
        EnsureInitialized();
        if (!s_isAvailable)
        {
            string reason = s_reason ?? "No GPU graphics context is available in this environment.";

            // CI sets BEUTL_REQUIRE_GPU on GPU-capable runs; there, a missing context means broken
            // provisioning, which must fail loudly instead of passing as a skip (mirrors GpuTestEnvironment).
            if (IsGpuRequired())
            {
                Assert.Fail(
                    "BEUTL_REQUIRE_GPU is set but no GPU graphics context is available, so this GPU-backed test "
                    + "is being silently skipped (provision MoltenVK / lavapipe / SwiftShader on this job): " + reason);
            }

            TestContext.WriteLine($"[GPU SKIP] {reason}");
            Assert.Ignore(reason);
        }
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
                IGraphicsContext? context = RenderThread.Dispatcher.Invoke(GraphicsContextFactory.GetOrCreateShared);
                if (context == null)
                {
                    s_isAvailable = false;
                    s_reason = "GraphicsContextFactory.GetOrCreateShared returned null; "
                        + "no Vulkan/MoltenVK/SwiftShader device.";
                }
                else
                {
                    s_isAvailable = true;
                }
            }
            catch (Exception ex)
            {
                s_isAvailable = false;
                s_reason = $"GPU context initialization threw: {ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                s_initialized = true;
            }
        }
    }
}
