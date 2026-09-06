using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Materials;

internal static class MaterialGpuResources
{
    public static IBuffer CreateUniformBuffer<TUbo>(IGraphicsContext graphicsContext)
        where TUbo : struct
    {
        return graphicsContext.CreateBuffer(
            (ulong)Marshal.SizeOf<TUbo>(),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
    }

    public static ISampler CreateLinearRepeatSampler(IGraphicsContext graphicsContext)
    {
        return graphicsContext.CreateSampler(
            SamplerFilter.Linear,
            SamplerFilter.Linear,
            SamplerAddressMode.Repeat,
            SamplerAddressMode.Repeat);
    }

    public static ITexture2D Create1x1Texture(IGraphicsContext graphicsContext, ReadOnlySpan<byte> bgra)
    {
        var texture = graphicsContext.CreateTexture2D(1, 1, TextureFormat.BGRA8Unorm);
        texture.Upload(bgra);
        return texture;
    }
}
