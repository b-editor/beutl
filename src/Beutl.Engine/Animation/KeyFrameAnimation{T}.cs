using Beutl.Engine;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.Animation;

public class KeyFrameAnimation<T> : KeyFrameAnimation, IAnimation<T>
{
    private EngineObject? _parent;

    public override Type ValueType => typeof(T);

    public new IValidator<T>? Validator
    {
        get => base.Validator as IValidator<T>;
        set => base.Validator = value;
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

    public T? GetAnimatedValue(TimeSpan time)
    {
        return Interpolate(ToLocalTime(time));
    }

    // UseGlobalClock=false のとき、グローバル時刻を所属 EngineObject のローカル時刻へ変換する。
    // 補間/積分側 (Interpolate, SpeedIntegrator.Integrate) は呼び出し側の座標系をそのまま使うため、
    // ローカル基準で動かしたい呼び出し元はこのメソッドを通して入力時刻を揃える必要がある。
    public TimeSpan ToLocalTime(TimeSpan time)
    {
        if (_parent != null && !UseGlobalClock)
        {
            return time - _parent.TimeRange.Start;
        }

        return time;
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
            // どちらかがnullの場合、片方を返す
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
}
