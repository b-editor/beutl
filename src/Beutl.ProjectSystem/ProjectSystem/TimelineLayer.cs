using Beutl.Media;

namespace Beutl.ProjectSystem;

public class TimelineLayer : Hierarchical
{
    public TimelineLayer()
    {
        ScanProperties<TimelineLayer>();
    }

    public IProperty<int> ZIndex { get; } = new ValueOnlyProperty<int>(0);

    // A layer starts with no color, as its CoreProperty field did.
    public IProperty<Color> Color { get; } = new ValueOnlyProperty<Color>(default);

    // Layer-level lock; the editor treats Element.IsLocked or TimelineLayer.IsLocked
    // as "this element cannot be edited".
    public IProperty<bool> IsLocked { get; } = new ValueOnlyProperty<bool>(false);

    // Orthogonal to IsVideoMuted — a layer can keep its picture while silencing its sound.
    public IProperty<bool> IsAudioMuted { get; } = new ValueOnlyProperty<bool>(false);

    public IProperty<bool> IsVideoMuted { get; } = new ValueOnlyProperty<bool>(false);

    // Non-exclusive: multiple layers can be soloed simultaneously.
    public IProperty<bool> IsSolo { get; } = new ValueOnlyProperty<bool>(false);
}
