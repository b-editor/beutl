using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Reactive;
using BeUtl.Streaming;
using BeUtl.Styling;

using Reactive.Bindings.Extensions;

namespace BeUtl.Services.Editors.Wrappers;

public interface IStylingSetterWrapper : IWrappedProperty
{
}

public sealed class StylingSetterWrapper<T> : IWrappedProperty<T>.IAnimatable, IStylingSetterWrapper
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

    public StylingSetterWrapper(Setter<T> setter)
    {
        AssociatedProperty = setter.Property;
        Tag = setter;

        Header = Observable.Return(setter.Property.Name);

        HasAnimation = new HasAnimationObservable(setter);
    }

    public CoreProperty<T> AssociatedProperty { get; }

    public object Tag { get; }

    public IObservable<string> Header { get; }

    public Animation<T> Animation
    {
        get
        {
            var setter = (Setter<T>)Tag;
            setter.Animation ??= new Animation<T>(AssociatedProperty);
            return setter.Animation;
        }
    }

    public IObservable<bool> HasAnimation { get; }

    public IObservable<T?> GetObservable()
    {
        return (Setter<T>)Tag;
    }

    public T? GetValue()
    {
        return ((Setter<T>)Tag).Value;
    }

    public void SetValue(T? value)
    {
        ((Setter<T>)Tag).Value = value;
    }

    IAnimationSpan IWrappedProperty.IAnimatable.CreateSpan(Easing easing)
    {
        CoreProperty<T> property = AssociatedProperty;
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
