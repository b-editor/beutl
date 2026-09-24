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

    // A recording cannot reach a device, so it asks Renderer3D.CanInitialize over the limits the request
    // carries. That answer is only worth acting on if it is the same answer Initialize gives, so this runs
    // both over one mock device: once for the extent the caller sizes, and once for each fixed shadow extent
    // that refuses every scene on a small device, however small the scene is.
    [TestCase(AttachmentBudget, CubeBudget, 64, 64, true,
        TestName = "ThePreflight_AcceptsWhatInitializeAccepts")]
    [TestCase(AttachmentBudget, CubeBudget, AttachmentBudget + 1, 64, false,
        TestName = "ThePreflight_RefusesAnOutputTextureTheDeviceCannotAttach")]
    [TestCase(ShadowPass.DefaultShadowMapSize - 1, CubeBudget, 64, 64, false,
        TestName = "ThePreflight_RefusesASmallSceneWhoseShadowMapsTheDeviceCannotAttach")]
    [TestCase(AttachmentBudget, PointShadowPass.DefaultCubeFaceSize - 1, 64, 64, false,
        TestName = "ThePreflight_RefusesASmallSceneWhoseShadowCubeTheDeviceCannotBuild")]
    [TestCase(0, 0, 64, 64, true,
        TestName = "ThePreflight_RefusesNothingWhenTheDeviceReportedNoLimits")]
    public void TheRecordTimePreflight_AgreesWithInitialize(
        int attachment, int cube, int width, int height, bool expected)
    {
        Mock<IGraphicsContext> device = LooseDevice(attachment: attachment, cube: cube);
        using var renderer = new Renderer3D(device.Object);

        bool preflight = Renderer3D.CanInitialize(new Device3DExtentBudget(attachment, cube), width, height);
        bool initializeAccepts = true;
        try
        {
            renderer.Initialize(width, height);
        }
        catch (InvalidOperationException)
        {
            initializeAccepts = false;
        }

        Assert.Multiple(() =>
        {
            Assert.That(preflight, Is.EqualTo(expected));
            Assert.That(preflight, Is.EqualTo(initializeAccepts),
                "a recording must refuse exactly the extents the allocation refuses");
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

        device.Verify(c => c.CreateTexture2D(16, 16, TextureFormat.RGBA16Float), Times.Exactly(2),
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
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    public void ANonPositiveExtent_IsRefusedAsACallerError_BeforeAnyNodeIsAsked(int width, int height)
    {
        // The backend casts a dimension to uint, so a negative one would reach the driver as an enormous
        // extent and slip under an upper-bound-only check; a zero one is an image the driver may not build.
        // Refused at the node's entry, so a fixed-size node that ignores the extent cannot commit it either.
        Mock<IGraphicsContext> device = MockDevice();
        using var allocating = new FlipPass(device.Object, Mock.Of<IShaderCompiler>());
        using var fixedSize = new ShadowPass(device.Object, Mock.Of<IShaderCompiler>());
        using var renderer = new Renderer3D(device.Object);

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => allocating.Initialize(width, height));
            Assert.Throws<ArgumentOutOfRangeException>(() => allocating.Resize(width, height));
            Assert.Throws<ArgumentOutOfRangeException>(() => fixedSize.Initialize(width, height));
            Assert.Throws<ArgumentOutOfRangeException>(() => fixedSize.Resize(width, height));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Initialize(width, height));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Resize(width, height));
            Assert.That((fixedSize.Width, fixedSize.Height), Is.EqualTo((0, 0)),
                "a fixed-size node must not commit an extent it was refused");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeviceExtentLimits.ThrowIfCannotMakeCubeFace(device.Object, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => DeviceExtentLimits.ThrowIfCannotAttachCubeFaces(device.Object, 0));
            AssertNeverAllocated(device);
        });
    }

    [Test]
    public void AResizeToZero_CannotBeMistakenForAReleasedExtent()
    {
        // BeginReplacingResources reports (0, 0) while a pass replaces its resources; a request for
        // (0, 0) must be refused rather than matched against that state and skipped.
        Mock<IGraphicsContext> device = LooseDevice();
        using var pass = new FlipPass(device.Object, Mock.Of<IShaderCompiler>());
        pass.Initialize(16, 16);
        device.Setup(c => c.CreateTexture2D(32, 32, TextureFormat.Depth32Float))
            .Throws(new InvalidOperationException("the device declined"));
        Assert.Throws<InvalidOperationException>(() => pass.Resize(32, 32));

        Assert.Throws<ArgumentOutOfRangeException>(() => pass.Resize(0, 0));
    }

    [Test]
    public void AnAttachableImage_IsBoundedByTheImageLimitAndEachFramebufferAxis()
    {
        // Every texture a context creates carries attachment usage, so Vulkan holds it to the framebuffer
        // limits at creation even when it is only ever sampled.
        const int image = 16384;
        const int wide = 16384;
        const int tall = 8192;
        Assert.Multiple(() =>
        {
            Assert.That(() => DeviceExtentLimits.ThrowIfCannotMakeAttachableImage(image, wide, tall, wide, tall), Throws.Nothing);
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotMakeAttachableImage(image, wide, tall, 1, tall + 1),
                Throws.InvalidOperationException.With.Message.Contains(tall.ToString()),
                "a sampled-only texture is still held to the framebuffer limit it was created attachable under");
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotMakeAttachableImage(image, wide, tall, tall + 1, 1),
                Throws.Nothing,
                "wider than the height limit is within the width limit");
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotMakeAttachableImage(image, 0, 0, image + 1, 1),
                Throws.InvalidOperationException.With.Message.Contains(image.ToString()));
            Assert.That(() => DeviceExtentLimits.ThrowIfCannotMakeAttachableImage(0, 0, 0, 100_000, 1), Throws.Nothing,
                "a device that did not answer leaves the extent to the allocator");
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotMakeAttachableImage(image, wide, tall, -1, 1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void AFramebuffer_IsMeasuredAgainstEachAxisLimit_NotTheSquareBudget()
    {
        // A device may allow a wider framebuffer than a tall one; the square budget would refuse the wide
        // one it can build.
        const int wide = 16384;
        const int tall = 8192;
        Assert.Multiple(() =>
        {
            Assert.That(() => DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(wide, tall, wide, tall), Throws.Nothing);
            Assert.That(() => DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(wide, tall, tall + 1, 1), Throws.Nothing,
                "wider than the height limit is still within the width limit");
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(wide, tall, 1, tall + 1),
                Throws.InvalidOperationException.With.Message.Contains(tall.ToString()));
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(wide, tall, wide + 1, 1),
                Throws.InvalidOperationException.With.Message.Contains(wide.ToString()));
            Assert.That(() => DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(0, 0, 100_000, 100_000), Throws.Nothing,
                "a device that did not answer leaves the extent to the allocator");
            Assert.That(
                () => DeviceExtentLimits.ThrowIfCannotBuildFramebuffer(wide, tall, -1, 1),
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
