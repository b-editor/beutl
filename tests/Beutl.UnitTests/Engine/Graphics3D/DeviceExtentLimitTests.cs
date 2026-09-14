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
    public void Renderer3DInitialize_OnADeviceThatCannotAttachTheShadowMaps_IsRefusedOnTheInitializePath()
    {
        // The shadow maps are a fixed size and used to be allocated lazily by Render, outside the
        // try/catch Scene3DRenderNode keeps around Initialize. The refusal has to land where it is handled.
        const int belowTheShadowMap = ShadowPass.DefaultShadowMapSize - 1;
        Mock<IGraphicsContext> device = LooseDevice(attachment: belowTheShadowMap, cube: CubeBudget);
        using var renderer = new Renderer3D(device.Object);

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => renderer.Initialize(64, 64));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain(belowTheShadowMap.ToString()));
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((0, 0)),
                "a refused initialization must stay retryable");
        });
    }

    [Test]
    public void Renderer3DInitialize_AllocatesTheShadowMapsBeforeReturning()
    {
        // Positive control for the eager allocation: with room for the shadow maps, Initialize builds them.
        Mock<IGraphicsContext> device = LooseDevice();
        using var renderer = new Renderer3D(device.Object);

        renderer.Initialize(64, 64);

        device.Verify(
            c => c.CreateTextureCube(PointShadowPass.DefaultCubeFaceSize, It.IsAny<TextureFormat>()),
            Times.AtLeastOnce);
    }

    [Test]
    public void Renderer3DResize_PastWhatTheDeviceCanAttach_IsRefusedBeforeTheAllocator()
    {
        Mock<IGraphicsContext> device = MockDevice();
        using var renderer = new Renderer3D(device.Object);

        Assert.Throws<InvalidOperationException>(() => renderer.Resize(AttachmentBudget + 1, 1));

        AssertNeverAllocated(device);
    }

    private static IEnumerable<TestCaseData> ExtentAllocatingPasses()
    {
        // Every pass that allocates the extent it is handed. GizmoPass borrows its colour and depth
        // textures, and the shadow passes allocate a fixed size, so they are bounded by the context alone.
        yield return new TestCaseData(new Func<IGraphicsContext, RenderNode3D>(
            c => new GeometryPass(c, Mock.Of<IShaderCompiler>()))).SetName("{m}(GeometryPass)");
        yield return new TestCaseData(new Func<IGraphicsContext, RenderNode3D>(
            c => new LightingPass(c, Mock.Of<IShaderCompiler>(), Mock.Of<ITexture2D>()))).SetName("{m}(LightingPass)");
        yield return new TestCaseData(new Func<IGraphicsContext, RenderNode3D>(
            c => new TransparentPass(c, Mock.Of<IShaderCompiler>(), Mock.Of<ITexture2D>()))).SetName("{m}(TransparentPass)");
        yield return new TestCaseData(new Func<IGraphicsContext, RenderNode3D>(
            c => new FlipPass(c, Mock.Of<IShaderCompiler>()))).SetName("{m}(FlipPass)");
    }

    [TestCaseSource(nameof(ExtentAllocatingPasses))]
    public void PassInitialize_PastWhatTheDeviceCanAttach_IsRefusedBeforeTheAllocator(
        Func<IGraphicsContext, RenderNode3D> create)
    {
        Mock<IGraphicsContext> device = MockDevice();
        using RenderNode3D pass = create(device.Object);

        InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
            () => pass.Initialize(AttachmentBudget + 1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain(AttachmentBudget.ToString()),
                "the refusal must report the limit it could not fit");
            Assert.That((pass.Width, pass.Height), Is.EqualTo((0, 0)),
                "a refused initialization must not commit an extent");
            AssertNeverAllocated(device);
        });
    }

    [Test]
    public void PassInitialize_WithinWhatTheDeviceCanAttach_ReachesTheAllocator()
    {
        // Positive control: the guard has to let the largest extent the device can attach through, or it
        // is refusing everything rather than measuring anything.
        Mock<IGraphicsContext> device = LooseDevice();
        using var pass = new FlipPass(device.Object, Mock.Of<IShaderCompiler>());

        pass.Initialize(AttachmentBudget, AttachmentBudget);

        Assert.Multiple(() =>
        {
            device.Verify(
                c => c.CreateTexture2D(AttachmentBudget, AttachmentBudget, It.IsAny<TextureFormat>()),
                Times.AtLeastOnce);
            Assert.That((pass.Width, pass.Height), Is.EqualTo((AttachmentBudget, AttachmentBudget)));
        });
    }

    [Test]
    public void PassResize_PastWhatTheDeviceCanAttach_KeepsTheCurrentExtentAndResources()
    {
        Mock<IGraphicsContext> device = LooseDevice();
        using var pass = new FlipPass(device.Object, Mock.Of<IShaderCompiler>());
        pass.Initialize(16, 16);
        ITexture2D? before = pass.OutputTexture;

        Assert.Throws<InvalidOperationException>(() => pass.Resize(1, AttachmentBudget + 1));

        Assert.Multiple(() =>
        {
            Assert.That((pass.Width, pass.Height), Is.EqualTo((16, 16)),
                "a refused resize must leave the extent the pass still holds resources for");
            Assert.That(pass.OutputTexture, Is.SameAs(before), "a refused resize must not dispose or reallocate");
            device.Verify(
                c => c.CreateTexture2D(1, AttachmentBudget + 1, It.IsAny<TextureFormat>()),
                Times.Never);
        });
    }

    [Test]
    public void PassResize_ThatFailsAfterDisposing_DoesNotLeaveTheOldExtentToBeMistakenForANoOp()
    {
        // Past the guard, a pass disposes what it had before it allocates. If the allocation then fails,
        // the old size must not be reported as still held, or a request to return to it would be skipped
        // and disposed resources left in use.
        Mock<IGraphicsContext> device = LooseDevice();
        using var pass = new FlipPass(device.Object, Mock.Of<IShaderCompiler>());
        pass.Initialize(16, 16);
        device.Setup(c => c.CreateTexture2D(32, 32, TextureFormat.Depth32Float))
            .Throws(new InvalidOperationException("the device declined"));

        Assert.Throws<InvalidOperationException>(() => pass.Resize(32, 32));
        pass.Resize(16, 16);

        device.Verify(c => c.CreateTexture2D(16, 16, TextureFormat.RGBA8Unorm), Times.Exactly(2),
            "returning to the old size after a failed replacement must reallocate it");
    }

    [Test]
    public void AFixedSizeShadowPass_IsNotRefusedForAViewportItDoesNotAllocate()
    {
        // ShadowPass and PointShadowPass ignore the extent Initialize is handed and allocate their own
        // fixed map, so a viewport past the attachment limit is not theirs to refuse; only the map is.
        Mock<IGraphicsContext> device = LooseDevice();
        using var shadow = new ShadowPass(device.Object, Mock.Of<IShaderCompiler>());
        using var pointShadow = new PointShadowPass(device.Object, Mock.Of<IShaderCompiler>());

        shadow.Initialize(AttachmentBudget + 1, AttachmentBudget + 1);
        pointShadow.Initialize(AttachmentBudget + 1, AttachmentBudget + 1);

        Assert.Multiple(() =>
        {
            device.Verify(
                c => c.CreateTexture2D(ShadowPass.DefaultShadowMapSize, ShadowPass.DefaultShadowMapSize, It.IsAny<TextureFormat>()),
                Times.AtLeastOnce);
            device.Verify(
                c => c.CreateTextureCube(PointShadowPass.DefaultCubeFaceSize, It.IsAny<TextureFormat>()),
                Times.Once);
            device.Verify(
                c => c.CreateTexture2D(AttachmentBudget + 1, AttachmentBudget + 1, It.IsAny<TextureFormat>()),
                Times.Never);
        });
    }

    [Test]
    public void ADeviceThatReportsNoLimit_LeavesTheExtentToTheAllocator()
    {
        // Mirrors the 2D path: a limit of zero means the device did not answer, and a guess would refuse
        // an allocation the device might well make.
        Mock<IGraphicsContext> device = LooseDevice(attachment: 0);
        using var pass = new FlipPass(device.Object, Mock.Of<IShaderCompiler>());

        pass.Initialize(100_000, 1);

        device.Verify(c => c.CreateTexture2D(100_000, 1, It.IsAny<TextureFormat>()), Times.AtLeastOnce);
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
        Mock<IGraphicsContext> device = LooseDevice();
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

    [TestCase(-1, 1)]
    [TestCase(1, -1)]
    public void ANegativeExtent_IsRefusedAsACallerError_NotMeasuredAgainstTheBudget(int width, int height)
    {
        // The backend casts a dimension to uint, so a negative one would reach the driver as an enormous
        // extent and slip under an upper-bound-only check.
        Mock<IGraphicsContext> device = MockDevice();
        using var pass = new FlipPass(device.Object, Mock.Of<IShaderCompiler>());

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => pass.Initialize(width, height));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeviceExtentLimits.ThrowIfCannotMakeCubeFace(device.Object, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeviceExtentLimits.ThrowIfCannotAttachCubeFaces(device.Object, -1));
            AssertNeverAllocated(device);
        });
    }

    [Test]
    public void TheImageLimit_BoundsAGenericTexture_WithoutTheAttachmentLimit()
    {
        // A context applies the image limit to every texture it creates: a sampled-only texture may be
        // wider than a framebuffer, so the attachment limit is for the paths that know they will attach.
        const int imageLimit = 16384;
        Assert.Multiple(() =>
        {
            Assert.That(() => DeviceExtentLimits.ThrowIfCannotMakeImage(imageLimit, imageLimit, 1), Throws.Nothing);
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotMakeImage(imageLimit, imageLimit + 1, 1),
                Throws.InvalidOperationException.With.Message.Contains(imageLimit.ToString()));
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotMakeImage(imageLimit, 1, imageLimit + 1),
                Throws.InvalidOperationException);
            Assert.That(() => DeviceExtentLimits.ThrowIfCannotMakeImage(0, 100_000, 1), Throws.Nothing,
                "a device that did not answer leaves the extent to the allocator");
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotMakeImage(imageLimit, -1, 1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
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

    /// <summary>A device that answers every allocation with a mock, for the controls that must reach it.</summary>
    private static Mock<IGraphicsContext> LooseDevice(int attachment = AttachmentBudget, int cube = CubeBudget)
    {
        var device = new Mock<IGraphicsContext>(MockBehavior.Loose) { DefaultValue = DefaultValue.Mock };
        device.SetupGet(c => c.MaxAttachmentDimension).Returns(attachment);
        device.SetupGet(c => c.MaxCubeFaceDimension).Returns(cube);
        // Moq cannot proxy IBuffer.Upload<T>(ReadOnlySpan<T>), so a buffer has to be a real stub.
        device.Setup(c => c.CreateBuffer(It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns((ulong size, BufferUsage _, MemoryProperty _) => new StubBuffer(size));
        return device;
    }

    private sealed class StubBuffer(ulong size) : IBuffer
    {
        public ulong Size => size;

        public void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged
        {
        }

        public IntPtr Map() => IntPtr.Zero;

        public void Unmap()
        {
        }

        public void Dispose()
        {
        }
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
}
