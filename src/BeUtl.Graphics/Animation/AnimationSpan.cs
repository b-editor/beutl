using System.Text.Json.Nodes;

using BeUtl.Animation.Easings;
using BeUtl.Styling;

namespace BeUtl.Animation;

public abstract class AnimationSpan : CoreObject, ILogicalElement, IStylingElement
{
    public static readonly CoreProperty<Easing> EasingProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    private Easing _easing;
    private TimeSpan _duration;
    private LogicalElementImpl _logicalElement;
    private StylingElementImpl _stylingElement;

    protected AnimationSpan()
    {
        _easing = (EasingProperty.GetMetadata<CorePropertyMetadata<Easing>>(GetType()).DefaultValue) ?? new LinearEasing();
    }

    static AnimationSpan()
    {
        EasingProperty = ConfigureProperty<Easing, AnimationSpan>(nameof(Easing))
            .Accessor(o => o.Easing, (o, v) => o.Easing = v)
            .Observability(PropertyObservability.Changed)
            .DefaultValue(new LinearEasing())
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, AnimationSpan>(nameof(Duration))
            .Accessor(o => o.Duration, (o, v) => o.Duration = v)
            .Observability(PropertyObservability.Changed)
            .SerializeName("duration")
            .Register();
    }

    public Easing Easing
    {
        get => _easing;
        set => SetAndRaise(EasingProperty, ref _easing, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        set => SetAndRaise(DurationProperty, ref _duration, value);
    }

    public ILogicalElement? LogicalParent => _logicalElement.LogicalParent;

    public IEnumerable<ILogicalElement> LogicalChildren => Enumerable.Empty<ILogicalElement>();

    public IStylingElement? StylingParent => _stylingElement.StylingParent;

    public IEnumerable<IStylingElement> StylingChildren => Enumerable.Empty<IStylingElement>();

    public event EventHandler<LogicalTreeAttachmentEventArgs> AttachedToLogicalTree
    {
        add => _logicalElement.AttachedToLogicalTree += value;
        remove => _logicalElement.AttachedToLogicalTree -= value;
    }

    public event EventHandler<LogicalTreeAttachmentEventArgs> DetachedFromLogicalTree
    {
        add => _logicalElement.DetachedFromLogicalTree += value;
        remove => _logicalElement.DetachedFromLogicalTree -= value;
    }

    public event EventHandler<StylingTreeAttachmentEventArgs> AttachedToStylingTree
    {
        add => _stylingElement.AttachedToStylingTree += value;
        remove => _stylingElement.AttachedToStylingTree -= value;
    }

    public event EventHandler<StylingTreeAttachmentEventArgs> DetachedFromStylingTree
    {
        add => _stylingElement.DetachedFromStylingTree += value;
        remove => _stylingElement.DetachedFromStylingTree -= value;
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);

        if (json is JsonObject jsonObject)
        {
            if (Easing is SplineEasing splineEasing)
            {
                jsonObject["easing"] = new JsonObject
                {
                    ["x1"] = splineEasing.X1,
                    ["y1"] = splineEasing.Y1,
                    ["x2"] = splineEasing.X2,
                    ["y2"] = splineEasing.Y2,
                };
            }
            else
            {
                jsonObject["easing"] = JsonValue.Create(TypeFormat.ToString(Easing.GetType()));
            }
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);

        if (json is JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("easing", out JsonNode? easingNode))
            {
                if (easingNode is JsonValue easingTypeValue
                    && easingTypeValue.TryGetValue(out string? easingType))
                {
                    Type type = TypeFormat.ToType(easingType) ?? typeof(LinearEasing);

                    if (Activator.CreateInstance(type) is Easing easing)
                    {
                        Easing = easing;
                    }
                }
                else if (easingNode is JsonObject easingObject)
                {
                    float x1 = (float?)easingObject["x1"] ?? 0;
                    float y1 = (float?)easingObject["y1"] ?? 0;
                    float x2 = (float?)easingObject["x2"] ?? 1;
                    float y2 = (float?)easingObject["y2"] ?? 1;

                    Easing = new SplineEasing(x1, y1, x2, y2);
                }
            }
        }
    }

    protected virtual void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnAttachedToStylingTree(in StylingTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromStylingTree(in StylingTreeAttachmentEventArgs args)
    {
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        _logicalElement.VerifyAttachedToLogicalTree();
        OnAttachedToLogicalTree(e);
        _logicalElement.NotifyAttachedToLogicalTree(e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        _logicalElement.VerifyDetachedFromLogicalTree(e);
        OnDetachedFromLogicalTree(e);
        _logicalElement.NotifyDetachedFromLogicalTree(e);
    }

    void IStylingElement.NotifyAttachedToStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        _stylingElement.VerifyAttachedToStylingTree();
        OnAttachedToStylingTree(e);
        _stylingElement.NotifyAttachedToStylingTree(e);
    }

    void IStylingElement.NotifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        _stylingElement.VerifyDetachedFromStylingTree(e);
        OnDetachedFromStylingTree(e);
        _stylingElement.NotifyDetachedFromStylingTree(e);
    }
}
