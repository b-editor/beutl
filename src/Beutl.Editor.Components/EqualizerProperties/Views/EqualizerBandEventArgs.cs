using Avalonia.Interactivity;

namespace Beutl.Editor.Components.EqualizerProperties.Views;

public enum EqualizerBandProperty
{
    Frequency,
    Gain,
    Q
}

public sealed class EqualizerBandEventArgs : RoutedEventArgs
{
    public EqualizerBandEventArgs(RoutedEvent routedEvent, int bandIndex, EqualizerBandProperty property, float oldValue, float newValue)
        : base(routedEvent)
    {
        BandIndex = bandIndex;
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public int BandIndex { get; }

    public EqualizerBandProperty Property { get; }

    public float OldValue { get; }

    public float NewValue { get; }
}

public sealed class EqualizerBandSelectedEventArgs : RoutedEventArgs
{
    public EqualizerBandSelectedEventArgs(RoutedEvent routedEvent, int bandIndex)
        : base(routedEvent)
    {
        BandIndex = bandIndex;
    }

    public int BandIndex { get; }
}
