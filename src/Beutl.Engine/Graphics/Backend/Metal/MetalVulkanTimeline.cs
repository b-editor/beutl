using Beutl.Graphics.Backend.Vulkan;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Beutl.Graphics.Backend.Metal;

using Semaphore = Silk.NET.Vulkan.Semaphore;

/// <summary>Orders work between Skia's Metal queue and the engine's MoltenVK queue on the GPU.</summary>
/// <remarks>
/// The two queues share no submission order, so each hand-off between them used to wait for completion on the
/// CPU. A Vulkan timeline semaphore that MoltenVK backs with an <c>MTLSharedEvent</c> is visible to both: the
/// Vulkan queue signals and waits on it through ordinary submissions, and Skia's queue through event commands
/// committed between Skia's own command buffers. Every hand-off takes the next value, so one timeline serves
/// both directions.
/// </remarks>
internal sealed unsafe class MetalVulkanTimeline : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<MetalVulkanTimeline>();
    private readonly MetalContext _metal;
    private readonly VulkanContext _vulkan;
    private readonly Semaphore _semaphore;
    // Owned by the semaphore; MoltenVK releases it when the semaphore is destroyed.
    private readonly IntPtr _sharedEvent;
    private readonly IntPtr _probeBuffer;
    private ulong _value;
    private bool _disposed;

    private MetalVulkanTimeline(MetalContext metal, VulkanContext vulkan, Semaphore semaphore, IntPtr sharedEvent)
    {
        _metal = metal;
        _vulkan = vulkan;
        _semaphore = semaphore;
        _sharedEvent = sharedEvent;
        _probeBuffer = metal.CreateProbeBuffer();
    }

    /// <summary>Creates the timeline, or returns <see langword="null"/> when the device cannot provide one.</summary>
    /// <remarks>The timeline is an optimization: without it every hand-off keeps its CPU completion wait.</remarks>
    public static MetalVulkanTimeline? TryCreate(MetalContext metal, VulkanContext vulkan)
    {
        if (!vulkan.SupportsTimelineSemaphores
            || !vulkan.Vk.TryGetDeviceExtension(vulkan.Instance, vulkan.Device, out ExtMetalObjects metalObjects))
        {
            return null;
        }

        var exportInfo = new ExportMetalObjectCreateInfoEXT
        {
            SType = StructureType.ExportMetalObjectCreateInfoExt,
            ExportObjectType = ExportMetalObjectTypeFlagsEXT.SharedEventBitExt,
        };
        var typeInfo = new SemaphoreTypeCreateInfo
        {
            SType = StructureType.SemaphoreTypeCreateInfo,
            PNext = &exportInfo,
            SemaphoreType = SemaphoreType.Timeline,
            InitialValue = 0,
        };
        var createInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = &typeInfo,
        };

        Semaphore semaphore;
        if (vulkan.Vk.CreateSemaphore(vulkan.Device, &createInfo, null, &semaphore) != Result.Success)
            return null;

        try
        {
            var sharedEventInfo = new ExportMetalSharedEventInfoEXT
            {
                SType = StructureType.ExportMetalSharedEventInfoExt,
                Semaphore = semaphore,
            };
            var objectsInfo = new ExportMetalObjectsInfoEXT
            {
                SType = StructureType.ExportMetalObjectsInfoExt,
                PNext = &sharedEventInfo,
            };
            metalObjects.ExportMetalObjects(vulkan.Device, &objectsInfo);
            if (sharedEventInfo.MtlSharedEvent == IntPtr.Zero)
            {
                vulkan.Vk.DestroySemaphore(vulkan.Device, semaphore, null);
                return null;
            }

            return new MetalVulkanTimeline(metal, vulkan, semaphore, sharedEventInfo.MtlSharedEvent);
        }
        catch (Exception ex)
        {
            vulkan.Vk.DestroySemaphore(vulkan.Device, semaphore, null);
            s_logger.LogWarning(ex, "Metal/Vulkan GPU ordering is unavailable; hand-offs wait on the CPU.");
            return null;
        }
    }

    /// <summary>Holds the next Vulkan batch until Skia has finished writing <paramref name="metalTexture"/>.</summary>
    /// <remarks>Skia's recorded work has to be submitted to its queue before this is called.</remarks>
    public void OrderSkiaWritesBeforeVulkan(IntPtr metalTexture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong value = ++_value;
        // Commit the signal before any Vulkan batch can wait on it, so the wait always has a producer.
        _metal.CommitSignalAfterReading(metalTexture, _probeBuffer, _sharedEvent, value);
        _vulkan.WaitForTimelineOnNextSubmission(_semaphore, value);
    }

    /// <summary>Submits the recorded Vulkan work and holds Skia's next command buffers until it finishes.</summary>
    public void OrderVulkanWorkBeforeSkia()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong value = ++_value;
        _vulkan.SubmitSignalingTimeline(_semaphore, value);
        _metal.CommitWaitForEvent(_sharedEvent, value);
    }

    /// <remarks>Both queues have to be idle: either may still be waiting on or signaling the timeline.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        MetalContext.ReleaseHandle(_probeBuffer);
        _vulkan.Vk.DestroySemaphore(_vulkan.Device, _semaphore, null);
    }
}
