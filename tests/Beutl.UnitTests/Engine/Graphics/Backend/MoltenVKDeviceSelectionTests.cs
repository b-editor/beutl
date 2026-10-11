using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

// On macOS Skia renders only through Metal, so the engine can render only on a device MoltenVK drives. MoltenVK names
// a device after the Metal device behind it, so the Intel and AMD GPUs of an Intel Mac do not carry Apple's name:
// the driver ID is what has to decide.
[TestFixture]
[NonParallelizable]
public sealed class MoltenVKDeviceSelectionTests
{
    [TestCase("AMD Radeon Pro 5500M", PhysicalDeviceType.DiscreteGpu, DriverId.Moltenvk, true)]
    [TestCase("Intel(R) UHD Graphics 630", PhysicalDeviceType.IntegratedGpu, DriverId.Moltenvk, true)]
    [TestCase("Apple M2 Pro", PhysicalDeviceType.IntegratedGpu, DriverId.Moltenvk, true)]
    [TestCase("SwiftShader Device (Subzero)", PhysicalDeviceType.Cpu, DriverId.GoogleSwiftshader, false)]
    // Asahi Linux's own driver names Apple silicon after Apple, and Skia renders on it through Vulkan.
    [TestCase("Apple M1 (G13G B1)", PhysicalDeviceType.IntegratedGpu, DriverId.MesaHoneykrisp, false)]
    public void IsMoltenVK_FollowsTheDriverRatherThanTheName(
        string name,
        PhysicalDeviceType type,
        DriverId driver,
        bool expected)
    {
        VulkanPhysicalDeviceInfo device = CreateDevice(name, type, driver);

        Assert.Multiple(() =>
        {
            Assert.That(device.IsMoltenVK, Is.EqualTo(expected));
            Assert.That(device.ToGraphicsDeviceInfo().IsMoltenVK, Is.EqualTo(expected));
        });
    }

    [Test]
    public void OnMacOS_OnlyTheDevicesMoltenVKDrivesAreOffered()
    {
        VulkanPhysicalDeviceInfo swiftShader = CreateDevice(
            "SwiftShader Device (Subzero)", PhysicalDeviceType.Cpu, DriverId.GoogleSwiftshader);
        VulkanPhysicalDeviceInfo intel = CreateDevice(
            "Intel(R) UHD Graphics 630", PhysicalDeviceType.IntegratedGpu, DriverId.Moltenvk);
        VulkanPhysicalDeviceInfo amd = CreateDevice(
            "AMD Radeon Pro 5500M", PhysicalDeviceType.DiscreteGpu, DriverId.Moltenvk);

        // SwiftShader comes first, as the loader may enumerate it: a device left in the list could also be the one
        // chosen automatically.
        Assert.That(
            VulkanInstance.FilterRenderableDevices([swiftShader, intel, amd], macOS: true),
            Is.EqualTo(new[] { intel, amd }));
    }

    [Test]
    public void ElsewhereEveryDeviceIsOffered()
    {
        VulkanPhysicalDeviceInfo swiftShader = CreateDevice(
            "SwiftShader Device (Subzero)", PhysicalDeviceType.Cpu, DriverId.GoogleSwiftshader);
        VulkanPhysicalDeviceInfo radeon = CreateDevice(
            "AMD Radeon RX 7600 (RADV NAVI33)", PhysicalDeviceType.DiscreteGpu, DriverId.MesaRadv);

        Assert.That(
            VulkanInstance.FilterRenderableDevices([swiftShader, radeon], macOS: false),
            Is.EqualTo(new[] { swiftShader, radeon }));
    }

    // The factory then renders on the CPU, and asking for the selected device, as the information page does, has
    // to answer that there is none rather than throw.
    [Test]
    public void OnMacOS_AMacWhoseOnlyDeviceIsSwiftShader_HasNoDeviceToSelect()
    {
        VulkanPhysicalDeviceInfo swiftShader = CreateDevice(
            "SwiftShader Device (Subzero)", PhysicalDeviceType.Cpu, DriverId.GoogleSwiftshader);

        Assert.That(
            VulkanInstance.SelectBest(VulkanInstance.FilterRenderableDevices([swiftShader], macOS: true)),
            Is.Null);
    }

    [Test]
    [Platform("MacOsX")]
    public void OnMacOS_ADeviceMoltenVKDoesNotDrive_IsRefusedBeforeAnythingIsCreatedOnIt()
    {
        VulkanPhysicalDeviceInfo swiftShader = CreateDevice(
            "SwiftShader Device (Subzero)", PhysicalDeviceType.Cpu, DriverId.GoogleSwiftshader);

        // No instance is passed: the device has to be refused before the context calls into Vulkan or Metal at all,
        // so that GraphicsContextFactory falls back to CPU rendering without a half-built context.
        Assert.That(() => new CompositeContext(null!, swiftShader), Throws.TypeOf<NotSupportedException>());
    }

    // The bundled SwiftShader driver is registered on macOS as well, so the instance enumerates it beside the MoltenVK
    // devices. This drives the real driver query and the real factory instead of device infos made up for the test.
    [Test]
    [Platform("MacOsX")]
    public void OnMacOS_TheBundledSwiftShader_IsNotOfferedAndFallsBackToCpuRenderingWhenSelected()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanInstance instance = GraphicsContextFactory.VulkanInstance
            ?? throw new InvalidOperationException("The shared context exists without a Vulkan instance.");

        VulkanPhysicalDeviceInfo[] enumerated = instance.EnumeratePhysicalDevices()
            .Select(instance.GetPhysicalDeviceDetails)
            .ToArray();
        VulkanPhysicalDeviceInfo? swiftShader = enumerated.FirstOrDefault(
            static device => device.Name.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase));
        GraphicsDeviceInfo[] offered = GraphicsContextFactory.GetAvailableDevices();

        Assert.Multiple(() =>
        {
            Assert.That(enumerated.Where(static device => device.IsMoltenVK), Is.Not.Empty,
                "the shared context is up, so MoltenVK has to report at least one device by its driver ID");
            Assert.That(offered, Is.Not.Empty);
            Assert.That(offered.Select(static device => device.IsMoltenVK), Has.All.True);
        });

        if (swiftShader is null)
            Assert.Ignore("The loader does not enumerate the bundled SwiftShader driver on this host.");

        Assert.Multiple(() =>
        {
            Assert.That(swiftShader.DriverId, Is.EqualTo(DriverId.GoogleSwiftshader));
            Assert.That(offered.Select(static device => device.Name), Does.Not.Contain(swiftShader.Name));
        });

        IGraphicsContext? fallback = null;
        RenderThread.Dispatcher.Invoke(() =>
        {
            InstalledGraphics live = GraphicsContextFactory.ExchangeInstalledGraphics(
                new InstalledGraphics(null, instance, swiftShader, FailedToInitialize: false));
            try
            {
                fallback = GraphicsContextFactory.GetOrCreateShared();
            }
            finally
            {
                // The instance is the live one and goes back with the rest; only a context built on SwiftShader is
                // this test's to release.
                InstalledGraphics discarded = GraphicsContextFactory.ExchangeInstalledGraphics(live);
                discarded.SharedContext?.Dispose();
            }
        });

        Assert.That(fallback, Is.Null, "a SwiftShader context on macOS has no Skia GPU context to render with");
    }

    private static VulkanPhysicalDeviceInfo CreateDevice(string name, PhysicalDeviceType type, DriverId driver)
        => new(default, name, type, (uint)Vk.Version13, new VulkanMemoryInfo(0, 0), driver);
}
