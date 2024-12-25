using SDL;

namespace Beutl.Graphics3D;

public unsafe class Sampler : GraphicsResource
{
    private readonly SamplerCreateInfo _createInfo;

    private Sampler(Device device, SDL_GPUSampler* handle, SamplerCreateInfo createInfo) : base(device)
    {
        Handle = handle;
        _createInfo = createInfo;
    }

    public Filter MinFilter => _createInfo.MinFilter;

    public Filter MagFilter => _createInfo.MagFilter;

    public SamplerMipmapMode MipmapMode => _createInfo.MipmapMode;

    public SamplerAddressMode AddressModeU => _createInfo.AddressModeU;

    public SamplerAddressMode AddressModeV => _createInfo.AddressModeV;

    public SamplerAddressMode AddressModeW => _createInfo.AddressModeW;

    public float MipLodBias => _createInfo.MipLodBias;

    public float MaxAnisotropy => _createInfo.MaxAnisotropy;

    public CompareOp CompareOp => _createInfo.CompareOp;

    public float MinLod => _createInfo.MinLod;

    public float MaxLod => _createInfo.MaxLod;

    public bool EnableAnisotropy => _createInfo.EnableAnisotropy;

    public bool EnableCompare => _createInfo.EnableCompare;

    internal SDL_GPUSampler* Handle { get; private set; }

    public static Sampler Create(
        Device device,
        in SamplerCreateInfo samplerCreateInfo)
    {
        var nativeInfo = samplerCreateInfo.ToNative();
        var handle = SDL3.SDL_CreateGPUSampler(device.Handle, &nativeInfo);
        if (handle == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new Sampler(device, handle, samplerCreateInfo);
    }

    protected override void Dispose(bool disposing)
    {
        SDL3.SDL_ReleaseGPUSampler(Device.Handle, Handle);
        Handle = null;
    }
}
