namespace BeUtl.Animation;

public class Animation<T> : BaseAnimation, IAnimation
    where T : struct
{
    public static readonly CoreProperty<Animator<T>> AnimatorProperty;
    public static readonly CoreProperty<T> PreviousProperty;
    public static readonly CoreProperty<T> NextProperty;
    private Animator<T> _animator;
    private T _previous;
    private T _next;

    public Animation()
    {
        _animator = (Animator<T>)Activator.CreateInstance(AnimatorRegistry.GetAnimatorType(typeof(T)))!;
    }

    static Animation()
    {
        AnimatorProperty = ConfigureProperty<Animator<T>, Animation<T>>(nameof(Animation))
            .Accessor(o => o.Animator, (o, v) => o.Animator = v)
            .Register();

        PreviousProperty = ConfigureProperty<T, Animation<T>>(nameof(Previous))
            .Accessor(o => o.Previous, (o, v) => o.Previous = v)
            .Observability(PropertyObservability.Changed)
            .JsonName("prev")
            .Register();

        NextProperty = ConfigureProperty<T, Animation<T>>(nameof(Next))
            .Accessor(o => o.Next, (o, v) => o.Next = v)
            .Observability(PropertyObservability.Changed)
            .JsonName("next")
            .Register();
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

    Animator IAnimation.Animator => Animator;

    object IAnimation.Previous
    {
        get => Previous;
        set
        {
            if (value is T typedValue)
            {
                Previous = typedValue;
            }
        }
    }

    object IAnimation.Next
    {
        get => Next;
        set
        {
            if (value is T typedValue)
            {
                Next = typedValue;
            }
        }
    }
}
