using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Framework;
using BeUtl.Reactive;
using BeUtl.Streaming;
using BeUtl.Styling;

using Reactive.Bindings.Extensions;

namespace BeUtl.Services.Editors.Wrappers;

public interface IStylingSetterWrapper : IAbstractProperty
{
    ISetter Setter { get; }
}

public sealed class StylingSetterClientImpl<T> : IAbstractAnimatableProperty<T>, IStylingSetterWrapper
{
    private sealed class HasAnimationObservable : LightweightObservableBase<bool>
    {
        private IDisposable? _disposable;
        private readonly Setter<T> _setter;

        public HasAnimationObservable(Setter<T> setter)
        {
            _setter = setter;
        }

        protected override void Subscribed(IObserver<bool> observer, bool first)
        {
            base.Subscribed(observer, first);
            observer.OnNext(_setter.Animation is { Children.Count: > 0 });
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;

            _setter.Invalidated -= Setter_Invalidated;
        }

        protected override void Initialize()
        {
            _disposable?.Dispose();

            _setter.Invalidated += Setter_Invalidated;
        }

        private void Setter_Invalidated(object? sender, EventArgs e)
        {
            _disposable?.Dispose();
            if(_setter.Animation is { } animation)
            {
                _disposable = _setter.Animation.Children
                    .ObserveProperty(x => x.Count)
                    .Select(x => x > 0)
                    .Subscribe(x => PublishNext(x));
            }
        }
    }

    public StylingSetterClientImpl(Setter<T> setter)
    {
        Property = setter.Property;
        Setter = setter;

        HasAnimation = new HasAnimationObservable(setter);
    }

    public CoreProperty<T> Property { get; }

    public Setter<T> Setter { get; }

    public Animation<T> Animation
    {
        get
        {
            Setter.Animation ??= new Animation<T>(Property);
            return Setter.Animation;
        }
    }

    public IObservable<bool> HasAnimation { get; }

    public Type ImplementedType => Animation.FindStylingParent<IStyle>()?.TargetType ?? Property.OwnerType;

    ISetter IStylingSetterWrapper.Setter => Setter;

    public IObservable<T?> GetObservable()
    {
        return Setter;
    }

    public T? GetValue()
    {
        return Setter.Value;
    }

    public void SetValue(T? value)
    {
        CorePropertyMetadata<T>? metadata = Property.GetMetadata<CorePropertyMetadata<T>>(ImplementedType);
        if (metadata?.Validator != null)
        {
            value = metadata.Validator.Coerce(null, value);
        }

        Setter.Value = value;
    }

    IAnimationSpan IAbstractAnimatableProperty.CreateSpan(Easing easing)
    {
        CoreProperty<T> property = Property;
        IStyle? style = Animation.FindStylingParent<IStyle>();
        T? defaultValue = GetValue();
        bool hasDefaultValue = true;
        if (style != null && defaultValue == null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(style.TargetType);
            defaultValue = metadata.DefaultValue;
            hasDefaultValue = metadata.HasDefaultValue;
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
}
