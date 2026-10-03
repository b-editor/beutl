using Beutl.Graphics.Backend.Vulkan;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Beutl.Graphics.Backend.Metal;

using Semaphore = Silk.NET.Vulkan.Semaphore;

/// <summary>Holds Skia's Metal queue behind the engine's MoltenVK work on the GPU.</summary>
/// <remarks>
/// The two queues share no submission order, so a hand-off from Vulkan to Skia used to wait for completion on the
/// CPU. A Vulkan timeline semaphore that MoltenVK backs with an <c>MTLSharedEvent</c> is visible to both: a Vulkan
/// submission signals the next value, and Skia's queue waits for it through an event command committed between
/// Skia's own command buffers.
/// <para>
/// The other direction keeps its CPU wait on purpose. Ordering Skia's writes before a Vulkan pass on the GPU let
/// the CPU run ahead and queue more Skia work, which then competed with the pass on the GPU: a result read right
/// after the pass arrived about 3 ms later on Apple silicon, with no gain in the render benchmarks.
/// </para>
/// </remarks>
internal sealed unsafe class MetalVulkanTimeline : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<MetalVulkanTimeline>();
    private readonly MetalContext _metal;
    private readonly VulkanContext _vulkan;
    private readonly Semaphore _semaphore;
    // Owned by the semaphore; MoltenVK releases it when the semaphore is destroyed.
    private readonly IntPtr _sharedEvent;
    private ulong _value;
    private bool _disposed;

    private MetalVulkanTimeline(MetalContext metal, VulkanContext vulkan, Semaphore semaphore, IntPtr sharedEvent)
    {
        _metal = metal;
        _vulkan = vulkan;
        _semaphore = semaphore;
        _sharedEvent = sharedEvent;
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

    /// <summary>Submits the recorded Vulkan work and holds Skia's next command buffers until it finishes.</summary>
    public void OrderVulkanWorkBeforeSkia()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong value = ++_value;
        _vulkan.SubmitSignalingTimeline(_semaphore, value);
        _metal.CommitWaitForEvent(_sharedEvent, value);
    }

    /// <remarks>Both queues have to be idle: Vulkan may still signal the timeline and Metal wait on it.</remarks>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _vulkan.Vk.DestroySemaphore(_vulkan.Device, _semaphore, null);
    }
}
