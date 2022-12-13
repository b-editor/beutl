using Beutl.Media;

namespace Beutl.Rendering;

public sealed class LayerNode : Element
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    public static readonly CoreProperty<Renderable?> ValueProperty;
    private TimeSpan _start;
    private TimeSpan _duration;
    private Renderable? _value;

    static LayerNode()
    {
        StartProperty = ConfigureProperty<TimeSpan, LayerNode>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, LayerNode>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        ValueProperty = ConfigureProperty<Renderable?, LayerNode>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        LogicalChild<LayerNode>(ValueProperty);
    }

    public TimeSpan Start
    {
        get => _start;
        set => SetAndRaise(StartProperty, ref _start, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        set => SetAndRaise(DurationProperty, ref _duration, value);
    }

    public TimeRange Range => new(Start, Duration);

    public Renderable? Value
    {
        get => _value;
        set => SetAndRaise(ValueProperty, ref _value, value);
    }
}
