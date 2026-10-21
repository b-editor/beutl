using System.ComponentModel;
using Beutl.Media;

namespace Beutl.ProjectSystem;

public class TimelineLayer : Hierarchical
{
    public TimelineLayer()
    {
        ScanProperties<TimelineLayer>();
        ZIndexProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(ZIndex)));
        ColorProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Color)));
        IsLockedProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsLocked)));
        IsAudioMutedProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsAudioMuted)));
        IsVideoMutedProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsVideoMuted)));
        IsSoloProperty.ValueChanged += (_, _) => OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsSolo)));
    }

    public IProperty<int> ZIndexProperty { get; } = new ValueOnlyProperty<int>(0);

    // A layer starts with no color, as its CoreProperty field did.
    public IProperty<Color> ColorProperty { get; } = new ValueOnlyProperty<Color>(default);

    public IProperty<bool> IsLockedProperty { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<bool> IsAudioMutedProperty { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<bool> IsVideoMutedProperty { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<bool> IsSoloProperty { get; } = new ValueOnlyProperty<bool>(false);

    public int ZIndex
    {
        get => ZIndexProperty.Value;
        set => ZIndexProperty.Value = value;
    }

    public Color Color
    {
        get => ColorProperty.Value;
        set => ColorProperty.Value = value;
    }

    // Layer-level lock; the editor treats Element.IsLocked or TimelineLayer.IsLocked
    // as "this element cannot be edited".
    public bool IsLocked
    {
        get => IsLockedProperty.Value;
        set => IsLockedProperty.Value = value;
    }

    // Orthogonal to IsVideoMuted — a layer can keep its picture while silencing its sound.
    public bool IsAudioMuted
    {
        get => IsAudioMutedProperty.Value;
        set => IsAudioMutedProperty.Value = value;
    }

    public bool IsVideoMuted
    {
        get => IsVideoMutedProperty.Value;
        set => IsVideoMutedProperty.Value = value;
    }

    // Non-exclusive: multiple layers can be soloed simultaneously.
    public bool IsSolo
    {
        get => IsSoloProperty.Value;
        set => IsSoloProperty.Value = value;
    }
}
