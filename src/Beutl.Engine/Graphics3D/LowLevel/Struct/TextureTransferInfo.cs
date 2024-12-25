using SDL;

namespace Beutl.Graphics3D;

public readonly struct TextureTransferInfo
{
    public TransferBuffer TransferBuffer { get; init; }

    public uint Offset { get; init; }

    public uint PixelsPerRow { get; init; }

    public uint RowsPerLayer { get; init; }

    internal unsafe SDL_GPUTextureTransferInfo ToNative()
    {
        return new SDL_GPUTextureTransferInfo
        {
            transfer_buffer = TransferBuffer != null ? TransferBuffer.Handle : null,
            offset = Offset,
            pixels_per_row = PixelsPerRow,
            rows_per_layer = RowsPerLayer
        };
    }
}
