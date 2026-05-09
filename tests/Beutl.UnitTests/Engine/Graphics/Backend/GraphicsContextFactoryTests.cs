using Beutl.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[NonParallelizable]
public class GraphicsContextFactoryTests
{
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
