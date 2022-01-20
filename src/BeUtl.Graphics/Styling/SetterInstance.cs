
using BeUtl.Animation;

namespace BeUtl.Styling;

#pragma warning disable CA1816

public class SetterInstance<T> : ISetterInstance
{
    private Setter<T>? _setter;

    public SetterInstance(Setter<T> setter)
    {
        _setter = setter;
    }

    public CoreProperty<T> Property => Setter.Property;

    public Setter<T> Setter => _setter ?? throw new InvalidOperationException();

    CoreProperty ISetterInstance.Property => Property;

    ISetter ISetterInstance.Setter => Setter;

    public void Apply(ISetterBatch batch, IClock clock)
    {
        if (batch is SetterBatch<T> setterBatch)
        {
            if (Setter.Animations.Count > 0)
            {
                setterBatch.Value = EaseAnimations(clock.CurrentTime);
            }
            else
            {
                setterBatch.Value = Setter.Value;
            }
        }
    }

    public void Dispose()
    {
        _setter = null;
    }

    private T EaseAnimations(TimeSpan progress)
    {
        static T Ease(Animation<T> animation, float progress)
        {
            // イージングする
            float ease = animation.Easing.Ease(progress);
            // 値を補間する
            T value = animation.Animator.Interpolate(ease, animation.Previous, animation.Next);

            return value;
        }

        TimeSpan cur = TimeSpan.Zero;
        Span<Animation<T>> span = Setter.Animations.AsSpan();
        foreach (Animation<T> item in span)
        {
            TimeSpan next = cur + item.Duration;
            if (cur <= progress && progress < next)
            {
                // 相対的なTimeSpan
                TimeSpan time = progress - cur;
                return Ease(item, (float)(time / item.Duration));
            }
            else
            {
                cur = next;
            }
        }

        return Ease(span[^1], 1);
    }

}
