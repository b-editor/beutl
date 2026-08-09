using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Animation.Easings;
using Beutl.Serialization;
using Beutl.Utilities;
using Beutl.Validation;

namespace Beutl.Animation;

public class KeyFrame : Hierarchical
{
    public static readonly CoreProperty<Easing> EasingProperty;
    public static readonly CoreProperty<TimeSpan> KeyTimeProperty;
    private Easing _easing;
    private Easing? _lossyFallbackEasing;
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

    [NotAutoSerialized]
    internal bool HasLossyEasing => ReferenceEquals(_easing, _lossyFallbackEasing);

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
                UseFallbackEasing(
                    FallbackReason.DeserializationFailed,
                    null,
                    "The easing value is null.");
            }
        }
        else if (easingNode is JsonValue easingTypeValue
                 && easingTypeValue.TryGetValue(out string? easingType))
        {
            Type? type = TypeFormat.ToType(easingType);
            if (type is null)
            {
                UseFallbackEasing(
                    FallbackReason.TypeNotFound,
                    easingType,
                    $"The easing type '{easingType}' could not be resolved.");
            }
            else if (!type.IsAssignableTo(typeof(Easing))
                || type.IsAbstract
                || type.ContainsGenericParameters
                || type.GetConstructor(Type.EmptyTypes) is null)
            {
                UseFallbackEasing(
                    FallbackReason.DeserializationFailed,
                    easingType,
                    $"The easing type '{easingType}' cannot be instantiated as an Easing.");
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
                        UseFallbackEasing(
                            FallbackReason.DeserializationFailed,
                            easingType,
                            $"The easing type '{easingType}' did not create an Easing instance.");
                    }
                }
                catch (Exception ex) when (ex is MissingMethodException
                                                or MemberAccessException
                                                or TargetInvocationException
                                                or TypeInitializationException
                                                or NotSupportedException)
                {
                    if (ExceptionHelpers.ContainsFileSystemFailure(ex))
                    {
                        if (ex.InnerException is { } inner)
                        {
                            ExceptionDispatchInfo.Capture(inner).Throw();
                        }

                        throw;
                    }

                    UseFallbackEasing(
                        FallbackReason.DeserializationFailed,
                        easingType,
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        else if (easingNode is JsonObject easingObject)
        {
            if (easingObject["X1"] is JsonValue x1Value
                && easingObject["Y1"] is JsonValue y1Value
                && easingObject["X2"] is JsonValue x2Value
                && easingObject["Y2"] is JsonValue y2Value
                && x1Value.TryGetValue<float>(out float x1)
                && y1Value.TryGetValue<float>(out float y1)
                && x2Value.TryGetValue<float>(out float x2)
                && y2Value.TryGetValue<float>(out float y2))
            {
                Easing = new SplineEasing(x1, y1, x2, y2);
            }
            else
            {
                UseFallbackEasing(
                    FallbackReason.DeserializationFailed,
                    null,
                    "The spline easing object does not contain four valid control-point values.");
            }
        }
        else
        {
            UseFallbackEasing(
                FallbackReason.DeserializationFailed,
                null,
                "The easing value has an unsupported JSON representation.");
        }
    }

    private void UseFallbackEasing(
        FallbackReason reason,
        string? typeName,
        string message)
    {
        DeserializationIncidents.RecordFallback(reason, typeName, message);
        _lossyFallbackEasing = new LinearEasing();
        Easing = _lossyFallbackEasing;
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
