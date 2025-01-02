using SDL;

namespace Beutl.Graphics3D;

public readonly struct MultisampleState
{
    public static readonly MultisampleState None = new() { SampleCount = SampleCount.One };

    public SampleCount SampleCount { get; init; }

    public uint SampleMask { get; init; }

    public bool EnableMask { get; init; }

    internal SDL_GPUMultisampleState ToNative()
    {
        return new SDL_GPUMultisampleState
        {
            sample_count = (SDL_GPUSampleCount)SampleCount,
            sample_mask = SampleMask,
            enable_mask = EnableMask
        };
    }
}
