using SDL;

namespace Beutl.Graphics3D;

public unsafe class Device : IDisposable
{
    private Device(SDL_GPUDevice* device)
    {
        Handle = device;
    }

    internal SDL_GPUDevice* Handle { get; set; }

    public CommandBuffer AcquireCommandBuffer()
    {
        var commandBuffer = SDL3.SDL_AcquireGPUCommandBuffer(Handle);
        if (commandBuffer == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new CommandBuffer(commandBuffer);
    }

    public static Device Create(ShaderFormat formatFlags, bool debugMode, string? name)
    {
        var device = SDL3.SDL_CreateGPUDevice((SDL_GPUShaderFormat)formatFlags, debugMode, name);
        if (device == null)
        {
            throw new InvalidOperationException(SDL3.SDL_GetError());
        }

        return new Device(device);
    }

    public void Dispose()
    {
        SDL3.SDL_DestroyGPUDevice(Handle);
        Handle = null;
    }
}
