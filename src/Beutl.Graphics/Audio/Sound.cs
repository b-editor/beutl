using Beutl.Animation;
using Beutl.Media.Music;
using Beutl.Rendering;

namespace Beutl.Audio;

public abstract class Sound : Renderable
{
    public static readonly CoreProperty<float> GainProperty;
    private float _gain = 1;
    private LayerNode? _layerNode;

    static Sound()
    {
        GainProperty = ConfigureProperty<float, Sound>(nameof(Gain))
            .Accessor(o => o.Gain, (o, v) => o.Gain = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(1)
            .SerializeName("amplification-factor")
            .Register();

        AffectsRender<Sound>(GainProperty);
    }

    public float Gain
    {
        get => _gain;
        set => SetAndRaise(GainProperty, ref _gain, value);
    }

    public override void Render(IRenderer renderer)
    {
        Record(renderer.Audio);
    }

    public void Record(IAudio audio)
    {
        using (audio.PushGain(_gain))
        {
            OnRecord(audio);
        }
    }

    protected abstract void OnRecord(IAudio audio);

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        _layerNode = args.Parent as LayerNode;
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        _layerNode = null;
    }

    protected abstract void Seek(TimeSpan timeSpan);

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        if (_layerNode != null)
        {
            Seek(clock.CurrentTime - _layerNode.Start);
        }
    }
}
