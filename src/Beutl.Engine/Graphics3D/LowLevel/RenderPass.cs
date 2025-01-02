using Beutl.Media;
using SDL;

namespace Beutl.Graphics3D;

public unsafe class RenderPass : IDisposable
{
    private readonly CommandBuffer _commandBuffer;
    private readonly ColorTargetInfo[] _colorTargetInfos;
    private readonly DepthStencilTargetInfo? _depthStencilTargetInfo;

    internal RenderPass(
        CommandBuffer commandBuffer, SDL_GPURenderPass* renderPass, ColorTargetInfo[] colorTargetInfos,
        DepthStencilTargetInfo? depthStencilTargetInfo)
    {
        _commandBuffer = commandBuffer;
        Handle = renderPass;
        _colorTargetInfos = colorTargetInfos;
        _depthStencilTargetInfo = depthStencilTargetInfo;
    }

    internal SDL_GPURenderPass* Handle { get; private set; }

    public void BindGraphicsPipeline(GraphicsPipeline graphicsPipeline)
    {
        SDL3.SDL_BindGPUGraphicsPipeline(Handle, graphicsPipeline.Handle);
    }

    public void SetViewport(in Viewport viewport)
    {
        var n = viewport.ToNative();
        SDL3.SDL_SetGPUViewport(Handle, &n);
    }

    public void SetScissor(in PixelRect scissor)
    {
        var rect = new SDL_Rect { x = scissor.X, y = scissor.Y, w = scissor.Width, h = scissor.Height };
        SDL3.SDL_SetGPUScissor(Handle, &rect);
    }

    public void SetStencilReference(byte stencilRef)
    {
        SDL3.SDL_SetGPUStencilReference(Handle, stencilRef);
    }

    public void SetBlendConstants(Color blendConstants)
    {
        SDL3.SDL_SetGPUBlendConstants(Handle,
            new SDL_FColor
            {
                r = blendConstants.R / 255f,
                g = blendConstants.G / 255f,
                b = blendConstants.B / 255f,
                a = blendConstants.A / 255f
            });
    }

    public void SetBlendConstants(ColorF blendConstants)
    {
        SDL3.SDL_SetGPUBlendConstants(Handle,
            new SDL_FColor { r = blendConstants.R, g = blendConstants.G, b = blendConstants.B, a = blendConstants.A });
    }

    public void BindVertexBuffers(uint slot, params ReadOnlySpan<BufferBinding> bufferBindings)
    {
        SDL_GPUBufferBinding* handlePtr = stackalloc SDL_GPUBufferBinding[bufferBindings.Length];
        for (int i = 0; i < bufferBindings.Length; i++)
        {
            ref readonly BufferBinding item = ref bufferBindings[i];
            handlePtr[i] = item.ToNative();
        }

        SDL3.SDL_BindGPUVertexBuffers(
            Handle, slot,
            handlePtr, (uint)bufferBindings.Length);
    }

    public void BindVertexBuffers(uint slot, params ReadOnlySpan<Buffer> buffers)
    {
        SDL_GPUBufferBinding* handlePtr = stackalloc SDL_GPUBufferBinding[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            handlePtr[i].buffer = buffers[i].Handle;
            handlePtr[i].offset = 0;
        }

        SDL3.SDL_BindGPUVertexBuffers(
            Handle, slot,
            handlePtr, (uint)buffers.Length);
    }

    public void BindVertexBuffers(params ReadOnlySpan<BufferBinding> bufferBindings)
    {
        BindVertexBuffers(0, bufferBindings);
    }

    public void BindVertexBuffers(params ReadOnlySpan<Buffer> buffers)
    {
        BindVertexBuffers(0, buffers);
    }

    public void BindIndexBuffer(BufferBinding bufferBinding, IndexElementSize indexElementSize)
    {
        var n = bufferBinding.ToNative();
        SDL3.SDL_BindGPUIndexBuffer(Handle, &n, (SDL_GPUIndexElementSize)indexElementSize);
    }

    public void BindVertexSamplers(
        uint slot,
        params ReadOnlySpan<TextureSamplerBinding> textureSamplerBindings)
    {
        SDL_GPUTextureSamplerBinding*
            handlePtr = stackalloc SDL_GPUTextureSamplerBinding[textureSamplerBindings.Length];
        for (int i = 0; i < textureSamplerBindings.Length; i++)
        {
            ref readonly TextureSamplerBinding item = ref textureSamplerBindings[i];
            handlePtr[i] = item.ToNative();
        }

        SDL3.SDL_BindGPUVertexSamplers(
            Handle, slot,
            handlePtr, (uint)textureSamplerBindings.Length);
    }

    public void BindVertexSamplers(params Span<TextureSamplerBinding> textureSamplerBindings)
    {
        BindVertexSamplers(0, textureSamplerBindings);
    }

    public void BindVertexStorageTextures(uint slot, params ReadOnlySpan<Texture> textures)
    {
        SDL_GPUTexture** handlePtr = stackalloc SDL_GPUTexture*[textures.Length];
        for (int i = 0; i < textures.Length; i++)
        {
            handlePtr[i] = textures[i].Handle;
        }

        SDL3.SDL_BindGPUVertexStorageTextures(
            Handle, slot,
            handlePtr, (uint)textures.Length);
    }

    public void BindVertexStorageTextures(params Span<Texture> textures)
    {
        BindVertexStorageTextures(0, textures);
    }

    public void BindVertexStorageBuffers(uint slot, params ReadOnlySpan<Buffer> buffers)
    {
        SDL_GPUBuffer** handlePtr = stackalloc SDL_GPUBuffer*[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            handlePtr[i] = buffers[i].Handle;
        }

        SDL3.SDL_BindGPUVertexStorageBuffers(
            Handle, slot,
            handlePtr, (uint)buffers.Length);
    }

    public void BindVertexStorageBuffers(params ReadOnlySpan<Buffer> buffers)
    {
        BindVertexStorageBuffers(0, buffers);
    }

    public void BindFragmentSamplers(uint slot, params ReadOnlySpan<TextureSamplerBinding> textureSamplerBindings)
    {
        SDL_GPUTextureSamplerBinding*
            handlePtr = stackalloc SDL_GPUTextureSamplerBinding[textureSamplerBindings.Length];
        for (int i = 0; i < textureSamplerBindings.Length; i++)
        {
            ref readonly TextureSamplerBinding item = ref textureSamplerBindings[i];
            handlePtr[i] = item.ToNative();
        }

        SDL3.SDL_BindGPUFragmentSamplers(
            Handle, slot,
            handlePtr, (uint)textureSamplerBindings.Length);
    }

    public void BindFragmentSamplers(params ReadOnlySpan<TextureSamplerBinding> textureSamplerBindings)
    {
        BindFragmentSamplers(0, textureSamplerBindings);
    }

    public void BindFragmentStorageTextures(uint slot, params ReadOnlySpan<Texture> textures)
    {
        SDL_GPUTexture** handlePtr = stackalloc SDL_GPUTexture*[textures.Length];
        for (int i = 0; i < textures.Length; i++)
        {
            handlePtr[i] = textures[i].Handle;
        }

        SDL3.SDL_BindGPUFragmentStorageTextures(
            Handle, slot,
            handlePtr, (uint)textures.Length);
    }

    public void BindFragmentStorageTextures(params ReadOnlySpan<Texture> textures)
    {
        BindFragmentStorageTextures(0, textures);
    }

    public void BindFragmentStorageBuffers(uint slot, params ReadOnlySpan<Buffer> buffers)
    {
        SDL_GPUBuffer** handlePtr = stackalloc SDL_GPUBuffer*[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            handlePtr[i] = buffers[i].Handle;
        }

        SDL3.SDL_BindGPUFragmentStorageBuffers(
            Handle, slot,
            handlePtr, (uint)buffers.Length);
    }

    public void BindFragmentStorageBuffers(params ReadOnlySpan<Buffer> buffers)
    {
        BindFragmentStorageBuffers(0, buffers);
    }

    public void DrawIndexedPrimitives(
        uint indexCount, uint instanceCount,
        uint firstIndex, int vertexOffset, uint firstInstance)
    {
        SDL3.SDL_DrawGPUIndexedPrimitives(
            Handle,
            indexCount, instanceCount,
            firstIndex, vertexOffset, firstInstance);
    }

    public void DrawPrimitives(
        uint vertexCount, uint instanceCount,
        uint firstVertex, uint firstInstance)
    {
        SDL3.SDL_DrawGPUPrimitives(
            Handle,
            vertexCount, instanceCount,
            firstVertex, firstInstance);
    }

    public void DrawPrimitivesIndirect(Buffer buffer, uint offsetInBytes, uint drawCount)
    {
        SDL3.SDL_DrawGPUPrimitivesIndirect(Handle, buffer.Handle, offsetInBytes, drawCount);
    }

    public void DrawIndexedPrimitivesIndirect(Buffer buffer, uint offsetInBytes, uint drawCount)
    {
        SDL3.SDL_DrawGPUIndexedPrimitivesIndirect(Handle, buffer.Handle, offsetInBytes, drawCount);
    }

    public void Dispose()
    {
        if (Handle == null) return;
        SDL3.SDL_EndGPURenderPass(Handle);
        Handle = null;
    }
}
