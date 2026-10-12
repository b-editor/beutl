using System.Runtime.ExceptionServices;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
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
        return GetAvailableGpus().Select(g => g.ToGraphicsDeviceInfo()).ToArray();
    }

    internal static VulkanPhysicalDeviceInfo[] GetAvailableGpus()
    {
        EnsureVulkanInstance();
        return s_vulkanInstance?.GetAvailableGpus() ?? [];
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

        var availableGpus = GetAvailableGpus();
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

    /// <summary>
    /// Predicts whether the shared context can render 3D, from any thread.
    /// </summary>
    /// <remarks>
    /// Before a context is built the answer is <see langword="true"/>, as refusing 3D on a prediction would drop it
    /// from the very request whose allocation builds the backend; it turns false only once that is settled.
    /// </remarks>
    /// <returns>
    /// <see langword="false"/> when the shared context is known not to support 3D rendering; otherwise
    /// <see langword="true"/>.
    /// </returns>
    internal static bool Predict3DRenderingSupport()
    {
        IGraphicsContext? installed = SharedContext;
        return installed is not null ? installed.Supports3DRendering : !s_failedToInitialize;
    }

    /// <summary>Predicts the 3D extents the shared context can attach, from any thread.</summary>
    /// <remarks>
    /// Before a context exists the answer is <see cref="Device3DExtentBudget.Unreported"/>, so nothing is refused
    /// on a guess about a device not chosen yet; a context that cannot render 3D reports nothing either.
    /// </remarks>
    internal static Device3DExtentBudget Predict3DExtentBudget()
    {
        IGraphicsContext? installed = SharedContext;
        return installed is null || !installed.Supports3DRendering
            ? Device3DExtentBudget.Unreported
            : Device3DExtentBudget.FromContext(installed);
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
    /// <returns>
    /// The selected graphics device info, or null if no Vulkan instance exists or no device can render.
    /// </returns>
    public static GraphicsDeviceInfo? GetSelectedDevice()
    {
        return GetSelectedGpuDetails()?.ToGraphicsDeviceInfo();
    }

    internal static VulkanPhysicalDeviceInfo? GetSelectedGpuDetails()
    {
        if (s_vulkanInstance == null)
            return null;

        VulkanPhysicalDeviceInfo? selectedPhysicalDevice = s_selectedPhysicalDevice;
        if (selectedPhysicalDevice == null)
        {
            // On a Mac without a MoltenVK device nothing is left to choose, and the shared context renders on the
            // CPU. There is no device to report then, and a throw would end the app from the information page's
            // render-thread callback.
            selectedPhysicalDevice = VulkanInstance.SelectBest(s_vulkanInstance.GetAvailableGpus());
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
            List<Exception> failures = [];

            try
            {
                // A pool holds its targets until a request names a different context, which after this call
                // never arrives, so nothing else ever destroys them on the device that owns them. Retiring
                // first rather than last is what puts their backend releases ahead of both the flush that
                // submits them and the destruction of the device they name.
                try
                {
                    RenderTargetPool.RetireRetainedTargetsBeforeContextTeardown();
                }
                catch (Exception ex)
                {
                    AppendTeardownFailure(failures, ex);
                }

                // The flush speaks for a device that may already be abandoned or lost, so its failure has
                // to reach the caller. What it must not decide is whether the queue is discharged.
                try
                {
                    GpuResourceReclaimQueue.FlushAndDrain();
                }
                catch (Exception ex)
                {
                    AppendTeardownFailure(failures, ex);
                }

                // Everything still queued is owned by the context released below and destroys itself
                // through that context's command pool. Discharging here re-defers each destroy onto a pool
                // that is still live, which retires it behind the submission that reads it; leaving the
                // queue for after the release would instead run every destroy immediately against a
                // device that no longer exists.
                GpuResourceReclaimQueue.DrainAfterContextSync();
            }
            finally
            {
                // A context left installed is handed straight back by the next GetOrCreateShared, so no failure
                // above may decide whether the release happens.
                try
                {
                    ReleaseInstalledGraphics();
                }
                catch (Exception ex)
                {
                    AppendTeardownFailure(failures, ex);
                }
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "More than one graphics teardown step failed.",
                    failures);
            }
        });
    }

    private static void AppendTeardownFailure(List<Exception> failures, Exception failure)
    {
        if (failure is AggregateException aggregate)
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        else
            failures.Add(failure);
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
    /// <remarks>Lets a test tear a stand-in down with <see cref="Shutdown"/> and then put the real state back.</remarks>
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
