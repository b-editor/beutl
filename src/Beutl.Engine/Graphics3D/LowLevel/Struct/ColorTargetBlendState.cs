using SDL;

namespace Beutl.Graphics3D;

public readonly struct ColorTargetBlendState
{
    public static readonly ColorTargetBlendState NoWrite = new()
    {
        EnableColorWriteMask = true,
        ColorWriteMask = ColorComponentFlags.None
    };

    public static readonly ColorTargetBlendState NoBlend = new()
    {
        EnableColorWriteMask = true,
        ColorWriteMask = ColorComponentFlags.RGBA
    };

    public static readonly ColorTargetBlendState Opaque = new()
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.One,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstColorBlendFactor = BlendFactor.Zero,
        DstAlphaBlendFactor = BlendFactor.Zero
    };

    public static readonly ColorTargetBlendState Additive = new()
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.SrcAlpha,
        SrcAlphaBlendFactor = BlendFactor.SrcAlpha,
        DstColorBlendFactor = BlendFactor.One,
        DstAlphaBlendFactor = BlendFactor.One
    };

    public static readonly ColorTargetBlendState PremultipliedAlphaBlend = new()
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.One,
        SrcAlphaBlendFactor = BlendFactor.One,
        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha
    };

    public static readonly ColorTargetBlendState NonPremultipliedAlphaBlend = new()
    {
        EnableBlend = true,
        AlphaBlendOp = BlendOp.Add,
        ColorBlendOp = BlendOp.Add,
        SrcColorBlendFactor = BlendFactor.SrcAlpha,
        SrcAlphaBlendFactor = BlendFactor.SrcAlpha,
        DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
        DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha
    };

    public BlendFactor SrcColorBlendFactor { get; init; }

    public BlendFactor DstColorBlendFactor { get; init; }

    public BlendOp ColorBlendOp { get; init; }

    public BlendFactor SrcAlphaBlendFactor { get; init; }

    public BlendFactor DstAlphaBlendFactor { get; init; }

    public BlendOp AlphaBlendOp { get; init; }

    public ColorComponentFlags ColorWriteMask { get; init; }

    public bool EnableBlend { get; init; }

    public bool EnableColorWriteMask { get; init; }

    internal SDL_GPUColorTargetBlendState ToNative()
    {
        return new SDL_GPUColorTargetBlendState
        {
            src_color_blendfactor = (SDL_GPUBlendFactor)SrcColorBlendFactor,
            dst_color_blendfactor = (SDL_GPUBlendFactor)DstColorBlendFactor,
            color_blend_op = (SDL_GPUBlendOp)ColorBlendOp,
            src_alpha_blendfactor = (SDL_GPUBlendFactor)SrcAlphaBlendFactor,
            dst_alpha_blendfactor = (SDL_GPUBlendFactor)DstAlphaBlendFactor,
            alpha_blend_op = (SDL_GPUBlendOp)AlphaBlendOp,
            color_write_mask = (SDL_GPUColorComponentFlags)ColorWriteMask,
            enable_blend = EnableBlend,
            enable_color_write_mask = EnableColorWriteMask
        };
    }
}
