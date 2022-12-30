using System.ComponentModel;

using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Rendering;

public sealed class RenderLayerSpan : Element
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    public static readonly CoreProperty<Renderable?> ValueProperty;
    private TimeSpan _start;
    private TimeSpan _duration;
    private Renderable? _value;

    static RenderLayerSpan()
    {
        StartProperty = ConfigureProperty<TimeSpan, RenderLayerSpan>(nameof(Start))
            .Accessor(o => o.Start, (o, v) => o.Start = v)
            .Display(Strings.StartTime)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, RenderLayerSpan>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Display(Strings.DurationTime)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        ValueProperty = ConfigureProperty<Renderable?, RenderLayerSpan>(nameof(Value))
            .Accessor(o => o.Value, (o, v) => o.Value = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        LogicalChild<RenderLayerSpan>(ValueProperty);
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
