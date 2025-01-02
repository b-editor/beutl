using SDL;

namespace Beutl.Graphics3D;

public readonly struct StencilOpState
{
    public StencilOp FailOp { get; init; }

    public StencilOp PassOp { get; init; }

    public StencilOp DepthFailOp { get; init; }

    public CompareOp CompareOp { get; init; }

    internal SDL_GPUStencilOpState ToNative()
    {
        return new SDL_GPUStencilOpState
        {
            fail_op = (SDL_GPUStencilOp)FailOp,
            pass_op = (SDL_GPUStencilOp)PassOp,
            depth_fail_op = (SDL_GPUStencilOp)DepthFailOp,
            compare_op = (SDL_GPUCompareOp)CompareOp
        };
    }
}
