using SDL;

namespace Beutl.Graphics3D;

public readonly struct DepthStencilTargetInfo
{
    public DepthStencilTargetInfo(Texture texture, float clearDepth, bool cycle = false)
    {
        Texture = texture;
        LoadOp = LoadOp.Clear;
        ClearDepth = clearDepth;
        StencilLoadOp = LoadOp.DontCare;
        StoreOp = StoreOp.DontCare;
        StencilStoreOp = StoreOp.DontCare;
        Cycle = cycle;
    }

    public DepthStencilTargetInfo(Texture texture, float clearDepth, byte clearStencil, bool cycle = false)
    {
        Texture = texture;
        LoadOp = LoadOp.Clear;
        StencilLoadOp = LoadOp.Clear;
        ClearDepth = clearDepth;
        ClearStencil = clearStencil;
        StoreOp = StoreOp.DontCare;
        StencilStoreOp = StoreOp.DontCare;
        Cycle = cycle;
    }

    public Texture Texture { get; init; }

    public float ClearDepth { get; init; }

    public LoadOp LoadOp { get; init; }

    public StoreOp StoreOp { get; init; }

    public LoadOp StencilLoadOp { get; init; }

    public StoreOp StencilStoreOp { get; init; }

    public bool Cycle { get; init; }

    public byte ClearStencil { get; init; }

    public byte Padding1 { get; init; }

    public byte Padding2 { get; init; }

    internal unsafe SDL_GPUDepthStencilTargetInfo ToNative()
    {
        return new SDL_GPUDepthStencilTargetInfo
        {
            texture = Texture != null ? Texture.Handle : null,
            clear_depth = ClearDepth,
            load_op = (SDL_GPULoadOp)LoadOp,
            store_op = (SDL_GPUStoreOp)StoreOp,
            stencil_load_op = (SDL_GPULoadOp)StencilLoadOp,
            stencil_store_op = (SDL_GPUStoreOp)StencilStoreOp,
            cycle = Cycle,
            clear_stencil = ClearStencil,
            padding1 = Padding1,
            padding2 = Padding2
        };
    }
}
