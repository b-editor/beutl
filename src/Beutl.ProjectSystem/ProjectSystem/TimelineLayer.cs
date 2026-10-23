using Beutl.Media;

namespace Beutl.ProjectSystem;

public partial class TimelineLayer : Hierarchical
{
    public TimelineLayer()
    {
        ScanProperties<TimelineLayer>();
    }

    public IProperty<int> ZIndex { get; } = Property.CreateValue(0);

    // A layer starts with no color, as its CoreProperty field did.
    public IProperty<Color> Color { get; } = Property.CreateValue<Color>(default);

    // Layer-level lock; the editor treats Element.IsLocked or TimelineLayer.IsLocked
    // as "this element cannot be edited".
    public IProperty<bool> IsLocked { get; } = Property.CreateValue(false);

    // Orthogonal to IsVideoMuted — a layer can keep its picture while silencing its sound.
    public IProperty<bool> IsAudioMuted { get; } = Property.CreateValue(false);

    public IProperty<bool> IsVideoMuted { get; } = Property.CreateValue(false);

    // Non-exclusive: multiple layers can be soloed simultaneously.
    public IProperty<bool> IsSolo { get; } = Property.CreateValue(false);
}
