using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Animation.Easings;
using Beutl.Converters;
using Beutl.Engine;
using Beutl.Serialization;
using Beutl.Utilities;
using Beutl.Validation;

namespace Beutl.Animation;

public class KeyFrame : Hierarchical
{
    // Every key frame starts with this one easing, as the CoreProperty's default value was shared.
    private static readonly LinearEasing s_defaultEasing = new();
    // Shared by every key frame, so that none allocates handlers of its own.
    private static readonly EventHandler<PropertyValueChangedEventArgs<Easing>> s_easingChanged =
        static (_, e) => ((KeyFrame)e.Property.GetOwnerObject()!).OnEasingChanged();
    private static readonly EventHandler<PropertyValueChangedEventArgs<TimeSpan>> s_keyTimeChanged =
        static (_, e) => ((KeyFrame)e.Property.GetOwnerObject()!).OnKeyTimeChanged();
    private Easing? _lossyFallbackEasing;
    private JsonNode? _lossyFallbackEasingJson;

    protected KeyFrame()
    {
        ScanProperties<KeyFrame>();
        EasingProperty.ValueChanged += s_easingChanged;
        KeyTimeProperty.ValueChanged += s_keyTimeChanged;
    }

    // Serialize and Deserialize write and read the easing themselves.
    [NotAutoSerialized]
    public IProperty<Easing> EasingProperty { get; } = Property.CreateValue<Easing>(s_defaultEasing);

    public IProperty<TimeSpan> KeyTimeProperty { get; } = Property.CreateValue(TimeSpan.Zero);

    public Easing Easing
    {
        get => EasingProperty.Value;
        set => EasingProperty.Value = value;
    }

    [NotAutoSerialized]
    internal bool HasLossyEasing => ReferenceEquals(Easing, _lossyFallbackEasing);

    private protected virtual void OnEasingChanged() => OnPropertyChanged(new PropertyChangedEventArgs(nameof(Easing)));

    private protected virtual void OnKeyTimeChanged() => OnPropertyChanged(new PropertyChangedEventArgs(nameof(KeyTime)));

    public TimeSpan KeyTime
    {
        get => KeyTimeProperty.Value;
        set => KeyTimeProperty.Value = value;
    }

    public IValidator? Validator { get; set; }

    private static bool TryReadSplinePoint(JsonObject value, string name, float fallback, out float result)
    {
        result = fallback;
        if (!value.TryGetPropertyValue(name, out JsonNode? node)) return true;
        return node is JsonValue number && number.TryGetValue(out result);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        JsonNode? easingNode = context.GetValue<JsonNode>(nameof(Easing));
        if (easingNode is null)
        {
            if (context.Contains(nameof(Easing)))
            {
                UseFallbackEasing(
                    easingNode,
                    FallbackReason.DeserializationFailed,
                    null,
                    "The easing value is null.");
            }
        }
        else if (easingNode is JsonValue easingTypeValue
                 && easingTypeValue.TryGetValue(out string? easingType))
        {
            DeserializeEasingType(easingNode, easingType);
        }
        else if (easingNode is JsonObject easingObject)
        {
            DeserializeSplineEasing(easingNode, easingObject);
        }
        else
        {
            UseFallbackEasing(
                easingNode,
                FallbackReason.DeserializationFailed,
                null,
                "The easing value has an unsupported JSON representation.");
        }
    }

    private void DeserializeEasingType(JsonNode easingNode, string easingType)
    {
        Type? type = TypeFormat.ToType(easingType);
        if (type is null)
        {
            UseFallbackEasing(
                easingNode,
                FallbackReason.TypeNotFound,
                easingType,
                $"The easing type '{easingType}' could not be resolved.");
        }
        else if (!IsInstantiableEasingType(type))
        {
            UseFallbackEasing(
                easingNode,
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
                        easingNode,
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
                if (ExceptionHelpers.ContainsFatalFailure(ex)
                    || ExceptionHelpers.ContainsFileSystemFailure(ex))
                {
                    if (ex.InnerException is { } inner)
                    {
                        ExceptionDispatchInfo.Capture(inner).Throw();
                    }

                    throw;
                }

                UseFallbackEasing(
                    easingNode,
                    FallbackReason.DeserializationFailed,
                    easingType,
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static bool IsInstantiableEasingType(Type type)
    {
        return EasingJsonConverter.IsConcreteEasingType(type)
            && type.GetConstructor(Type.EmptyTypes) is not null;
    }

    private void DeserializeSplineEasing(JsonNode easingNode, JsonObject easingObject)
    {
        if (TryReadSplineEasing(easingObject, out SplineEasing? splineEasing))
        {
            Easing = splineEasing;
        }
        else
        {
            UseFallbackEasing(
                easingNode,
                FallbackReason.DeserializationFailed,
                null,
                "The spline easing object does not contain four valid control-point values.");
        }
    }

    private static bool TryReadSplineEasing(JsonObject easingObject, [NotNullWhen(true)] out SplineEasing? easing)
    {
        bool isSplineEncoding = !easingObject.TryGetPropertyValue("$type", out JsonNode? typeNode)
            || typeNode is JsonValue typeValue && typeValue.TryGetValue(out string? typeName)
            && TypeFormat.ToType(typeName!) == typeof(SplineEasing);
        if (isSplineEncoding && TryReadSplinePoint(easingObject, "X1", 0, out float x1)
            && TryReadSplinePoint(easingObject, "Y1", 0, out float y1)
            && TryReadSplinePoint(easingObject, "X2", 1, out float x2)
            && TryReadSplinePoint(easingObject, "Y2", 1, out float y2)
            && float.IsFinite(y1) && float.IsFinite(y2)
            && x1 is >= 0 and <= 1 && x2 is >= 0 and <= 1)
        {
            easing = new SplineEasing(x1, y1, x2, y2);
            return true;
        }

        easing = null;
        return false;
    }

    private void UseFallbackEasing(
        JsonNode? originalJson,
        FallbackReason reason,
        string? typeName,
        string message)
    {
        DeserializationIncidents.RecordFallback(reason, typeName, message);
        _lossyFallbackEasing = new LinearEasing();
        _lossyFallbackEasingJson = originalJson?.DeepClone();
        Easing = _lossyFallbackEasing;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        if (HasLossyEasing)
        {
            LossyEasingSerializationCapture.Record();
            context.SetValue(nameof(Easing), _lossyFallbackEasingJson?.DeepClone());
        }
        else if (Easing is SplineEasing splineEasing)
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
