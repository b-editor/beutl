using SDL;

namespace Beutl.Graphics3D;

public unsafe readonly struct VertexBufferDescription
{
    public uint Slot { get; init; }

    public uint Pitch { get; init; }

    public VertexInputRate InputRate { get; init; }

    public uint InstanceStepRate { get; init; }

    public static VertexBufferDescription Create<T>(
        uint slot = 0,
        VertexInputRate inputRate = VertexInputRate.Vertex,
        uint stepRate = 0)
        where T : unmanaged
    {
        return new VertexBufferDescription
        {
            Slot = slot,
            Pitch = (uint)sizeof(T),
            InputRate = inputRate,
            InstanceStepRate = stepRate
        };
    }

    internal SDL_GPUVertexBufferDescription ToNative()
    {
        return new SDL_GPUVertexBufferDescription
        {
            slot = Slot,
            pitch = Pitch,
            input_rate = (SDL_GPUVertexInputRate)InputRate,
            instance_step_rate = InstanceStepRate
        };
    }
}
