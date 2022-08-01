using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Reactive;

using Reactive.Bindings.Extensions;

namespace BeUtl.Services.Editors.Wrappers;

public sealed class AnimatableCorePropertyWrapper<T> : CorePropertyWrapper<T>, IWrappedProperty<T>.IAnimatable
{
    private sealed class HasAnimationObservable : LightweightObservableBase<bool>
    {
        private readonly CoreProperty<T> _property;
        private readonly Animatable _obj;
        private IDisposable? _disposable0;
        private IDisposable? _disposable1;

        public HasAnimationObservable(CoreProperty<T> property, Animatable obj)
        {
            _property = property;
            _obj = obj;
        }

        protected override void Subscribed(IObserver<bool> observer, bool first)
        {
            base.Subscribed(observer, first);
            foreach (IAnimation item in _obj.Animations.AsSpan())
            {
                if (item.Property.Id == _property.Id
                    && item is Animation<T> { Children.Count: > 0 })
                {
                    observer.OnNext(true);
                    return;
                }
            }

            observer.OnNext(false);
        }

        protected override void Deinitialize()
        {
            _disposable0?.Dispose();
            _disposable1?.Dispose();
            (_disposable0, _disposable1) = (null, null);
        }

        protected override void Initialize()
        {
            _disposable0?.Dispose();
            _disposable0 = _obj.Animations.ForEachItem(
                item =>
                {
                    if (item.Property.Id == _property.Id)
                    {
                        _disposable1?.Dispose();
                        _disposable1 = item.Children.ObserveProperty(x => x.Count)
                            .Select(x => x > 0)
                            .Subscribe(x => PublishNext(x));
                    }
                },
                item =>
                {
                    if (item.Property.Id == _property.Id)
                    {
                        _disposable1?.Dispose();
                        _disposable1 = null;
                    }
                },
                () =>
                {
                    _disposable1?.Dispose();
                    _disposable1 = null;
                });
        }
    }

    public AnimatableCorePropertyWrapper(CoreProperty<T> property, Animatable obj)
        : base(property, obj)
    {
        HasAnimation = new HasAnimationObservable(property, obj);
    }

    public Animation<T> Animation => GetAnimation();

    public IObservable<bool> HasAnimation { get; }

    public IAnimationSpan CreateSpan(Easing easing)
    {
        CoreProperty<T> property = AssociatedProperty;
        Type ownerType = property.OwnerType;
        ILogicalElement? owner = Animation.FindLogicalParent(ownerType);
        T? defaultValue = default;
        bool hasDefaultValue = true;
        if (owner is ICoreObject ownerCO)
        {
            defaultValue = ownerCO.GetValue(property);
        }
        else if (owner != null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(owner.GetType());
            defaultValue = metadata.DefaultValue;
            hasDefaultValue = metadata.HasDefaultValue;
        }
        else
        {
            hasDefaultValue = false;
        }

        var span = new AnimationSpan<T>
        {
            Easing = easing,
            Duration = TimeSpan.FromSeconds(2)
        };

        if (hasDefaultValue && defaultValue != null)
        {
            span.Previous = defaultValue;
            span.Next = defaultValue;
        }

        return span;
    }

    private Animation<T> GetAnimation()
    {
        var animatable = (Animatable)Tag;

        foreach (IAnimation item in animatable.Animations.AsSpan())
        {
            if (item.Property.Id == AssociatedProperty.Id
                && item is Animation<T> animation1)
            {
                return animation1;
            }
        }

        var animation2 = new Animation<T>(AssociatedProperty);
        animatable.Animations.Add(animation2);
        return animation2;
    }
}
