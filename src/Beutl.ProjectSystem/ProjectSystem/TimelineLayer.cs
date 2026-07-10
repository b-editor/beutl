using Beutl.Media;

namespace Beutl.ProjectSystem;

public class TimelineLayer : Hierarchical
{
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<bool> IsLockedProperty;
    public static readonly CoreProperty<bool> IsAudioMutedProperty;
    public static readonly CoreProperty<bool> IsVideoMutedProperty;
    public static readonly CoreProperty<bool> IsSoloProperty;

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

        IsLockedProperty = ConfigureProperty<bool, TimelineLayer>(nameof(IsLocked))
            .Accessor(o => o.IsLocked, (o, v) => o.IsLocked = v)
            .DefaultValue(false)
            .Register();

        IsAudioMutedProperty = ConfigureProperty<bool, TimelineLayer>(nameof(IsAudioMuted))
            .Accessor(o => o.IsAudioMuted, (o, v) => o.IsAudioMuted = v)
            .DefaultValue(false)
            .Register();

        IsVideoMutedProperty = ConfigureProperty<bool, TimelineLayer>(nameof(IsVideoMuted))
            .Accessor(o => o.IsVideoMuted, (o, v) => o.IsVideoMuted = v)
            .DefaultValue(false)
            .Register();

        IsSoloProperty = ConfigureProperty<bool, TimelineLayer>(nameof(IsSolo))
            .Accessor(o => o.IsSolo, (o, v) => o.IsSolo = v)
            .DefaultValue(false)
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

    // Layer-level lock; the editor treats Element.IsLocked or TimelineLayer.IsLocked
    // as "this element cannot be dragged/trimmed/split/deleted".
    public bool IsLocked
    {
        get;
        set => SetAndRaise(IsLockedProperty, ref field, value);
    }

    // Orthogonal to IsVideoMuted — a layer can keep its picture while silencing its sound.
    public bool IsAudioMuted
    {
        get;
        set => SetAndRaise(IsAudioMutedProperty, ref field, value);
    }

    public bool IsVideoMuted
    {
        get;
        set => SetAndRaise(IsVideoMutedProperty, ref field, value);
    }

    // Non-exclusive: multiple layers can be soloed simultaneously.
    public bool IsSolo
    {
        get;
        set => SetAndRaise(IsSoloProperty, ref field, value);
    }
}
