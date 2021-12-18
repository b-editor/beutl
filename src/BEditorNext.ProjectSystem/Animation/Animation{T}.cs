namespace BEditorNext.Animation;

public class Animation<T> : BaseAnimation, IAnimation
    where T : struct
{
    public static readonly PropertyDefine<Animator<T>> AnimatorProperty;
    public static readonly PropertyDefine<T> PreviousProperty;
    public static readonly PropertyDefine<T> NextProperty;
    private Animator<T> _animator;
    private T _previous;
    private T _next;

    public Animation()
    {
        _animator = (Animator<T>)Activator.CreateInstance(AnimatorRegistry.GetAnimatorType(typeof(T)))!;
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
