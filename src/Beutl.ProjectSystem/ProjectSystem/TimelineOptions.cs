using System.Numerics;

namespace Beutl.ProjectSystem;

public readonly record struct BpmGridOptions
{
    public BpmGridOptions() : this(120.0, 4, MediaTime.Zero, false) { }

    public BpmGridOptions(double bpm, int subdivisions, MediaTime offset, bool isEnabled)
    {
        Bpm = bpm;
        Subdivisions = subdivisions;
        Offset = offset;
        IsEnabled = isEnabled;
    }

    public double Bpm { get; init; }
    public int Subdivisions { get; init; }
    public MediaTime Offset { get; init; }
    public bool IsEnabled { get; init; }
}

public readonly record struct TimelineOptions
{
    private readonly float _scale;
    private readonly Vector2 _offset;

    public TimelineOptions()
    {
        _scale = 1;
        _offset = Vector2.Zero;
        MaxLayerCount = 50;
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

    public BpmGridOptions BpmGrid { get; init; } = new();
}
