using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

using Semaphore = Silk.NET.Vulkan.Semaphore;

/// <summary>
/// The timeline submissions that order the engine's Vulkan queue against Skia's Metal queue on macOS. The
/// semantics are plain Vulkan, so they are checked on every device that enables timeline semaphores.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed unsafe class VulkanTimelineSubmissionTests
{
    [Test]
    [Category("GpuPassFusionGpu")]
    public void SubmitSignalingTimeline_SignalsOnceTheBatchCompletes()
    {
        VulkanContext context = RequireTimelineContext();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Semaphore timeline = CreateTimeline(context);
            try
            {
                context.SubmitSignalingTimeline(timeline, 5);

                Assert.That(WaitFor(context, timeline, 5, TimeSpan.FromSeconds(10)), Is.EqualTo(Result.Success));
                Assert.That(ReadValue(context, timeline), Is.GreaterThanOrEqualTo(5ul));
            }
            finally
            {
                context.WaitIdle();
                context.Vk.DestroySemaphore(context.Device, timeline, null);
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void WaitForTimelineOnNextSubmission_HoldsTheBatchUntilTheValueIsSignaled()
    {
        VulkanContext context = RequireTimelineContext();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Semaphore timeline = CreateTimeline(context);
            try
            {
                // The batch waits for 3 and then signals 4, so 4 can only appear after something else signals 3.
                context.WaitForTimelineOnNextSubmission(timeline, 3);
                context.SubmitSignalingTimeline(timeline, 4);

                Assert.That(WaitFor(context, timeline, 4, TimeSpan.FromMilliseconds(100)), Is.EqualTo(Result.Timeout));
                Assert.That(ReadValue(context, timeline), Is.Zero);

                Assert.That(Signal(context, timeline, 3), Is.EqualTo(Result.Success));

                Assert.That(WaitFor(context, timeline, 4, TimeSpan.FromSeconds(10)), Is.EqualTo(Result.Success));
            }
            finally
            {
                // A failed assertion above leaves the batch waiting; release it before the queue is drained.
                if (ReadValue(context, timeline) < 3)
                    Signal(context, timeline, 3);

                context.WaitIdle();
                context.Vk.DestroySemaphore(context.Device, timeline, null);
            }
        });
    }

    private static VulkanContext RequireTimelineContext()
    {
        IGraphicsContext shared = VulkanTestEnvironment.EnsureAvailable();
        VulkanContext context = shared as VulkanContext
            ?? (shared as CompositeContext)?.Vulkan
            ?? throw new InvalidOperationException("The shared graphics context has no Vulkan backend.");
        if (!context.SupportsTimelineSemaphores)
            Assert.Ignore("The Vulkan device does not enable timeline semaphores.");

        return context;
    }

    private static Semaphore CreateTimeline(VulkanContext context)
    {
        var typeInfo = new SemaphoreTypeCreateInfo
        {
            SType = StructureType.SemaphoreTypeCreateInfo,
            SemaphoreType = SemaphoreType.Timeline,
            InitialValue = 0,
        };
        var createInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = &typeInfo,
        };

        Semaphore timeline;
        Result result = context.Vk.CreateSemaphore(context.Device, &createInfo, null, &timeline);
        return result == Result.Success
            ? timeline
            : throw new InvalidOperationException($"Failed to create a timeline semaphore: {result}");
    }

    private static Result WaitFor(VulkanContext context, Semaphore timeline, ulong value, TimeSpan timeout)
    {
        var waitInfo = new SemaphoreWaitInfo
        {
            SType = StructureType.SemaphoreWaitInfo,
            SemaphoreCount = 1,
            PSemaphores = &timeline,
            PValues = &value,
        };
        return context.Vk.WaitSemaphores(context.Device, &waitInfo, (ulong)timeout.Ticks * 100);
    }

    private static Result Signal(VulkanContext context, Semaphore timeline, ulong value)
    {
        var signal = new SemaphoreSignalInfo
        {
            SType = StructureType.SemaphoreSignalInfo,
            Semaphore = timeline,
            Value = value,
        };
        return context.Vk.SignalSemaphore(context.Device, &signal);
    }

    private static ulong ReadValue(VulkanContext context, Semaphore timeline)
    {
        ulong value;
        Result result = context.Vk.GetSemaphoreCounterValue(context.Device, timeline, &value);
        return result == Result.Success
            ? value
            : throw new InvalidOperationException($"Failed to read a timeline semaphore: {result}");
    }
}
