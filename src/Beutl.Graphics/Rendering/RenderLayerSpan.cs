using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;

namespace Beutl.Rendering;

public sealed class RenderLayerSpan : Hierarchical, IAffectsRender
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
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, RenderLayerSpan>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Register();

        ValueProperty = ConfigureProperty<Renderables, RenderLayerSpan>(nameof(Value))
            .Accessor(o => o.Value)
            .Register();

        RenderLayerProperty = ConfigureProperty<RenderLayer?, RenderLayerSpan>(nameof(RenderLayer))
            .Accessor(o => o.RenderLayer)
            .Register();
    }

    public RenderLayerSpan()
    {
        _value = new(this);
        _value.Invalidated += OnValueInvalidated;
    }

    [Display(Name = nameof(Strings.StartTime), ResourceType = typeof(Strings))]
    public TimeSpan Start
    {
        get => _start;
        set => SetAndRaise(StartProperty, ref _start, value);
    }

    [Display(Name = nameof(Strings.DurationTime), ResourceType = typeof(Strings))]
    public TimeSpan Duration
    {
        get => _duration;
        set => SetAndRaise(DurationProperty, ref _duration, value);
    }

    public TimeRange Range => new(Start, Duration);

    public Renderables Value => _value;

    [NotAutoSerialized]
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
