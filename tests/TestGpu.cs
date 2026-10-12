using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl;

/// <summary>
/// The gate for tests that render on the GPU. Each test states what it needs: any Vulkan device, SwiftShader
/// included, or a hardware GPU.
/// </summary>
/// <remarks>
/// <para>
/// Every test project that renders on the GPU links this file. A test that cannot get what it needs skips, with a
/// reason that starts with <see cref="NoDeviceReason"/> or <see cref="NoHardwareGpuReason"/>.
/// </para>
/// <para>
/// CI provides SwiftShader and declares it with <c>BEUTL_REQUIRE_GPU=1</c>, which only
/// <c>.github/scripts/ci_tests.py</c> reads. There, a skip for want of a device fails the run. A hardware-only skip
/// does not, because no CI job has a hardware GPU.
/// </para>
/// </remarks>
internal static class TestGpu
{
    /// <summary>Starts the reason of a skip for want of any Vulkan device. <c>ci_tests.py</c> matches it.</summary>
    public const string NoDeviceReason = "No GPU device:";

    /// <summary>Starts the reason of a skip on a device that is not a hardware GPU. <c>ci_tests.py</c> matches it.</summary>
    public const string NoHardwareGpuReason = "Needs a hardware GPU:";

    private static readonly Lazy<SharedDevice> s_shared = new(Probe);

    /// <summary>Gets whether any Vulkan device is available, SwiftShader included.</summary>
    public static bool IsAvailable => s_shared.Value.Context is not null;

    /// <summary>Gets why no Vulkan device is available, or <see langword="null"/> when one is.</summary>
    public static string? UnavailableReason => s_shared.Value.UnavailableReason;

    /// <summary>Returns the shared context of any Vulkan device, SwiftShader included, or skips the test.</summary>
    public static IGraphicsContext RequireAnyDevice()
    {
        SharedDevice shared = s_shared.Value;
        if (shared.Context is null)
            Assert.Ignore($"{NoDeviceReason} {shared.UnavailableReason}");

        return shared.Context;
    }

    /// <summary>Skips the rest of the test unless it runs on a hardware GPU.</summary>
    /// <param name="expectation">What only a hardware GPU is held to. It ends the skip reason.</param>
    /// <remarks>
    /// SwiftShader and other CPU implementations skip, and so does a device whose type is unknown.
    /// <c>BEUTL_REQUIRE_GPU</c> does not change this: the device CI provides is SwiftShader.
    /// </remarks>
    public static void RequireHardwareGpu(string expectation)
    {
        RequireAnyDevice();
        GraphicsDeviceInfo? device = s_shared.Value.Device;
        if (device?.DeviceType is GraphicsDeviceType.Integrated or GraphicsDeviceType.Discrete)
            return;

        string found = device is null ? "the device type is unknown" : $"{device.Name} is a {device.DeviceType} device";
        Assert.Ignore($"{NoHardwareGpuReason} {found}. {expectation}");
    }

    private static SharedDevice Probe()
    {
        try
        {
            // The factory owns the shared context, so an unavailable device leaves no handle behind to release.
            return RenderThread.Dispatcher.Invoke(static () =>
            {
                IGraphicsContext? context = GraphicsContextFactory.GetOrCreateShared();
                if (context is null)
                {
                    return new SharedDevice(
                        null,
                        null,
                        "GraphicsContextFactory.GetOrCreateShared returned null, so no Vulkan driver (SwiftShader, "
                        + "MoltenVK or a GPU's) could be initialized.");
                }

                if (!context.Supports3DRendering)
                    return new SharedDevice(null, null, $"the shared {context.Backend} context cannot render 3D.");

                return new SharedDevice(context, SelectedDevice(), null);
            });
        }
        catch (Exception ex)
        {
            return new SharedDevice(
                null,
                null,
                $"initializing the shared graphics context threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static GraphicsDeviceInfo? SelectedDevice()
    {
        try
        {
            // The factory chose the context's device by the same rule a moment ago.
            return GraphicsContextFactory.GetSelectedDevice();
        }
        catch (Exception)
        {
            // An unknown device only makes hardware-only expectations skip.
            return null;
        }
    }

    private sealed record SharedDevice(
        IGraphicsContext? Context,
        GraphicsDeviceInfo? Device,
        string? UnavailableReason);
}
