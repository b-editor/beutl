using Beutl.Graphics.Backend;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[NonParallelizable]
public class GraphicsContextFactoryTests
{
    [TestCase("1", true)]
    [TestCase("true", true)]
    [TestCase("YES", true)]
    [TestCase("on", true)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void IsVulkanValidationEnabled_ParsesEnvironmentSetting(string? value, bool expected)
    {
        bool hadPreviousSwitch = AppContext.TryGetSwitch(
            GraphicsContextFactory.VulkanValidationAppContextSwitch,
            out bool previousSwitch);
        string? previous = Environment.GetEnvironmentVariable(
            GraphicsContextFactory.VulkanValidationEnvironmentVariable);
        try
        {
            AppContext.SetSwitch(GraphicsContextFactory.VulkanValidationAppContextSwitch, false);
            Environment.SetEnvironmentVariable(
                GraphicsContextFactory.VulkanValidationEnvironmentVariable,
                value);

            Assert.That(GraphicsContextFactory.IsVulkanValidationEnabled(), Is.EqualTo(expected));
        }
        finally
        {
            AppContext.SetSwitch(
                GraphicsContextFactory.VulkanValidationAppContextSwitch,
                hadPreviousSwitch && previousSwitch);
            Environment.SetEnvironmentVariable(
                GraphicsContextFactory.VulkanValidationEnvironmentVariable,
                previous);
        }
    }

    // The prediction is what a recording reads, so it must be settled by the installed state alone: true
    // until the process has established that no 3D backend exists, false from then on.
    [TestCase(false, null, true, TestName = "Predict3DRenderingSupport_BeforeAnyContext_IsTrue")]
    [TestCase(true, null, false, TestName = "Predict3DRenderingSupport_AfterInitializationFailed_IsFalse")]
    [TestCase(false, true, true, TestName = "Predict3DRenderingSupport_UnderA3DCapableContext_IsTrue")]
    [TestCase(false, false, false, TestName = "Predict3DRenderingSupport_UnderAContextWithout3D_IsFalse")]
    public void Predict3DRenderingSupport_FollowsTheInstalledState(
        bool failedToInitialize,
        bool? installedSupports3D,
        bool expected)
    {
        IGraphicsContext? installed = null;
        if (installedSupports3D is { } supports3D)
        {
            var context = new Mock<IGraphicsContext>();
            context.SetupGet(static c => c.Supports3DRendering).Returns(supports3D);
            installed = context.Object;
        }

        InstalledGraphics previous = GraphicsContextFactory.ExchangeInstalledGraphics(
            new InstalledGraphics(installed, null, null, failedToInitialize));
        try
        {
            Assert.That(GraphicsContextFactory.Predict3DRenderingSupport(), Is.EqualTo(expected));
        }
        finally
        {
            GraphicsContextFactory.ExchangeInstalledGraphics(previous);
        }
    }

    // The extent budget is settled the same way: only a context that exists and can render 3D reports
    // limits, and every other state answers Unreported, which refuses nothing.
    [TestCase(null, 0, 0, 0, 0, TestName = "Predict3DExtentBudget_BeforeAnyContext_IsUnreported")]
    [TestCase(true, 8192, 4096, 8192, 4096, TestName = "Predict3DExtentBudget_UnderA3DCapableContext_IsTheDeviceLimits")]
    [TestCase(false, 8192, 4096, 0, 0, TestName = "Predict3DExtentBudget_UnderAContextWithout3D_IsUnreported")]
    [TestCase(true, 0, 4096, 0, 4096, TestName = "Predict3DExtentBudget_CarriesTheOneLimitTheDeviceReported")]
    public void Predict3DExtentBudget_FollowsTheInstalledState(
        bool? installedSupports3D,
        int installedAttachment,
        int installedCube,
        int expectedAttachment,
        int expectedCube)
    {
        IGraphicsContext? installed = null;
        if (installedSupports3D is { } supports3D)
        {
            var context = new Mock<IGraphicsContext>();
            context.SetupGet(static c => c.Supports3DRendering).Returns(supports3D);
            context.SetupGet(c => c.MaxAttachmentDimension).Returns(installedAttachment);
            context.SetupGet(c => c.MaxCubeFaceDimension).Returns(installedCube);
            installed = context.Object;
        }

        InstalledGraphics previous = GraphicsContextFactory.ExchangeInstalledGraphics(
            new InstalledGraphics(installed, null, null, FailedToInitialize: false));
        try
        {
            Assert.That(
                GraphicsContextFactory.Predict3DExtentBudget(),
                Is.EqualTo(new Device3DExtentBudget(expectedAttachment, expectedCube)));
        }
        finally
        {
            GraphicsContextFactory.ExchangeInstalledGraphics(previous);
        }
    }

    [Test]
    public void GetAvailableDevices_ReturnsAtLeastOne()
    {
        VulkanTestEnvironment.EnsureAvailable();

        var devices = GraphicsContextFactory.GetAvailableDevices();

        Assert.That(devices, Is.Not.Null);
        Assert.That(devices.Length, Is.GreaterThan(0),
            "Vulkanインスタンスは作成できているのにデバイスが0件なのは想定外です。");

        foreach (var device in devices)
        {
            Assert.That(device.Name, Is.Not.Null.And.Not.Empty);
            Assert.That(device.ApiVersion, Is.Not.Null.And.Not.Empty);
        }
    }

    [Test]
    public void GetSelectedDevice_ReflectsSelection()
    {
        VulkanTestEnvironment.EnsureAvailable();

        var selected = GraphicsContextFactory.GetSelectedDevice();
        Assert.That(selected, Is.Not.Null);

        var devices = GraphicsContextFactory.GetAvailableDevices();
        Assert.That(devices.Any(d => d.Name == selected!.Name), Is.True,
            "GetSelectedDevice の返すデバイス名が GetAvailableDevices に含まれていません。");
    }

    [Test]
    public void GetOrCreateShared_ReturnsSameInstance()
    {
        var first = VulkanTestEnvironment.EnsureAvailable();

        var second = VulkanTestEnvironment.InvokeOnRenderThread(GraphicsContextFactory.GetOrCreateShared);

        Assert.That(second, Is.SameAs(first), "共有 GraphicsContext は使い回されるべきです。");
        Assert.That(GraphicsContextFactory.SharedContext, Is.SameAs(first));
    }

    [Test]
    public void SharedContext_HasExpectedBackend()
    {
        var ctx = VulkanTestEnvironment.EnsureAvailable();

        var expected = OperatingSystem.IsMacOS() ? GraphicsBackend.Metal : GraphicsBackend.Vulkan;
        Assert.That(ctx.Backend, Is.EqualTo(expected));
    }

    [Test]
    public void GetEnabledExtensions_IsNotEmpty()
    {
        VulkanTestEnvironment.EnsureAvailable();

        var extensions = GraphicsContextFactory.GetEnabledExtensions().ToArray();

        Assert.That(extensions, Is.Not.Null);
        Assert.That(extensions.Length, Is.GreaterThan(0));
    }

    [Test]
    public void SelectGpuByName_RejectsNullOrEmpty()
    {
        VulkanTestEnvironment.EnsureAvailable();

        Assert.That(GraphicsContextFactory.SelectGpuByName(null), Is.False);
        Assert.That(GraphicsContextFactory.SelectGpuByName(string.Empty), Is.False);
    }

    [Test]
    public void SelectGpuByName_ReturnsFalseForUnknownName()
    {
        VulkanTestEnvironment.EnsureAvailable();

        Assert.That(GraphicsContextFactory.SelectGpuByName("__non_existent_gpu__"), Is.False);
    }
}
