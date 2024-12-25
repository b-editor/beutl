using SDL;

namespace Beutl.Graphics3D;

public unsafe class Texture : GraphicsResource
{
    private readonly TextureCreateInfo _createInfo;

    private Texture(Device device, SDL_GPUTexture* handle, TextureCreateInfo createInfo) : base(device)
    {
        _createInfo = createInfo;
        Handle = handle;
        Size = SDL3.SDL_CalculateGPUTextureFormatSize(
            (SDL_GPUTextureFormat)Format, Width, Height, LayerCountOrDepth);
    }

    public uint Width => _createInfo.Width;

    public uint Height => _createInfo.Height;

    public uint LayerCountOrDepth => _createInfo.LayerCountOrDepth;

    public TextureType Type => _createInfo.Type;

    public TextureFormat Format => _createInfo.Format;

    public TextureUsageFlags Usage => _createInfo.Usage;

    public uint LevelCount => _createInfo.LevelCount;

    public SampleCount SampleCount => _createInfo.SampleCount;

    public uint Size { get; }

    internal SDL_GPUTexture* Handle { get;private set; }

    public static Texture Create(Device device, in TextureCreateInfo createInfo)
    {
        var nativeInfo = createInfo.ToNative();
        var handle = SDL3.SDL_CreateGPUTexture(device.Handle, &nativeInfo);
        if (handle == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new Texture(device, handle, createInfo);
    }

    public static Texture Create2D(
        Device device,
        uint width,
        uint height,
        TextureFormat format,
        TextureUsageFlags usageFlags,
        uint levelCount = 1,
        SampleCount sampleCount = SampleCount.One)
    {
        var textureCreateInfo = new TextureCreateInfo
        {
            Type = TextureType.TwoDimensional,
            Format = format,
            Usage = usageFlags,
            Width = width,
            Height = height,
            LayerCountOrDepth = 1,
            LevelCount = levelCount,
            SampleCount = sampleCount
        };

        return Create(device, in textureCreateInfo);
    }

    public static Texture Create2DArray(
        Device device,
        uint width,
        uint height,
        uint layerCount,
        TextureFormat format,
        TextureUsageFlags usageFlags,
        uint levelCount = 1)
    {
        var textureCreateInfo = new TextureCreateInfo
        {
            Type = TextureType.TwoDimensionalArray,
            Format = format,
            Usage = usageFlags,
            Width = width,
            Height = height,
            LayerCountOrDepth = layerCount,
            LevelCount = levelCount,
            SampleCount = SampleCount.One,
        };

        return Create(device, in textureCreateInfo);
    }

    public static Texture Create3D(
        Device device,
        uint width,
        uint height,
        uint depth,
        TextureFormat format,
        TextureUsageFlags usageFlags,
        uint levelCount = 1)
    {
        var textureCreateInfo = new TextureCreateInfo
        {
            Type = TextureType.ThreeDimensional,
            Format = format,
            Usage = usageFlags,
            Width = width,
            Height = height,
            LayerCountOrDepth = depth,
            LevelCount = levelCount,
            SampleCount = SampleCount.One,
        };

        return Create(device, in textureCreateInfo);
    }

    public static Texture CreateCube(
        Device device,
        uint size,
        TextureFormat format,
        TextureUsageFlags usageFlags,
        uint levelCount = 1)
    {
        var textureCreateInfo = new TextureCreateInfo
        {
            Type = TextureType.Cube,
            Format = format,
            Usage = usageFlags,
            Width = size,
            Height = size,
            LayerCountOrDepth = 6,
            LevelCount = levelCount,
            SampleCount = SampleCount.One
        };

        return Create(device, textureCreateInfo);
    }

    public static Texture CreateCubeArray(
        Device device,
        uint size,
        TextureFormat format,
        TextureUsageFlags usageFlags,
        uint arrayCount,
        uint levelCount = 1)
    {
        var textureCreateInfo = new TextureCreateInfo
        {
            Type = TextureType.CubeArray,
            Format = format,
            Usage = usageFlags,
            Width = size,
            Height = size,
            LayerCountOrDepth = arrayCount * 6,
            LevelCount = levelCount,
            SampleCount = SampleCount.One,
        };

        return Create(device, in textureCreateInfo);
    }

    public TextureRegion GetRegion(uint x, uint y, uint width, uint height)
    {
        return new TextureRegion
        {
            Texture = this,
            MipLevel = 0,
            Layer = 0,
            X = x,
            Y = y,
            Z = 0,
            Width = width,
            Height = height,
            Depth = 1
        };
    }

    protected override void Dispose(bool disposing)
    {
        SDL3.SDL_ReleaseGPUTexture(Device.Handle, Handle);
        Handle = null;
    }
}
