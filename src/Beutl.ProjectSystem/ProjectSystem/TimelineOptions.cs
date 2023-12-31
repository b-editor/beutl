using System.Numerics;

namespace Beutl.ProjectSystem;

public readonly record struct TimelineOptions
{
    private readonly float _scale;
    private readonly Vector2 _offset;

    public TimelineOptions()
        : this(1, Vector2.Zero)
    {
    }

    public TimelineOptions(float scale, Vector2 offset)
        : this(scale, offset, 50)
    {
    }

    public TimelineOptions(float scale, Vector2 offset, int maxLayerCount)
    {
        _scale = scale;
        _offset = offset;
        MaxLayerCount = maxLayerCount;
    }

    public float Scale
    {
        get => _scale;
        init => _scale = Math.Min(value, 2);
    }

    public Vector2 Offset
    {
        get => _offset;
        init => _offset = Vector2.Max(value, new Vector2(0, 0));
    }

    public int MaxLayerCount { get; init; }
}
