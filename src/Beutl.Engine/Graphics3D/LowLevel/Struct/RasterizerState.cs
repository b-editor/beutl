using SDL;

namespace Beutl.Graphics3D;

public readonly struct RasterizerState
{
    public static readonly RasterizerState CW_CullFront = new()
    {
        CullMode = CullMode.Front,
        FrontFace = FrontFace.Clockwise,
        FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CW_CullBack = new()
    {
        CullMode = CullMode.Back,
        FrontFace = FrontFace.Clockwise,
        FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CW_CullNone = new()
    {
        CullMode = CullMode.None,
        FrontFace = FrontFace.Clockwise,
        FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CW_Wireframe = new()
    {
        CullMode = CullMode.None,
        FrontFace = FrontFace.Clockwise,
        FillMode = FillMode.Line
    };

    public static readonly RasterizerState CCW_CullFront = new()
    {
        CullMode = CullMode.Front,
        FrontFace = FrontFace.CounterClockwise,
        FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CCW_CullBack = new()
    {
        CullMode = CullMode.Back,
        FrontFace = FrontFace.CounterClockwise,
        FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CCW_CullNone = new()
    {
        CullMode = CullMode.None,
        FrontFace = FrontFace.CounterClockwise,
        FillMode = FillMode.Fill
    };

    public static readonly RasterizerState CCW_Wireframe = new()
    {
        CullMode = CullMode.None,
        FrontFace = FrontFace.CounterClockwise,
        FillMode = FillMode.Line
    };

    public FillMode FillMode { get; init; }

    public CullMode CullMode { get; init; }

    public FrontFace FrontFace { get; init; }

    public float DepthBiasConstantFactor { get; init; }

    public float DepthBiasClamp { get; init; }

    public float DepthBiasSlopFactor { get; init; }

    public bool EnableDepthBias { get; init; }

    public bool EnableDepthClip { get; init; }

    internal SDL_GPURasterizerState ToNative()
    {
        return new SDL_GPURasterizerState
        {
            fill_mode = (SDL_GPUFillMode)FillMode,
            cull_mode = (SDL_GPUCullMode)CullMode,
            front_face = (SDL_GPUFrontFace)FrontFace,
            depth_bias_constant_factor = DepthBiasConstantFactor,
            depth_bias_clamp = DepthBiasClamp,
            depth_bias_slope_factor = DepthBiasSlopFactor,
            enable_depth_bias = EnableDepthBias,
            enable_depth_clip = EnableDepthClip
        };
    }
}
