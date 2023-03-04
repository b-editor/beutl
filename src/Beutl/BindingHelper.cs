using System.Reactive.Subjects;

using Avalonia;
using Avalonia.Data;

using Reactive.Bindings;

namespace Beutl;

internal static class BindingHelper
{
    public static IBinding ToPropertyBinding<T>(this IReactiveProperty<T> property, BindingMode bindingMode = BindingMode.Default)
    {
        return new BindingAdaptor<T>(property, bindingMode);
    }

    private class BindingAdaptor<T> : IBinding
    {
        private readonly IReactiveProperty<T> _property;
        private readonly RxPropertySubject<T> _source;
        private readonly BindingMode _bindingMode;

        public BindingAdaptor(IReactiveProperty<T> property, BindingMode bindingMode)
        {
            _property = property;
            _source = new RxPropertySubject<T>(property);
            _bindingMode = bindingMode;
        }

        public InstancedBinding? Initiate(
            IAvaloniaObject target,
            AvaloniaProperty? targetProperty,
            object? anchor = null,
            bool enableDataValidation = false)
        {
            BindingMode bindingMode = _bindingMode == BindingMode.Default
                ? targetProperty?.GetMetadata(target.GetType())?.DefaultBindingMode ?? BindingMode.Default
                : _bindingMode;

            return bindingMode switch
            {
                BindingMode.TwoWay => InstancedBinding.TwoWay(_source),
                BindingMode.OneTime => InstancedBinding.OneTime(_property.Value!),
                BindingMode.OneWayToSource => InstancedBinding.OneWayToSource(_source),
                _ => InstancedBinding.OneWay(_source),
            };
        }
    }

    private sealed class RxPropertySubject<T> : ISubject<object?>
    {
        private readonly IReactiveProperty<T> _source;

        public RxPropertySubject(IReactiveProperty<T> source)
        {
            _source = source;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(object? value)
        {
            if (value is T t)
            {
                _source.Value = t;
            }
            else
            {
                _source.Value = default!;
            }
        }

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            return _source.Subscribe(
                v => observer.OnNext(v),
                observer.OnError,
                observer.OnCompleted);
        }
    }
}
