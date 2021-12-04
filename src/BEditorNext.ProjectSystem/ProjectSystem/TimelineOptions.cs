using System.Numerics;

namespace BEditorNext.ProjectSystem;

public record struct TimelineOptions
{
    private readonly float _scale;
    private readonly Vector2 _offset;

    public TimelineOptions()
        : this(1, Vector2.Zero)
    {
    }

    public TimelineOptions(float scale, Vector2 offset)
    {
        _scale = scale;
        _offset = offset;
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
}
