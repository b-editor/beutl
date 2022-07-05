
using BeUtl.Animation;

namespace BeUtl.Styling;

#pragma warning disable CA1816

public class SetterInstance<T> : ISetterInstance
{
    private IStyleable? _target;
    private Setter<T>? _setter;

    public SetterInstance(Setter<T> setter, IStyleable target)
    {
        _setter = setter;
        _target = target;
    }

    public CoreProperty<T> Property => Setter.Property;

    public Setter<T> Setter => _setter ?? throw new InvalidOperationException();

    public IStyleable Target => _target ?? throw new InvalidOperationException();

    CoreProperty ISetterInstance.Property => Property;

    ISetter ISetterInstance.Setter => Setter;

    public void Apply(IClock clock)
    {
        if (Setter.Animations.Count > 0)
        {
            Target.SetValue(Property, EaseAnimations(clock.CurrentTime));
        }
        else
        {
            Target.SetValue(Property, Setter.Value);
        }
    }

    public void Begin()
    {
    }

    public void Dispose()
    {
        _target?.ClearValue(Property);
        _setter = null;
        _target = null;
    }

    public void End()
    {
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
