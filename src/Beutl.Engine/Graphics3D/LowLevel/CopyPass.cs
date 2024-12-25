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

    public void DownloadFromTexture(TextureRegion source, TextureTransferInfo destination)
    {
        var _source = source.ToNative();
        var _destination = destination.ToNative();
        SDL3.SDL_DownloadFromGPUTexture(Handle, &_source, &_destination);
    }

    public void UploadToBuffer(TransferBufferLocation source, BufferRegion destination, bool cycle)
    {
        var _source = source.ToNative();
        var _destination = destination.ToNative();
        SDL3.SDL_UploadToGPUBuffer(Handle, &_source, &_destination, cycle);
    }

    public void Dispose()
    {
        SDL3.SDL_EndGPUCopyPass(Handle);
        Handle = null;
    }
}
