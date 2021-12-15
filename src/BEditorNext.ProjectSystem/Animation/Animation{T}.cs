using System.Text.Json.Nodes;

using BEditorNext.Animation.Easings;
using BEditorNext.ProjectSystem;

namespace BEditorNext.Animation;

public class Animation<T> : Element, IAnimation
    where T : struct
{
    public static readonly PropertyDefine<Animator<T>> AnimatorProperty;
    public static readonly PropertyDefine<T> PreviousProperty;
    public static readonly PropertyDefine<T> NextProperty;
    public static readonly PropertyDefine<Easing> EasingProperty;
    public static readonly PropertyDefine<TimeSpan> DurationProperty;
    private Animator<T> _animator;
    private T _previous;
    private T _next;
    private Easing _easing;
    private TimeSpan _duration;

    public Animation()
    {
        _animator = (Animator<T>)Activator.CreateInstance(AnimatorRegistry.GetAnimatorType(typeof(T)))!;
        _easing = EasingProperty.GetDefaultValue() ?? new LinearEasing();
    }

    static Animation()
    {
        AnimatorProperty = RegisterProperty<Animator<T>, Animation<T>>(
            nameof(Animation),
            (owner, obj) => owner.Animator = obj,
            owner => owner.Animator);

        PreviousProperty = RegisterProperty<T, Animation<T>>(
            nameof(Previous),
            (owner, obj) => owner.Previous = obj,
            owner => owner.Previous)
            .NotifyPropertyChanged(true)
            .JsonName("prev");

        NextProperty = RegisterProperty<T, Animation<T>>(
            nameof(Next),
            (owner, obj) => owner.Next = obj,
            owner => owner.Next)
            .NotifyPropertyChanged(true)
            .JsonName("next");

        EasingProperty = RegisterProperty<Easing, Animation<T>>(
            nameof(Easing),
            (owner, obj) => owner.Easing = obj,
            owner => owner.Easing)
            .NotifyPropertyChanged(true)
            .DefaultValue(new LinearEasing());

        DurationProperty = RegisterProperty<TimeSpan, Animation<T>>(
            nameof(Duration),
            (owner, obj) => owner.Duration = obj,
            owner => owner.Duration)
            .NotifyPropertyChanged(true)
            .JsonName("duration");
    }

    public Animator<T> Animator
    {
        get => _animator;
        set => SetAndRaise(AnimatorProperty, ref _animator, value);
    }

    public T Previous
    {
        get => _previous;
        set => SetAndRaise(PreviousProperty, ref _previous, value);
    }

    public T Next
    {
        get => _next;
        set => SetAndRaise(NextProperty, ref _next, value);
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

    Animator IAnimation.Animator => Animator;

    object IAnimation.Previous => Previous;

    object IAnimation.Next => Next;

    public override JsonNode ToJson()
    {
        JsonNode node = base.ToJson();

        if (node is JsonObject jsonObject)
        {
            jsonObject.Remove("name");

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
                jsonObject["easing"] = JsonValue.Create(SceneLayer.TypeResolver.ToString(Easing.GetType()));
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
                    Type type = SceneLayer.TypeResolver.ToType(easingType) ?? typeof(LinearEasing);

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
