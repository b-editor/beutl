using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Framework;
using Beutl.Reactive;
using Beutl.Operation;
using Beutl.Styling;

using Reactive.Bindings.Extensions;

namespace Beutl.Services.Editors.Wrappers;

public interface IStylingSetterWrapper : IAbstractProperty
{
    ISetter Setter { get; }

    IStyle Style { get; }
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
            if (_setter.Animation is { } animation)
            {
                _disposable = _setter.Animation.Children
                    .ObserveProperty(x => x.Count)
                    .Select(x => x > 0)
                    .Subscribe(x => PublishNext(x));
            }
        }
    }

    public StylingSetterClientImpl(Setter<T> setter, Style style)
    {
        Property = setter.Property;
        Setter = setter;
        Style = style;
        HasAnimation = new HasAnimationObservable(setter);
    }

    public CoreProperty<T> Property { get; }

    public Setter<T> Setter { get; }

    public Style Style { get; }

    public Animation<T> Animation
    {
        get
        {
            Setter.Animation ??= new Animation<T>(Property);
            return Setter.Animation;
        }
    }

    public IObservable<bool> HasAnimation { get; }

    public Type ImplementedType => Style.TargetType;

    ISetter IStylingSetterWrapper.Setter => Setter;

    IStyle IStylingSetterWrapper.Style => Style;

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
        Setter.Value = value;
    }
}
