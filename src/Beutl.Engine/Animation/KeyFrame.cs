using System.Text.Json.Nodes;

using Beutl.Animation.Easings;
using Beutl.Language;
using Beutl.Serialization;

namespace Beutl.Animation;

public class KeyFrame : CoreObject
{
    public static readonly CoreProperty<Easing> EasingProperty;
    public static readonly CoreProperty<TimeSpan> KeyTimeProperty;
    private Easing _easing;
    private TimeSpan _keyTime;

    protected KeyFrame()
    {
        _easing = EasingProperty.GetMetadata<CorePropertyMetadata<Easing>>(GetType()).DefaultValue ?? new LinearEasing();
    }

    static KeyFrame()
    {
        EasingProperty = ConfigureProperty<Easing, KeyFrame>(nameof(Easing))
            .Accessor(o => o.Easing, (o, v) => o.Easing = v)
            .DefaultValue(new LinearEasing())
            .Register();

        KeyTimeProperty = ConfigureProperty<TimeSpan, KeyFrame>(nameof(KeyTime))
            .Accessor(o => o.KeyTime, (o, v) => o.KeyTime = v)
            .Register();
    }

    [NotAutoSerialized]
    public Easing Easing
    {
        get => _easing;
        set => SetAndRaise(EasingProperty, ref _easing, value);
    }

    public TimeSpan KeyTime
    {
        get => _keyTime;
        set => SetAndRaise(KeyTimeProperty, ref _keyTime, value);
    }

    internal virtual CoreProperty? Property { get; set; }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        if (Easing is SplineEasing splineEasing)
        {
            json[nameof(Easing)] = new JsonObject
            {
                ["X1"] = splineEasing.X1,
                ["Y1"] = splineEasing.Y1,
                ["X2"] = splineEasing.X2,
                ["Y2"] = splineEasing.Y2,
            };
        }
        else
        {
            json[nameof(Easing)] = TypeFormat.ToString(Easing.GetType());
        }
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);

        if (json.TryGetPropertyValue(nameof(Easing), out JsonNode? easingNode))
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
                float x1 = (float?)easingObject["X1"] ?? 0;
                float y1 = (float?)easingObject["Y1"] ?? 0;
                float x2 = (float?)easingObject["X2"] ?? 1;
                float y2 = (float?)easingObject["Y2"] ?? 1;

                Easing = new SplineEasing(x1, y1, x2, y2);
            }
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<JsonNode>(nameof(Easing)) is { } easingNode)
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
                float x1 = (float?)easingObject["X1"] ?? 0;
                float y1 = (float?)easingObject["Y1"] ?? 0;
                float x2 = (float?)easingObject["X2"] ?? 1;
                float y2 = (float?)easingObject["Y2"] ?? 1;

                Easing = new SplineEasing(x1, y1, x2, y2);
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        if (Easing is SplineEasing splineEasing)
        {
            context.SetValue(nameof(Easing), new JsonObject
            {
                ["X1"] = splineEasing.X1,
                ["Y1"] = splineEasing.Y1,
                ["X2"] = splineEasing.X2,
                ["Y2"] = splineEasing.Y2,
            });
        }
        else
        {
            context.SetValue(nameof(Easing), TypeFormat.ToString(Easing.GetType()));
        }
    }
}
