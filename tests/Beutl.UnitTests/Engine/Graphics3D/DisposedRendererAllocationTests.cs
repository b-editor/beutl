using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Nodes;
using Moq;
using static Beutl.UnitTests.Engine.Graphics3D.Graphics3DTestDevices;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public sealed class DisposedRendererAllocationTests
{
    [TestCase(true, 32)]
    [TestCase(false, 32)]
    [TestCase(false, 16)]
    public void ADisposedPass_RefusesInitializationOrResizeBeforeAllocating(bool initialize, int size)
    {
        Mock<IGraphicsContext> context = LooseDevice();
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
        Mock<IGraphicsContext> context = LooseDevice();
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
}
