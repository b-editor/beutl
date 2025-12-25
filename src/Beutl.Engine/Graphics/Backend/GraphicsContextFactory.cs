using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend;

internal class GraphicsContextFactory
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(GraphicsContextFactory));
    private static bool s_failedToInitialize;
    private static VulkanInstance? s_vulkanInstance;
    private static VulkanPhysicalDeviceInfo? s_selectedPhysicalDevice;

    public static IGraphicsContext? SharedContext { get; private set; }

    public static VulkanInstance? VulkanInstance => s_vulkanInstance;

    public static VulkanPhysicalDeviceInfo[] GetAvailableGpus()
    {
        EnsureVulkanInstance();
        return s_vulkanInstance?.GetAvailableGpus() ?? [];
    }

    public static void SelectGpu(VulkanPhysicalDeviceInfo physicalDevice)
    {
        if (SharedContext != null)
        {
            throw new InvalidOperationException("Cannot change GPU after the graphics context has been created.");
        }

        s_selectedPhysicalDevice = physicalDevice;
    }

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
            s_vulkanInstance = new VulkanInstance(vk, enableValidation: false);
        }
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

    public static VulkanPhysicalDeviceInfo? GetSelectedGpuDetails()
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
        RenderThread.Dispatcher.Invoke(() =>
        {
            SharedContext?.Dispose();
            SharedContext = null;

            if (s_vulkanInstance != null)
            {
                s_vulkanInstance.Dispose();
                s_vulkanInstance = null;
            }

            s_selectedPhysicalDevice = null;
        });
    }
}
