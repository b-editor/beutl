using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend;

public class GraphicsContextFactory
{
    internal const string VulkanValidationEnvironmentVariable = "BEUTL_VULKAN_VALIDATION";
    internal const string VulkanValidationAppContextSwitch = "Beutl.Graphics.Vulkan.EnableValidation";
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(GraphicsContextFactory));
    private static bool s_failedToInitialize;
    private static VulkanInstance? s_vulkanInstance;
    private static VulkanPhysicalDeviceInfo? s_selectedPhysicalDevice;

    public static IGraphicsContext? SharedContext { get; private set; }

    internal static VulkanInstance? VulkanInstance => s_vulkanInstance;

    /// <summary>
    /// Gets all available graphics devices.
    /// </summary>
    /// <returns>An array of available graphics devices.</returns>
    public static GraphicsDeviceInfo[] GetAvailableDevices()
    {
        EnsureVulkanInstance();
        var gpus = s_vulkanInstance?.GetAvailableGpus() ?? [];
        return gpus.Select(g => g.ToGraphicsDeviceInfo()).ToArray();
    }

    internal static VulkanPhysicalDeviceInfo[] GetAvailableGpus()
    {
        EnsureVulkanInstance();
        return s_vulkanInstance?.GetAvailableGpus() ?? [];
    }

    internal static void SelectGpu(VulkanPhysicalDeviceInfo physicalDevice)
    {
        if (SharedContext != null)
        {
            throw new InvalidOperationException("Cannot change GPU after the graphics context has been created.");
        }

        s_selectedPhysicalDevice = physicalDevice;
    }

    /// <summary>
    /// Selects a GPU by its name.
    /// </summary>
    /// <param name="gpuName">The name of the GPU to select.</param>
    /// <returns>True if a matching GPU was found and selected; otherwise, false.</returns>
    public static bool SelectGpuByName(string? gpuName)
    {
        if (string.IsNullOrEmpty(gpuName))
            return false;

        EnsureVulkanInstance();

        var availableGpus = s_vulkanInstance?.GetAvailableGpus() ?? [];
        var matchingGpu = availableGpus.FirstOrDefault(g => g.Name == gpuName);

        if (matchingGpu != null)
        {
            s_selectedPhysicalDevice = matchingGpu;
            return true;
        }

        return false;
    }

    private static void EnsureVulkanInstance()
    {
        if (s_vulkanInstance == null)
        {
            VulkanSetup.Setup();
            var vk = Vk.GetApi();
            s_vulkanInstance = new VulkanInstance(vk, IsVulkanValidationEnabled());
        }
    }

    internal static bool IsVulkanValidationEnabled()
    {
        if (AppContext.TryGetSwitch(VulkanValidationAppContextSwitch, out bool enabled) && enabled)
            return true;

        string? value = Environment.GetEnvironmentVariable(VulkanValidationEnvironmentVariable);
        return value is not null
            && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    public static IGraphicsContext CreateContext()
    {
        EnsureVulkanInstance();
        var physicalDevice = s_selectedPhysicalDevice ?? s_vulkanInstance!.SelectBestPhysicalDevice();

        if (OperatingSystem.IsMacOS())
            return new CompositeContext(s_vulkanInstance!, physicalDevice);

        return new VulkanContext(s_vulkanInstance!, physicalDevice);
    }

    public static IGraphicsContext? GetOrCreateShared()
    {
        if (s_failedToInitialize)
            return null;

        if (SharedContext == null)
        {
            RenderThread.Dispatcher.VerifyAccess();

            try
            {
                SharedContext = CreateContext();
            }
            catch (Exception e)
            {
                s_logger.LogError(e, "Failed to initialize shared graphics context.");
                s_failedToInitialize = true;
            }
        }

        return SharedContext;
    }

    /// <summary>
    /// Gets the currently selected or best available graphics device.
    /// </summary>
    /// <returns>The selected graphics device info, or null if no Vulkan instance exists.</returns>
    public static GraphicsDeviceInfo? GetSelectedDevice()
    {
        if (s_vulkanInstance == null)
            return null;

        VulkanPhysicalDeviceInfo? selectedPhysicalDevice = s_selectedPhysicalDevice ?? default;
        if (selectedPhysicalDevice == null || selectedPhysicalDevice.Device.Handle == IntPtr.Zero)
        {
            selectedPhysicalDevice = s_vulkanInstance.SelectBestPhysicalDevice();
        }

        return selectedPhysicalDevice?.ToGraphicsDeviceInfo();
    }

    internal static VulkanPhysicalDeviceInfo? GetSelectedGpuDetails()
    {
        if (s_vulkanInstance == null)
            return null;

        VulkanPhysicalDeviceInfo? selectedPhysicalDevice = s_selectedPhysicalDevice ?? default;
        if (selectedPhysicalDevice == null || selectedPhysicalDevice.Device.Handle == IntPtr.Zero)
        {
            selectedPhysicalDevice = s_vulkanInstance.SelectBestPhysicalDevice();
        }

        return selectedPhysicalDevice;
    }

    public static IEnumerable<string> GetEnabledExtensions()
    {
        if (SharedContext is VulkanContext vulkanContext)
        {
            return vulkanContext.EnabledExtensions;
        }
        else if (SharedContext is CompositeContext compositeContext)
        {
            return compositeContext.Vulkan.EnabledExtensions;
        }

        return [];
    }

    public static void Shutdown()
    {
        RenderThread.Dispatcher.Invoke(static () =>
        {
            // The flush speaks for a device that may already be abandoned or lost, so its failure has to
            // reach the caller. What it must not decide is whether graphics are released: a context left
            // installed is handed straight back by the next GetOrCreateShared, and both RenderTargetPool's
            // retained slots and the buffer-dimension memo are only invalidated by the context changing.
            try
            {
                GpuResourceReclaimQueue.FlushAndDrain();
            }
            finally
            {
                ReleaseInstalledGraphics();
            }
        });
    }

    /// <summary>Clears the installed graphics state before destroying it, so a failure cannot strand it.</summary>
    private static void ReleaseInstalledGraphics()
    {
        IGraphicsContext? context = SharedContext;
        VulkanInstance? vulkanInstance = s_vulkanInstance;
        SharedContext = null;
        s_vulkanInstance = null;
        s_selectedPhysicalDevice = null;

        try
        {
            context?.Dispose();
        }
        finally
        {
            vulkanInstance?.Dispose();
        }
    }

    /// <summary>Installs <paramref name="replacement"/> in place of the live state, reporting what it replaced.</summary>
    /// <remarks>
    /// <see cref="Shutdown"/> destroys state the whole process shares, so the only way to exercise it without
    /// taking the device out from under every other caller is to stand in for that state, tear the stand-in
    /// down, and put the real state back.
    /// </remarks>
    internal static InstalledGraphics ExchangeInstalledGraphics(InstalledGraphics replacement)
    {
        var previous = new InstalledGraphics(
            SharedContext,
            s_vulkanInstance,
            s_selectedPhysicalDevice,
            s_failedToInitialize);
        SharedContext = replacement.SharedContext;
        s_vulkanInstance = replacement.VulkanInstance;
        s_selectedPhysicalDevice = replacement.PhysicalDevice;
        s_failedToInitialize = replacement.FailedToInitialize;
        return previous;
    }
}

/// <summary>The process-wide graphics state <see cref="GraphicsContextFactory.Shutdown"/> tears down.</summary>
internal readonly record struct InstalledGraphics(
    IGraphicsContext? SharedContext,
    VulkanInstance? VulkanInstance,
    VulkanPhysicalDeviceInfo? PhysicalDevice,
    bool FailedToInitialize);
