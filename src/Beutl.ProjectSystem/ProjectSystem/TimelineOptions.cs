using System.Numerics;

using Beutl.Utilities;

namespace Beutl.ProjectSystem;

public readonly record struct TimelineOptions
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
        init
        {
            if (MathUtilities.AreClose(value, 1))
                value = 1F;
            else if (MathUtilities.AreClose(value, 2))
                value = 2F;
            else if (MathUtilities.AreClose(value, 0.75))
                value = 0.75F;
            else if (MathUtilities.AreClose(value, 0.50))
                value = 0.50F;
            else if (MathUtilities.AreClose(value, 0.25))
                value = 0.25F;

            _scale = Math.Min(value, 2);
        }
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
