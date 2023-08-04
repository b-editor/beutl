using Beutl.Animation;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Reactive;

namespace Beutl.Operators.Configure;

public sealed class AnimatableCorePropertyImpl<T> : CorePropertyImpl<T>, IAbstractAnimatableProperty<T>
{
    private sealed class AnimationObservable : LightweightObservableBase<IAnimation<T>?>
    {
        private readonly CoreProperty<T> _property;
        private readonly Animatable _obj;
        private IDisposable? _disposable0;

        public AnimationObservable(CoreProperty<T> property, Animatable obj)
        {
            _property = property;
            _obj = obj;
        }

        protected override void Subscribed(IObserver<IAnimation<T>?> observer, bool first)
        {
            base.Subscribed(observer, first);
            foreach (IAnimation item in _obj.Animations.GetMarshal().Value)
            {
                if (item.Property.Id == _property.Id
                    && item is IAnimation<T> animation)
                {
                    observer.OnNext(animation);
                    return;
                }
            }

            observer.OnNext(null);
        }

        protected override void Deinitialize()
        {
            _disposable0?.Dispose();
            _disposable0 = null;
        }

        protected override void Initialize()
        {
            _disposable0?.Dispose();
            _disposable0 = _obj.Animations.ForEachItem(
                item =>
                {
                    if (item.Property.Id == _property.Id
                        && item is IAnimation<T> animation)
                    {
                        PublishNext(animation);
                    }
                },
                item =>
                {
                    if (item.Property.Id == _property.Id)
                    {
                        PublishNext(null);
                    }
                },
                () =>
                {
                });
        }
    }

    public AnimatableCorePropertyImpl(CoreProperty<T> property, Animatable obj)
        : base(property, obj)
    {
        ObserveAnimation = new AnimationObservable(property, obj);
    }

    public IAnimation<T>? Animation
    {
        get => GetAnimation();
        set => SetAnimation(value);
    }

    public IObservable<IAnimation<T>?> ObserveAnimation { get; }

    private IAnimation<T>? GetAnimation()
    {
        var animatable = (Animatable)Object;

        foreach (IAnimation item in animatable.Animations.GetMarshal().Value)
        {
            if (item.Property.Id == Property.Id
                && item is IAnimation<T> animation1)
            {
                return animation1;
            }
        }

        return null;
    }

    private void SetAnimation(IAnimation<T>? animation)
    {
        var animatable = (Animatable)Object;

        for (int i = animatable.Animations.Count - 1; i >= 0; i--)
        {
            IAnimation item = animatable.Animations[i];
            if (item.Property.Id == Property.Id)
            {
                animatable.Animations.RemoveAt(i);
            }
        }

        if (animation != null)
        {
            animatable.Animations.Add(animation);
        }
    }
}
