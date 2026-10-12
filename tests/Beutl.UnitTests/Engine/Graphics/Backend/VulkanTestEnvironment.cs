using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Helpers for unit tests that need Vulkan: render-thread invocation and the validation gate.
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
