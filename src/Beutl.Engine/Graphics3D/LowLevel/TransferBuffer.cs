using SDL;

namespace Beutl.Graphics3D;

public unsafe class TransferBuffer : GraphicsResource
{
    private readonly TransferBufferCreateInfo _createInfo;

    private TransferBuffer(Device device, SDL_GPUTransferBuffer* handle, TransferBufferCreateInfo createInfo)
        : base(device)
    {
        Handle = handle;
        _createInfo = createInfo;
    }

    public uint Size => _createInfo.Size;

    public TransferBufferUsage Usage => _createInfo.Usage;

    internal SDL_GPUTransferBuffer* Handle { get; private set; }

    public static TransferBuffer Create(Device device, in TransferBufferCreateInfo createInfo)
    {
        var nativeInfo = createInfo.ToNative();
        var handle = SDL3.SDL_CreateGPUTransferBuffer(device.Handle, &nativeInfo);
        if (handle == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new TransferBuffer(device, handle, createInfo);
    }

    public static TransferBuffer Create<T>(
        Device device,
        TransferBufferUsage usage,
        uint elementCount) where T : unmanaged
    {
        return Create(device, new TransferBufferCreateInfo
        {
            Usage = usage,
            Size = (uint) (sizeof(T) * elementCount),
        });
    }

    public MappedBuffer Map(bool cycle = false)
    {
        IntPtr ptr = SDL3.SDL_MapGPUTransferBuffer(Device.Handle, Handle, cycle);
        return new MappedBuffer(ptr, this);
    }

    protected override void Dispose(bool disposing)
    {
        SDL3.SDL_ReleaseGPUTransferBuffer(Device.Handle, Handle);
        Handle = null;
    }
}
