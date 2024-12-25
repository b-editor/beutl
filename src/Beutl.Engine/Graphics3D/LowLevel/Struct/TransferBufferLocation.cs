using SDL;

namespace Beutl.Graphics3D;

public readonly record struct TransferBufferLocation(TransferBuffer TransferBuffer, uint Offset = 0)
{
    internal unsafe SDL_GPUTransferBufferLocation ToNative()
    {
        return new SDL_GPUTransferBufferLocation
        {
            transfer_buffer = TransferBuffer != null ? TransferBuffer.Handle : null,
            offset = Offset
        };
    }

    public static implicit operator TransferBufferLocation(TransferBuffer transferBuffer) => new(transferBuffer);
}
