using Beutl.Graphics.Backend;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

// The loose graphics devices the Renderer3D tests allocate against.
internal static class Graphics3DTestDevices
{
    /// <summary>A device that answers every call with a mock and every buffer with a <see cref="StubBuffer"/>.</summary>
    public static Mock<IGraphicsContext> LooseDevice()
    {
        var device = new Mock<IGraphicsContext>(MockBehavior.Loose) { DefaultValue = DefaultValue.Mock };
        // Moq cannot proxy IBuffer.Upload<T>(ReadOnlySpan<T>), which shadow initialization calls, so a buffer
        // has to be a real stub.
        device.Setup(c => c.CreateBuffer(It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns((ulong size, BufferUsage _, MemoryProperty _) => new StubBuffer(size));
        return device;
    }

    /// <summary>A <see cref="LooseDevice()"/> that reports the given attachment and cube-face limits.</summary>
    public static Mock<IGraphicsContext> LooseDevice(int attachment, int cube)
    {
        Mock<IGraphicsContext> device = LooseDevice();
        device.SetupGet(c => c.MaxAttachmentDimension).Returns(attachment);
        device.SetupGet(c => c.MaxCubeFaceDimension).Returns(cube);
        return device;
    }
}

internal sealed class StubBuffer(ulong size) : IBuffer
{
    public ulong Size => size;

    public void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
    }

    public void Dispose()
    {
    }
}
