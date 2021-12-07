using System.Numerics;

namespace BEditorNext.ProjectSystem;

public record struct TimelineOptions
{
    private readonly float _scale;
    private readonly Vector2 _offset;
    private readonly int _maxLayerCount;

    public TimelineOptions()
        : this(1, Vector2.Zero)
    {
    }

    public TimelineOptions(float scale, Vector2 offset)
        : this(scale, offset, 100)
    {
    }
    
    public TimelineOptions(float scale, Vector2 offset, int maxLayerCount)
    {
        _scale = scale;
        _offset = offset;
        _maxLayerCount = maxLayerCount;
    }

    public float Scale
    {
        get => _scale;
        init => _scale = Math.Min(value, 1);
    }

    public Vector2 Offset
    {
        get => _offset;
        init => _offset = Vector2.Max(value, new Vector2(0, 0));
    }

    public int MaxLayerCount
    {
        get => _maxLayerCount;
        init => _maxLayerCount = Math.Max(100, value);
    }
}
