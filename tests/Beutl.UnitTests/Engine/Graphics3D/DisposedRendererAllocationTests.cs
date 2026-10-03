using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Nodes;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public sealed class DisposedRendererAllocationTests
{
    [TestCase(true, 32)]
    [TestCase(false, 32)]
    [TestCase(false, 16)]
    public void ADisposedPass_RefusesInitializationOrResizeBeforeAllocating(bool initialize, int size)
    {
        Mock<IGraphicsContext> context = CreateContext();
        using var pass = new FlipPass(context.Object, context.Object.CreateShaderCompiler());
        pass.Initialize(16, 16);
        pass.Dispose();
        context.Invocations.Clear();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () =>
                {
                    if (initialize)
                        pass.Initialize(size, size);
                    else
                        pass.Resize(size, size);
                },
                Throws.InstanceOf<ObjectDisposedException>());
            AssertNeverAllocated(context);
            Assert.That((pass.Width, pass.Height), Is.EqualTo((16, 16)),
                "a disposed pass must not commit an extent for resources that it cannot release again");
        }
    }

    [TestCase(true, 32)]
    [TestCase(false, 32)]
    [TestCase(false, 16)]
    public void ADisposedRenderer_RefusesInitializationOrResizeBeforeAllocating(bool initialize, int size)
    {
        Mock<IGraphicsContext> context = CreateContext();
        using var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);
        renderer.Dispose();
        context.Invocations.Clear();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                () =>
                {
                    if (initialize)
                        renderer.Initialize(size, size);
                    else
                        renderer.Resize(size, size);
                },
                Throws.InstanceOf<ObjectDisposedException>());
            AssertNeverAllocated(context);
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((16, 16)),
                "a disposed renderer must not commit an extent for resources that it cannot release again");
        }
    }

    private static Mock<IGraphicsContext> CreateContext()
    {
        var context = new Mock<IGraphicsContext> { DefaultValue = DefaultValue.Mock };
        // Shadow initialization uploads its matrices through a span, which Moq cannot proxy.
        context.Setup(c => c.CreateBuffer(It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns((ulong size, BufferUsage _, MemoryProperty _) => new StubBuffer(size));
        return context;
    }

    private static void AssertNeverAllocated(Mock<IGraphicsContext> context)
    {
        context.Verify(
            c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()),
            Times.Never,
            "new textures would be stranded because Dispose has already completed");
        context.Verify(
            c => c.CreateTextureCube(It.IsAny<int>(), It.IsAny<TextureFormat>()),
            Times.Never);
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
}
