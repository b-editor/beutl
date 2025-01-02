using SDL;

namespace Beutl.Graphics3D;

public readonly struct ColorTargetInfo
{
    public ColorTargetInfo(Texture texture, ColorF clearColor, bool cycle = false)
    {
        Texture = texture;
        LoadOp = LoadOp.Clear;
        ClearColor = clearColor;
        StoreOp = StoreOp.Store;
        Cycle = cycle;
    }

    public ColorTargetInfo(Texture texture, LoadOp loadOp, bool cycle = false)
    {
        Texture = texture;
        LoadOp = loadOp;
        StoreOp = StoreOp.Store;
        Cycle = cycle;
    }

    
    public Texture Texture { get; init; }
    
    public uint MipLevel { get; init; }
    
    public uint LayerOrDepthPlane { get; init; }
    
    public ColorF ClearColor { get; init; }
    
    public LoadOp LoadOp { get; init; }
    
    public StoreOp StoreOp { get; init; }
    
    public Texture ResolveTexture { get; init; }
    
    public uint ResolveMipLevel { get; init; }
    
    public uint ResolveLayer { get; init; }
    
    public bool Cycle { get; init; }
    
    public bool CycleResolveTexture { get; init; }

    internal unsafe SDL_GPUColorTargetInfo ToNative()
    {
        return new SDL_GPUColorTargetInfo
        {
            texture = Texture != null ? Texture.Handle : null,
            mip_level = MipLevel,
            layer_or_depth_plane = LayerOrDepthPlane,
            clear_color = new SDL_FColor
            {
                r = ClearColor.R,
                g = ClearColor.G,
                b = ClearColor.B,
                a = ClearColor.A
            },
            load_op = (SDL_GPULoadOp)LoadOp,
            store_op = (SDL_GPUStoreOp)StoreOp,
            resolve_texture = ResolveTexture != null ? ResolveTexture.Handle : null,
            resolve_mip_level = ResolveMipLevel,
            resolve_layer = ResolveLayer,
            cycle = Cycle,
            cycle_resolve_texture = CycleResolveTexture
        };
    }
}
