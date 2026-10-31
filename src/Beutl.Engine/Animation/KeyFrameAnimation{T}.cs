using System.ComponentModel;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

// Keyframe animation is the expression at the bottom of a property's expression chain (see IExpression<T>.Replaced).
public partial class KeyFrameAnimation<T> : Hierarchical, IKeyFrameAnimation, IPropertyAnimation<T>, IKeyFrameExpression
{
    private IValidator<T>? _validator;
    private EngineObject? _parent;

    public KeyFrameAnimation()
    {
        ScanProperties<KeyFrameAnimation<T>>();
        UseGlobalClock.ValueChanged += (_, _) => Edited?.Invoke(this, EventArgs.Empty);
        KeyFrames.Attached += OnKeyFrameAttached;
        KeyFrames.Detached += OnKeyFrameDetached;
    }

    public event EventHandler? Edited;

    public Type ValueType => typeof(T);

    public string ExpressionString => "[KeyFrames]";

    public IProperty<bool> UseGlobalClock { get; } = Property.CreateValue(false);

    // Serialize and Deserialize write and read the key frames themselves.
    [NotAutoSerialized]
    public IListProperty<IKeyFrame> KeyFrames { get; } = new ListProperty<IKeyFrame> { AffectsRender = false };

    public IValidator<T>? Validator
    {
        get => _validator;
        set
        {
            _validator = value;
            foreach (IKeyFrame item in KeyFrames)
            {
                if (item is KeyFrame keyFrame)
                {
                    keyFrame.Validator = value;
                }
            }
        }
    }

    IValidator? IKeyFrameAnimation.Validator
    {
        get => Validator;
        set => Validator = value as IValidator<T>;
    }

    // A property without a validator leaves the key frames' current one in place.
    void IPropertyAnimation.Configure(IProperty owner)
    {
        if (owner.GetValidator() is IValidator<T> validator) Validator = validator;
    }

    public MediaTime Duration
        => KeyFrames.Count > 0
            ? KeyFrames[^1].KeyTime.Value
            : MediaTime.Zero;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }

    public T EvaluateAt(MediaTime time) => GetAnimatedValue(time)!;

    private T InterpolateValues(float progress, T previous, T next, MediaTime previousTime, MediaTime nextTime)
        => HierarchicalParent is IComponentInterpolator<T> interpolate
            ? interpolate.Interpolate(this, progress, previous, next, previousTime, nextTime)
            : KeyFrame<T>.s_animator.Interpolate(progress, previous, next);

    public T Evaluate(ExpressionContext context) => GetAnimatedValue(context.Time)!;

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe)
    {
        return KeyFrames.GetPreviousAndNextKeyFrame(keyframe);
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(MediaTime timeSpan)
    {
        return KeyFrames.GetPreviousAndNextKeyFrame(timeSpan);
    }

    private MediaTime TimeOrigin => UseGlobalClock.Value ? MediaTime.Zero : _parent?.Start ?? MediaTime.Zero;

    public MediaTime ToLocal(MediaTime sceneTime) => sceneTime - TimeOrigin;

    public MediaTime ToScene(MediaTime localTime) => localTime + TimeOrigin;

    public T? GetAnimatedValue(MediaTime time) => Interpolate(ToLocal(time));

    public T? Interpolate(MediaTime timeSpan)
    {
        (IKeyFrame? prev, IKeyFrame? next) = GetPreviousAndNextKeyFrame(timeSpan);

        if (next is KeyFrame<T> next2)
        {
            T? nextValue = next2.Value.Value;
            T? prevValue = prev is KeyFrame<T> prev2 ? prev2.Value.Value : nextValue;
            MediaTime prevTime = prev?.KeyTime.Value ?? MediaTime.Zero;
            MediaTime nextTime = next.KeyTime.Value;
            // Zero除算になるので
            if (nextTime == prevTime)
            {
                return nextValue;
            }

            float progress = (float)((double)(timeSpan - prevTime).Flicks / (nextTime - prevTime).Flicks);
            float ease = next.Easing.Value.Ease(progress);
            if (prevValue == null)
                return nextValue;
            else if (nextValue == null)
                return prevValue;

            T? value = InterpolateValues(ease, prevValue, nextValue, prevTime, nextTime);

            return value;
        }
        else if (prev is KeyFrame<T> prev2)
        {
            return prev2.Value.Value;
        }
        else
        {
            return KeyFrame<T>.s_animator.DefaultValue();
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(KeyFrames), KeyFrames);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<IKeyFrame[]>(nameof(KeyFrames)) is { } keyframes)
        {
            KeyFrames.Replace(keyframes);
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        _parent = this.FindHierarchicalParent<EngineObject>();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        _parent = null;
    }

    private void OnKeyTimeChanged(object? sender, EventArgs e)
    {
        if (sender is not IKeyFrame keyframe) return;
        using var _ = PublishingSuppression.Enter();

        int index = KeyFrames.IndexOf(keyframe);
        (IKeyFrame? prev, IKeyFrame? next) = GetPreviousAndNextKeyFrame(keyframe);

        if (IsOutOfOrder(prev, keyframe, next))
        {
            for (int i = 0; i < KeyFrames.Count; i++)
            {
                IKeyFrame item = KeyFrames[i];
                if (keyframe != item && keyframe.KeyTime.Value <= item.KeyTime.Value)
                {
                    if (index < i)
                    {
                        i--;
                    }
                    KeyFrames.Move(index, i);
                    return;
                }
            }

            KeyFrames.Move(index, KeyFrames.Count - 1);
        }
    }

    private static bool IsOutOfOrder(IKeyFrame? prev, IKeyFrame keyframe, IKeyFrame? next)
    {
        return (prev != null && prev.KeyTime.Value > keyframe.KeyTime.Value)
            || (next != null && keyframe.KeyTime.Value > next.KeyTime.Value);
    }

    private void OnKeyFrameEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void OnKeyFrameAttached(IKeyFrame obj)
    {
        if (obj is KeyFrame keyFrame)
        {
            keyFrame.Validator = Validator;
        }

        obj.KeyTimeChanged += OnKeyTimeChanged;
        obj.Edited += OnKeyFrameEdited;
    }

    private void OnKeyFrameDetached(IKeyFrame obj)
    {
        if (obj is KeyFrame keyFrame)
        {
            keyFrame.Validator = null;
        }

        obj.KeyTimeChanged -= OnKeyTimeChanged;
        obj.Edited -= OnKeyFrameEdited;
    }
}
