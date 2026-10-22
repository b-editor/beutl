using System.ComponentModel;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

// Keyframe animation is the expression at the bottom of a property's expression chain (see IExpression<T>.Replaced).
public class KeyFrameAnimation<T> : Hierarchical, IKeyFrameAnimation, IExpression<T>, IKeyFrameExpression
{
    private readonly KeyFrames _keyFrames;
    private IValidator<T>? _validator;
    private EngineObject? _parent;

    public KeyFrameAnimation()
    {
        _keyFrames = new KeyFrames(this);
        // The list is a value rather than a default, as its CoreProperty form had no default.
        KeyFramesProperty.Value = _keyFrames;
        ScanProperties<KeyFrameAnimation<T>>();
        UseGlobalClockProperty.ValueChanged += (_, _) =>
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(UseGlobalClock)));
            Edited?.Invoke(this, EventArgs.Empty);
        };
        _keyFrames.Attached += OnKeyFrameAttached;
        _keyFrames.Detached += OnKeyFrameDetached;
    }

    public event EventHandler? Edited;

    public Type ValueType => typeof(T);

    public string ExpressionString => "[KeyFrames]";

    public IProperty<bool> UseGlobalClockProperty { get; } = Property.CreateValue(false);

    // Serialize and Deserialize write and read the key frames themselves.
    [NotAutoSerialized]
    public IProperty<KeyFrames> KeyFramesProperty { get; } = new ValueOnlyProperty<KeyFrames>(null!) { KeepsList = true };

    public bool UseGlobalClock
    {
        get => UseGlobalClockProperty.Value;
        set => UseGlobalClockProperty.Value = value;
    }

    public KeyFrames KeyFrames
    {
        get => _keyFrames;
        set => _keyFrames.Replace(value);
    }

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

    public TimeSpan Duration
        => KeyFrames.Count > 0
            ? KeyFrames[^1].KeyTime
            : TimeSpan.Zero;

    public bool Validate(out string? error)
    {
        error = null;
        return true;
    }

    public T Evaluate(ExpressionContext context) => GetAnimatedValue(context.Time)!;

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe)
    {
        return KeyFrames.GetPreviousAndNextKeyFrame(keyframe);
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(TimeSpan timeSpan)
    {
        return KeyFrames.GetPreviousAndNextKeyFrame(timeSpan);
    }

    public T? GetAnimatedValue(TimeSpan time)
    {
        if (_parent != null && !UseGlobalClock)
        {
            return Interpolate(time - _parent.TimeRange.Start);
        }

        return Interpolate(time);
    }

    public T? Interpolate(TimeSpan timeSpan)
    {
        (IKeyFrame? prev, IKeyFrame? next) = GetPreviousAndNextKeyFrame(timeSpan);

        if (next is KeyFrame<T> next2)
        {
            T? nextValue = next2.Value;
            T? prevValue = prev is KeyFrame<T> prev2 ? prev2.Value : nextValue;
            TimeSpan prevTime = prev?.KeyTime ?? TimeSpan.Zero;
            TimeSpan nextTime = next.KeyTime;
            // Zero除算になるので
            if (nextTime == prevTime)
            {
                return nextValue;
            }

            float progress = (float)((timeSpan - prevTime) / (nextTime - prevTime));
            float ease = next.Easing.Ease(progress);
            if (prevValue == null)
                return nextValue;
            else if (nextValue == null)
                return prevValue;

            T? value = KeyFrame<T>.s_animator.Interpolate(ease, prevValue, nextValue);

            return value;
        }
        else if (prev is KeyFrame<T> prev2)
        {
            return prev2.Value;
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

        if (context.GetValue<KeyFrames>(nameof(KeyFrames)) is { } keyframes)
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
                if (keyframe != item && keyframe.KeyTime <= item.KeyTime)
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
        return (prev != null && prev.KeyTime > keyframe.KeyTime)
            || (next != null && keyframe.KeyTime > next.KeyTime);
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
