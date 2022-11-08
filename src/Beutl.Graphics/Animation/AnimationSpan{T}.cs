using System.ComponentModel;

using Beutl.Animation.Easings;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Animation;

public sealed class AnimationSpan<T> : AnimationSpan, IAnimationSpan<T>
{
    public static readonly CoreProperty<T> PreviousProperty;
    public static readonly CoreProperty<T> NextProperty;
    private static readonly Animator<T> s_animator;
    private T _previous;
    private T _next;

    public AnimationSpan()
    {
        _previous = s_animator.DefaultValue();
        _next = s_animator.DefaultValue();
    }

    static AnimationSpan()
    {
        s_animator = (Animator<T>)Activator.CreateInstance(AnimatorRegistry.GetAnimatorType(typeof(T)))!;

        PreviousProperty = ConfigureProperty<T, AnimationSpan<T>>(nameof(Previous))
            .Accessor(o => o.Previous, (o, v) => o.Previous = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("prev")
            .Register();

        NextProperty = ConfigureProperty<T, AnimationSpan<T>>(nameof(Next))
            .Accessor(o => o.Next, (o, v) => o.Next = v)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("next")
            .Register();
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

    public Animator<T> Animator => s_animator;

    public event EventHandler? Invalidated;

    public T Interpolate(float progress)
    {
        float ease = Easing.Ease(progress);
        return s_animator.Interpolate(ease, _previous, _next);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(Previous) or nameof(Next) or nameof(Easing) or nameof(Duration))
        {
            Invalidated?.Invoke(this, EventArgs.Empty);

            if (args.PropertyName is nameof(Previous) or nameof(Next)
                && args is CorePropertyChangedEventArgs<T> args1)
            {
                // IAffectsRenderのイベント登録
                if (args1.OldValue is IAffectsRender affectsRender1)
                {
                    affectsRender1.Invalidated -= AffectsRender_Invalidated;
                }

                if (args1.NewValue is IAffectsRender affectsRender2)
                {
                    affectsRender2.Invalidated += AffectsRender_Invalidated;
                }
            }

        }
    }

    private void AffectsRender_Invalidated(object? sender, EventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
