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

    /// <summary>
    /// The descriptor layout every built-in material declares: its uniform buffer at binding 0, read by both
    /// stages, then <paramref name="textureCount"/> fragment samplers at bindings 1 to n.
    /// </summary>
    /// <remarks><see cref="CreateDrawBindings{TUbo}"/> sizes its descriptor pool for exactly this layout.</remarks>
    public static DescriptorBinding[] CreateDescriptorBindings(uint textureCount)
    {
        var bindings = new DescriptorBinding[textureCount + 1];
        bindings[0] = new DescriptorBinding(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex | ShaderStage.Fragment);
        for (uint binding = 1; binding <= textureCount; binding++)
        {
            bindings[binding] = new DescriptorBinding(binding, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment);
        }

        return bindings;
    }

    public static MaterialDrawBindings CreateDrawBindings<TUbo>(
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
            return new MaterialDrawBindings(buffer, descriptors);
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
