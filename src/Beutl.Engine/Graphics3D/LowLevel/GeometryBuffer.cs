namespace Beutl.Graphics3D;

public class GeoemtryBuffer : GraphicsResource
{
    public GeoemtryBuffer(uint width, uint height, Device device) : base(device)
    {
        AlbedoOcclusion = Texture.Create2D(
            device, width, height,
            TextureFormat.R8G8B8A8UNorm, TextureUsageFlags.ColorTarget | TextureUsageFlags.Sampler);

        SpecularSmooth = Texture.Create2D(
            device, width, height,
            TextureFormat.R8G8B8A8UNorm, TextureUsageFlags.ColorTarget | TextureUsageFlags.Sampler);

        NormalMetalic = Texture.Create2D(
            device, width, height,
            TextureFormat.R16G16B16A16Float, TextureUsageFlags.ColorTarget | TextureUsageFlags.Sampler);

        EmissiveHeight = Texture.Create2D(
            device, width, height,
            TextureFormat.R16G16B16A16Float, TextureUsageFlags.ColorTarget | TextureUsageFlags.Sampler);

        Depth = Texture.Create2D(
            device, width, height,
            TextureFormat.D16UNorm, TextureUsageFlags.DepthStencilTarget | TextureUsageFlags.Sampler);
    }

    public Texture AlbedoOcclusion { get; }

    public Texture SpecularSmooth { get; }

    public Texture NormalMetalic { get; }

    public Texture EmissiveHeight { get; }

    public Texture Depth { get; }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        AlbedoOcclusion.Dispose();
        SpecularSmooth.Dispose();
        NormalMetalic.Dispose();
        EmissiveHeight.Dispose();
        Depth.Dispose();
    }
}