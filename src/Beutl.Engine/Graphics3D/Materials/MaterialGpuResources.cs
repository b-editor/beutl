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

    public static (IBuffer Buffer, IDescriptorSet Descriptors) CreateDrawBindings<TUbo>(
        IGraphicsContext context, IPipeline3D pipeline, uint textureCount)
        where TUbo : struct
    {
        IBuffer buffer = CreateUniformBuffer<TUbo>(context);
        IDescriptorSet? descriptors = null;
        try
        {
            descriptors = context.CreateDescriptorSet(pipeline,
            [
                new(DescriptorType.UniformBuffer, 1),
                new(DescriptorType.CombinedImageSampler, textureCount),
            ]);
            descriptors.UpdateBuffer(0, buffer);
            return (buffer, descriptors);
        }
        catch
        {
            descriptors?.Dispose();
            buffer.Dispose();
            throw;
        }
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
