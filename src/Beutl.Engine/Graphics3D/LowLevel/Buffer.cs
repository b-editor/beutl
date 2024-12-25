using SDL;

namespace Beutl.Graphics3D;

public unsafe class Buffer : GraphicsResource
{
    private readonly BufferCreateInfo _createInfo;

    private Buffer(Device device, SDL_GPUBuffer* handle, BufferCreateInfo createInfo) : base(device)
    {
        Handle = handle;
        _createInfo = createInfo;
    }

    public BufferUsageFlags Usage => _createInfo.Usage;

    public uint Size => _createInfo.Size;

    internal SDL_GPUBuffer* Handle { get; }

    public static Buffer Create<T>(
        Device device,
        BufferUsageFlags usageFlags,
        uint elementCount) where T : unmanaged
    {
        return Create(device, new BufferCreateInfo
        {
            Usage = usageFlags,
            Size = (uint)sizeof(T) * elementCount
        });
    }

    public static Buffer Create(
        Device device,
        in BufferCreateInfo createInfo)
    {
        var nativeInfo = createInfo.ToNative();
        var handle = SDL3.SDL_CreateGPUBuffer(device.Handle, &nativeInfo);
        if (handle == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new Buffer(device, handle, createInfo);
    }

    public static implicit operator BufferBinding(Buffer buffer) => new(buffer);

    protected override void Dispose(bool disposing)
    {
        SDL3.SDL_ReleaseGPUBuffer(Device.Handle, Handle);
    }
}
