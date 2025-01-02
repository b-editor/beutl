using SDL;

namespace Beutl.Graphics3D;

public unsafe class CopyPass : IDisposable
{
    private readonly CommandBuffer _commandBuffer;

    internal CopyPass(CommandBuffer commandBuffer, SDL_GPUCopyPass* handle)
    {
        _commandBuffer = commandBuffer;
        Handle = handle;
    }

    internal SDL_GPUCopyPass* Handle { get; private set; }

    public void UploadToTexture(
        in TextureTransferInfo source,
        in TextureRegion destination,
        bool cycle)
    {
        SDL_GPUTextureTransferInfo _source = source.ToNative();
        SDL_GPUTextureRegion _destination = destination.ToNative();
        SDL3.SDL_UploadToGPUTexture(
            Handle,
            &_source,
            &_destination,
            cycle);
    }

    public void UploadToTexture(
        TransferBuffer source,
        Texture destination,
        bool cycle)
    {
        UploadToTexture(
            new TextureTransferInfo
            {
                TransferBuffer = source,
                Offset = 0
            },
            new TextureRegion(destination),
            cycle);
    }

    public void UploadToBuffer(
        in TransferBufferLocation source,
        in BufferRegion destination,
        bool cycle)
    {
        SDL_GPUTransferBufferLocation _source = source.ToNative();
        SDL_GPUBufferRegion _destination = destination.ToNative();
        SDL3.SDL_UploadToGPUBuffer(
            Handle,
            &_source,
            &_destination,
            cycle);
    }

    public void UploadToBuffer(
        TransferBuffer source,
        Buffer destination,
        bool cycle)
    {
        UploadToBuffer(
            new TransferBufferLocation
            {
                TransferBuffer = source,
                Offset = 0
            },
            new BufferRegion
            {
                Buffer = destination,
                Offset = 0,
                Size = destination.Size
            },
            cycle);
    }

    public void UploadToBuffer<T>(
        TransferBuffer source,
        Buffer destination,
        uint sourceStartElement,
        uint destinationStartElement,
        uint numElements,
        bool cycle) where T : unmanaged
    {
        int elementSize = sizeof(T);
        uint dataLengthInBytes = (uint)(elementSize * numElements);
        uint srcOffsetInBytes = (uint)(elementSize * sourceStartElement);
        uint dstOffsetInBytes = (uint)(elementSize * destinationStartElement);

        UploadToBuffer(
            new TransferBufferLocation
            {
                TransferBuffer = source,
                Offset = srcOffsetInBytes
            },
            new BufferRegion
            {
                Buffer = destination,
                Offset = dstOffsetInBytes,
                Size = dataLengthInBytes
            },
            cycle);
    }

    public void CopyTextureToTexture(
        in TextureLocation source,
        in TextureLocation destination,
        uint width,
        uint height,
        uint depth,
        bool cycle)
    {
        SDL_GPUTextureLocation _source = source.ToNative();
        SDL_GPUTextureLocation _destination = destination.ToNative();
        SDL3.SDL_CopyGPUTextureToTexture(
            Handle,
            &_source,
            &_destination,
            width,
            height,
            depth,
            cycle);
    }

    public void CopyBufferToBuffer(
        in BufferLocation source,
        in BufferLocation destination,
        uint size,
        bool cycle)
    {
        SDL_GPUBufferLocation _source = source.ToNative();
        SDL_GPUBufferLocation _destination = destination.ToNative();
        SDL3.SDL_CopyGPUBufferToBuffer(
            Handle,
            &_source,
            &_destination,
            size,
            cycle);
    }

    public void DownloadFromBuffer(
        in BufferRegion source,
        in TransferBufferLocation destination)
    {
        SDL_GPUBufferRegion _source = source.ToNative();
        SDL_GPUTransferBufferLocation _destination = destination.ToNative();
        SDL3.SDL_DownloadFromGPUBuffer(
            Handle,
            &_source,
            &_destination);
    }

    public void DownloadFromTexture(
        in TextureRegion source,
        in TextureTransferInfo destination)
    {
        SDL_GPUTextureRegion _source = source.ToNative();
        SDL_GPUTextureTransferInfo _destination = destination.ToNative();
        SDL3.SDL_DownloadFromGPUTexture(
            Handle,
            &_source,
            &_destination);
    }

    public void Dispose()
    {
        SDL3.SDL_EndGPUCopyPass(Handle);
        Handle = null;
    }
}
