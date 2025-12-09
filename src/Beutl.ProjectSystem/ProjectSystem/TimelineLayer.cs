using Beutl.Media;

namespace Beutl.ProjectSystem;

public class TimelineLayer : Hierarchical
{
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<Color> ColorProperty;

    static TimelineLayer()
    {
        ZIndexProperty = ConfigureProperty<int, TimelineLayer>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .DefaultValue(0)
            .Register();

        ColorProperty = ConfigureProperty<Color, TimelineLayer>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Transparent)
            .Register();
    }

    public int ZIndex
    {
        get;
        set => SetAndRaise(ZIndexProperty, ref field, value);
    }

    public Color Color
    {
        get;
        set => SetAndRaise(ColorProperty, ref field, value);
    }
}
