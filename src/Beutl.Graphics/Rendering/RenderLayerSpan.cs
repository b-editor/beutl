using Beutl.Language;
using Beutl.Media;

namespace Beutl.Rendering;

public sealed class RenderLayerSpan : Element, IAffectsRender
{
    public static readonly CoreProperty<TimeSpan> StartProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    public static readonly CoreProperty<Renderables> ValueProperty;
    public static readonly CoreProperty<RenderLayer?> RenderLayerProperty;
    private readonly Renderables _value;
    private TimeSpan _start;
    private TimeSpan _duration;
    private RenderLayer? _renderLayer;

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

        ValueProperty = ConfigureProperty<Renderables, RenderLayerSpan>(nameof(Value))
            .Accessor(o => o.Value)
            .Register();

        RenderLayerProperty = ConfigureProperty<RenderLayer?, RenderLayerSpan>(nameof(RenderLayer))
            .Accessor(o => o.RenderLayer)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();
    }

    public RenderLayerSpan()
    {
        _value = new(this);
        _value.Invalidated += OnValueInvalidated;
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

    public Renderables Value => _value;

    public RenderLayer? RenderLayer
    {
        get => _renderLayer;
        private set => SetAndRaise(RenderLayerProperty, ref _renderLayer, value);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public event EventHandler<RenderLayer>? AttachedToRenderLayer;

    public event EventHandler<RenderLayer>? DetachedFromRenderLayer;

    private void OnValueInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    public void AttachToRenderLayer(RenderLayer renderLayer)
    {
        if (RenderLayer != null && RenderLayer != renderLayer)
        {
            throw new InvalidOperationException();
        }

        RenderLayer = renderLayer;
        AttachedToRenderLayer?.Invoke(this, renderLayer);
    }

    public void DetachFromRenderLayer()
    {
        RenderLayer? tmp = RenderLayer;
        RenderLayer = null;
        if (tmp != null)
        {
            DetachedFromRenderLayer?.Invoke(this, tmp);
        }
    }
}
