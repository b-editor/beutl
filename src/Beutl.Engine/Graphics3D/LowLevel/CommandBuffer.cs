using SDL;

namespace Beutl.Graphics3D;

public unsafe class CommandBuffer : IDisposable
{
    internal CommandBuffer(SDL_GPUCommandBuffer* handle)
    {
        Handle = handle;
    }

    internal SDL_GPUCommandBuffer* Handle { get; private set; }

    public RenderPass BeginRenderPass(
        ColorTargetInfo[] colorTargetInfos,
        DepthStencilTargetInfo? depthStencilTargetInfo)
    {
        var _colorTargetInfos = stackalloc SDL_GPUColorTargetInfo[colorTargetInfos.Length];
        for (var i = 0; i < colorTargetInfos.Length; i++)
        {
            _colorTargetInfos[i] = colorTargetInfos[i].ToNative();
        }
        var _depthStencilTargetInfo = depthStencilTargetInfo?.ToNative() ?? default;
        var renderPass = SDL3.SDL_BeginGPURenderPass(
            Handle,
            _colorTargetInfos,
            (uint)colorTargetInfos.Length,
            depthStencilTargetInfo != null ? &_depthStencilTargetInfo : null);
        if (renderPass == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new RenderPass(this, renderPass, colorTargetInfos, depthStencilTargetInfo);
    }

    public CopyPass BeginCopyPass()
    {
        var copyPass = SDL3.SDL_BeginGPUCopyPass(Handle);
        if (copyPass == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new CopyPass(this, copyPass);
    }

    public void Submit()
    {
        SDL3.SDL_SubmitGPUCommandBuffer(Handle);
    }

    public void Dispose()
    {
        Handle = null;
    }
}
