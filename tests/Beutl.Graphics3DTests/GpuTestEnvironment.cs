using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Helpers for tests that require Vulkan/3D rendering. <see cref="TestGpu"/> decides whether a device is
/// available.
/// </summary>
/// <remarks>
/// The render-thread helpers mirror <c>VulkanTestEnvironment</c> under tests/Beutl.UnitTests, which is referenced
/// from too many call sites there to move.
/// </remarks>
internal static class GpuTestEnvironment
{
    /// <summary>
    /// Call at the start of a test that requires 3D rendering. Skips it when no Vulkan device, SwiftShader
    /// included, is available (<see cref="TestGpu.RequireAnyDevice"/>).
    /// </summary>
    public static IGraphicsContext EnsureAvailable() => TestGpu.RequireAnyDevice();

    /// <summary>Like <see cref="EnsureAvailable"/>, returning the Vulkan context behind the shared one.</summary>
    public static VulkanContext EnsureVulkanAvailable() => ResolveVulkan(EnsureAvailable());

    // On macOS the context is a CompositeContext pairing a Metal context with the Vulkan one that owns the
    // device; everywhere else it is the Vulkan context itself.
    public static VulkanContext ResolveVulkan(IGraphicsContext context)
        => context switch
        {
            VulkanContext vulkan => vulkan,
            CompositeContext composite => composite.Vulkan,
            _ => throw new InvalidOperationException(
                $"'{context.GetType().Name}' is not backed by a Vulkan context."),
        };

    public static T InvokeOnRenderThread<T>(Func<T> func)
    {
        int before = VulkanValidationErrorLog.Shared.Count;
        T result = RenderThread.Dispatcher.Invoke(func);
        FailOnValidationErrorsSince(before);
        return result;
    }

    public static void InvokeOnRenderThread(Action action)
    {
        int before = VulkanValidationErrorLog.Shared.Count;
        RenderThread.Dispatcher.Invoke(action);
        FailOnValidationErrorsSince(before);
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
