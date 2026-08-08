using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Animation.Easings;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

public class KeyFrame : Hierarchical
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
        set
        {
            HasLossyEasing = false;
            SetAndRaise(EasingProperty, ref _easing, value);
        }
    }

    [NotAutoSerialized]
    internal bool HasLossyEasing { get; private set; }

    public TimeSpan KeyTime
    {
        get => _keyTime;
        set => SetAndRaise(KeyTimeProperty, ref _keyTime, value);
    }

    public IValidator? Validator { get; set; }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        JsonNode? easingNode = context.GetValue<JsonNode>(nameof(Easing));
        if (easingNode is null)
        {
            if (context.Contains(nameof(Easing)))
            {
                UseFallbackEasing();
            }
        }
        else if (easingNode is JsonValue easingTypeValue
                 && easingTypeValue.TryGetValue(out string? easingType))
        {
            Type? type = TypeFormat.ToType(easingType);
            if (type is null
                || !type.IsAssignableTo(typeof(Easing))
                || type.IsAbstract
                || type.ContainsGenericParameters
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                UseFallbackEasing();
            }
            else
            {
                try
                {
                    if (Activator.CreateInstance(type) is Easing easing)
                    {
                        Easing = easing;
                    }
                    else
                    {
                        UseFallbackEasing();
                    }
                }
                catch (Exception ex) when (ex is MissingMethodException
                                                or MemberAccessException
                                                or TargetInvocationException
                                                or TypeInitializationException
                                                or NotSupportedException)
                {
                    UseFallbackEasing();
                }
            }
        }
        else if (easingNode is JsonObject easingObject)
        {
            try
            {
                float x1 = (float?)easingObject["X1"] ?? 0;
                float y1 = (float?)easingObject["Y1"] ?? 0;
                float x2 = (float?)easingObject["X2"] ?? 1;
                float y2 = (float?)easingObject["Y2"] ?? 1;

                Easing = new SplineEasing(x1, y1, x2, y2);
            }
            catch (Exception ex) when (ex is JsonException
                                            or FormatException
                                            or InvalidOperationException
                                            or ArgumentException)
            {
                UseFallbackEasing();
            }
        }
        else
        {
            UseFallbackEasing();
        }
    }

    private void UseFallbackEasing()
    {
        DeserializationIncidents.RecordFallback();
        Easing = new LinearEasing();
        HasLossyEasing = true;
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
