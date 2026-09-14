using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Nodes;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

/// <summary>
/// The 3D allocation paths refuse an extent the device cannot make before the allocator is asked. The
/// load-bearing assertion is that the allocator was never reached: neither shipped driver refuses an
/// over-limit extent itself, so a result proves nothing on its own.
/// </summary>
[TestFixture]
public class DeviceExtentLimitTests
{
    private const int AttachmentBudget = 4096;
    private const int CubeBudget = 2048;

    [TestCase(AttachmentBudget + 1, 1)]
    [TestCase(1, AttachmentBudget + 1)]
    public void Renderer3DInitialize_PastWhatTheDeviceCanAttach_IsRefusedBeforeTheAllocator(int width, int height)
    {
        Mock<IGraphicsContext> device = MockDevice();
        using var renderer = new Renderer3D(device.Object);

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => renderer.Initialize(width, height));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain(AttachmentBudget.ToString()),
                "the refusal must report the limit it could not fit");
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((0, 0)),
                "a refused initialization must not commit an extent");
            AssertNeverAllocated(device);
        });
    }

    [Test]
    public void Renderer3DResize_PastWhatTheDeviceCanAttach_IsRefusedBeforeTheAllocator()
    {
        Mock<IGraphicsContext> device = MockDevice();
        using var renderer = new Renderer3D(device.Object);

        Assert.Throws<InvalidOperationException>(() => renderer.Resize(AttachmentBudget + 1, 1));

        AssertNeverAllocated(device);
    }

    [Test]
    public void RenderNode3DInitialize_PastWhatTheDeviceCanAttach_IsRefusedBeforeTheNodeAllocates()
    {
        Mock<IGraphicsContext> device = MockDevice();
        using var node = new ProbeNode(device.Object);

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => node.Initialize(AttachmentBudget + 1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain(AttachmentBudget.ToString()));
            Assert.That(node.InitializeCalls, Is.Zero, "the node's own allocation must never run");
            Assert.That((node.Width, node.Height), Is.EqualTo((0, 0)));
        });
    }

    [Test]
    public void RenderNode3DInitialize_WithinWhatTheDeviceCanAttach_ReachesTheNode()
    {
        // Positive control: the guard has to let the largest extent the device can attach through, or it
        // is refusing everything rather than measuring anything.
        Mock<IGraphicsContext> device = MockDevice();
        using var node = new ProbeNode(device.Object);

        node.Initialize(AttachmentBudget, AttachmentBudget);

        Assert.Multiple(() =>
        {
            Assert.That(node.InitializeCalls, Is.EqualTo(1));
            Assert.That((node.Width, node.Height), Is.EqualTo((AttachmentBudget, AttachmentBudget)));
        });
    }

    [Test]
    public void RenderNode3DResize_PastWhatTheDeviceCanAttach_KeepsTheCurrentExtent()
    {
        Mock<IGraphicsContext> device = MockDevice();
        using var node = new ProbeNode(device.Object);
        node.Initialize(16, 16);

        Assert.Throws<InvalidOperationException>(() => node.Resize(1, AttachmentBudget + 1));

        Assert.Multiple(() =>
        {
            Assert.That(node.ResizeCalls, Is.Zero, "a refused resize must not reallocate");
            Assert.That((node.Width, node.Height), Is.EqualTo((16, 16)),
                "a refused resize must leave the extent the node still holds resources for");
        });
    }

    [Test]
    public void ADeviceThatReportsNoLimit_LeavesTheExtentToTheAllocator()
    {
        // Mirrors the 2D path: a limit of zero means the device did not answer, and a guess would refuse
        // an allocation the device might well make.
        Mock<IGraphicsContext> device = MockDevice(attachment: 0);
        using var node = new ProbeNode(device.Object);

        node.Initialize(100_000, 1);

        Assert.That(node.InitializeCalls, Is.EqualTo(1));
    }

    [Test]
    public void PointShadowResize_PastWhatTheDeviceCanMakeOfACube_IsRefusedBeforeTheAllocator()
    {
        Mock<IGraphicsContext> device = MockDevice(attachment: AttachmentBudget, cube: CubeBudget);
        using var pass = new PointShadowPass(device.Object, Mock.Of<IShaderCompiler>());

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => pass.ResizeShadowMap(CubeBudget + 1));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain(CubeBudget.ToString()),
                "a cube face is bounded by the cube limit, not the attachment one");
            AssertNeverAllocated(device);
        });
    }

    [Test]
    public void PointShadowResize_PastWhatTheDeviceCanAttach_IsRefusedEvenWhenTheCubeWouldFit()
    {
        // Each face is drawn into a 2D attachment before it is copied into the cube, so the cube limit
        // alone is not the budget.
        const int smallAttachment = 1024;
        Mock<IGraphicsContext> device = MockDevice(attachment: smallAttachment, cube: CubeBudget);
        using var pass = new PointShadowPass(device.Object, Mock.Of<IShaderCompiler>());

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => pass.ResizeShadowMap(CubeBudget));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain(smallAttachment.ToString()));
            AssertNeverAllocated(device);
        });
    }

    [Test]
    public void PointShadowResize_WithinBothLimits_ReachesTheAllocator()
    {
        // Positive control for the cube guard: a face that fits both limits must be allocated as asked.
        var device = new Mock<IGraphicsContext>(MockBehavior.Loose) { DefaultValue = DefaultValue.Mock };
        device.SetupGet(c => c.MaxAttachmentDimension).Returns(AttachmentBudget);
        device.SetupGet(c => c.MaxCubeFaceDimension).Returns(CubeBudget);
        using var pass = new PointShadowPass(device.Object, Mock.Of<IShaderCompiler>());

        pass.ResizeShadowMap(CubeBudget);

        device.Verify(c => c.CreateTextureCube(CubeBudget, It.IsAny<TextureFormat>()), Times.Once);
    }

    [Test]
    public void ShadowResize_PastWhatTheDeviceCanAttach_IsRefusedBeforeTheAllocator()
    {
        Mock<IGraphicsContext> device = MockDevice();
        using var pass = new ShadowPass(device.Object, Mock.Of<IShaderCompiler>());

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => pass.ResizeShadowMap(AttachmentBudget + 1));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain(AttachmentBudget.ToString()));
            AssertNeverAllocated(device);
        });
    }

    [Test]
    public void TheCubeFaceBudget_IsTheSmallerOfTheTwoLimits_AndFallsBackWhenOneIsMissing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                DeviceExtentLimits.ResolveCubeFaceAttachmentBudget(MockDevice(attachment: 4096, cube: 2048).Object),
                Is.EqualTo(2048));
            Assert.That(
                DeviceExtentLimits.ResolveCubeFaceAttachmentBudget(MockDevice(attachment: 1024, cube: 2048).Object),
                Is.EqualTo(1024));
            Assert.That(
                DeviceExtentLimits.ResolveCubeFaceAttachmentBudget(MockDevice(attachment: 0, cube: 2048).Object),
                Is.EqualTo(2048));
            Assert.That(
                DeviceExtentLimits.ResolveCubeFaceAttachmentBudget(MockDevice(attachment: 1024, cube: 0).Object),
                Is.EqualTo(1024));
        });
    }

    private static Mock<IGraphicsContext> MockDevice(int attachment = AttachmentBudget, int cube = CubeBudget)
    {
        // Strict: any allocation call that is not set up fails the test, which is the point.
        var device = new Mock<IGraphicsContext>(MockBehavior.Strict);
        device.SetupGet(c => c.MaxAttachmentDimension).Returns(attachment);
        device.SetupGet(c => c.MaxCubeFaceDimension).Returns(cube);
        device.Setup(c => c.CreateShaderCompiler()).Returns(Mock.Of<IShaderCompiler>());
        return device;
    }

    private static void AssertNeverAllocated(Mock<IGraphicsContext> device)
    {
        device.Verify(
            c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()),
            Times.Never,
            "an attachment the device cannot make must never reach the allocator");
        device.Verify(
            c => c.CreateTextureCube(It.IsAny<int>(), It.IsAny<TextureFormat>()),
            Times.Never,
            "a cube the device cannot make must never reach the allocator");
    }

    private sealed class ProbeNode(IGraphicsContext context) : RenderNode3D(context)
    {
        public int InitializeCalls { get; private set; }

        public int ResizeCalls { get; private set; }

        protected override void OnInitialize(int width, int height) => InitializeCalls++;

        protected override void OnResize(int width, int height) => ResizeCalls++;

        protected override void OnDispose()
        {
        }
    }
}
