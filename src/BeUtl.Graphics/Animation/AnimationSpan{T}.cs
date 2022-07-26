using System.ComponentModel;

using BeUtl.Animation.Easings;
using BeUtl.Media;
using BeUtl.Styling;

namespace BeUtl.Animation;

public sealed class AnimationSpan<T> : AnimationSpan, IAnimationSpan<T>, ILogicalElement, IStylingElement
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
            .Observability(PropertyObservability.Changed)
            .SerializeName("prev")
            .Register();

        NextProperty = ConfigureProperty<T, AnimationSpan<T>>(nameof(Next))
            .Accessor(o => o.Next, (o, v) => o.Next = v)
            .Observability(PropertyObservability.Changed)
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

    public event EventHandler? Invalidated;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren
    {
        get
        {
            if (_previous is ILogicalElement prevElm)
                yield return prevElm;
            if (_next is ILogicalElement nextElm)
                yield return nextElm;
        }
    }
    
    IEnumerable<IStylingElement> IStylingElement.StylingChildren
    {
        get
        {
            if (_previous is IStylingElement prevElm)
                yield return prevElm;
            if (_next is IStylingElement nextElm)
                yield return nextElm;
        }
    }

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

                // 論理ツリーの設定
                var logicalTreeAttachmentArgs = new LogicalTreeAttachmentEventArgs(this);
                (args1.OldValue as ILogicalElement)?.NotifyDetachedFromLogicalTree(logicalTreeAttachmentArgs);
                (args1.NewValue as ILogicalElement)?.NotifyAttachedToLogicalTree(logicalTreeAttachmentArgs);

                // スタイルツリーの設定
                var stylingTreeAttachmentArgs = new StylingTreeAttachmentEventArgs(this);
                (args1.OldValue as IStylingElement)?.NotifyDetachedFromStylingTree(stylingTreeAttachmentArgs);
                (args1.NewValue as IStylingElement)?.NotifyAttachedToStylingTree(stylingTreeAttachmentArgs);
            }

        }
    }

    private void AffectsRender_Invalidated(object? sender, EventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
