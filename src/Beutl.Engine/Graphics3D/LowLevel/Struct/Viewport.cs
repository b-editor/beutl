using SDL;

namespace Beutl.Graphics3D;

public readonly struct Viewport
{
    public Viewport(float width, float height)
    {
        Width = width;
        Height = height;
        MaxDepth = 1;
    }

    public Viewport(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        MaxDepth = 1;
    }

    public float X { get; init; }

    public float Y { get; init; }

    public float Width { get; init; }

    public float Height { get; init; }

    public float MinDepth { get; init; }

    public float MaxDepth { get; init; }

    internal SDL_GPUViewport ToNative()
    {
        return new SDL_GPUViewport
        {
            x = X,
            y = Y,
            w = Width,
            h = Height,
            min_depth = MinDepth,
            max_depth = MaxDepth
        };
    }
}
