using System.Reflection;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Helpers for unit tests that need Vulkan: the Vulkan context, render-thread invocation and the validation gate.
/// <see cref="TestGpu"/> decides whether a device is available.
/// </summary>
internal static class VulkanTestEnvironment
{
    private static int s_deliberateValidationErrors;

    /// <summary>
    /// Call at the start of a test that needs Vulkan. Skips it when no Vulkan device, SwiftShader included, is
    /// available (<see cref="TestGpu.RequireAnyDevice"/>).
    /// </summary>
    public static IGraphicsContext EnsureAvailable() => TestGpu.RequireAnyDevice();

    /// <summary>Like <see cref="EnsureAvailable"/>, but returns the Vulkan context behind the shared one.</summary>
    public static VulkanContext EnsureVulkanContext() => ResolveVulkan(EnsureAvailable());

    /// <summary>Returns <paramref name="context"/> as a Vulkan context, or the Vulkan side of a composite one.</summary>
    public static VulkanContext ResolveVulkan(IGraphicsContext context) => context switch
    {
        VulkanContext vulkan => vulkan,
        CompositeContext composite => composite.Vulkan,
        _ => throw new InvalidOperationException("The graphics context has no Vulkan backend."),
    };

    /// <summary>Reads the private backend texture Skia draws <paramref name="texture"/> through.</summary>
    public static GRBackendTexture GetSkiaBackendTexture(VulkanTexture2D texture)
        => (GRBackendTexture)typeof(VulkanTexture2D)
            .GetField("_skiaBackendTexture", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(texture)!;

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
    /// The suite calls this once at the end, so a validation error recorded outside
    /// <see cref="InvokeOnRenderThread(Action)"/> still fails the run. The failure belongs to no test, so read the
    /// <c>dotnet test</c> exit code.
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
