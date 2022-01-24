using System.Text.Json.Nodes;

using BeUtl.Animation.Easings;

namespace BeUtl.Animation;

public abstract class BaseAnimation : CoreObject
{
    public static readonly CoreProperty<Easing> EasingProperty;
    public static readonly CoreProperty<TimeSpan> DurationProperty;
    private Easing _easing;
    private TimeSpan _duration;

    protected BaseAnimation()
    {
        _easing = (EasingProperty.GetMetadata<CorePropertyMetadata<Easing>>(GetType()).DefaultValue) ?? new LinearEasing();
    }

    static BaseAnimation()
    {
        EasingProperty = ConfigureProperty<Easing, BaseAnimation>(nameof(Easing))
            .Accessor(o => o.Easing, (o, v) => o.Easing = v)
            .Observability(PropertyObservability.Changed)
            .DefaultValue(new LinearEasing())
            .Register();

        DurationProperty = ConfigureProperty<TimeSpan, BaseAnimation>(nameof(Duration))
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

    public override JsonNode ToJson()
    {
        JsonNode node = base.ToJson();

        if (node is JsonObject jsonObject)
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

        return node;
    }

    public override void FromJson(JsonNode json)
    {
        base.FromJson(json);

        if (json is JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("easing", out JsonNode? easingNode))
            {
                if (easingNode is JsonValue easingTypeValue &&
                    easingTypeValue.TryGetValue(out string? easingType))
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
}
