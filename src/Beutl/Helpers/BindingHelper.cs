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

    private class BindingAdaptor<T>(IReactiveProperty<T> property, BindingMode bindingMode) : IBinding
    {
        private readonly RxPropertySubject<T> _source = new(property);
        private readonly BindingMode _bindingMode = bindingMode;

        public InstancedBinding? Initiate(
            AvaloniaObject target,
            AvaloniaProperty? targetProperty,
            object? anchor = null,
            bool enableDataValidation = false)
        {
            BindingMode bindingMode = _bindingMode == BindingMode.Default
                ? targetProperty?.GetMetadata(target.GetType())?.DefaultBindingMode ?? BindingMode.Default
                : _bindingMode;

            return bindingMode switch
            {
                BindingMode.TwoWay => InstancedBinding.TwoWay(_source, _source),
                BindingMode.OneTime => InstancedBinding.OneTime(property.Value!),
                BindingMode.OneWayToSource => InstancedBinding.OneWayToSource(_source),
                _ => InstancedBinding.OneWay(_source),
            };
        }
    }

    private sealed class RxPropertySubject<T>(IReactiveProperty<T> source) : ISubject<object?>
    {
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
                source.Value = t;
            }
            else
            {
                source.Value = default!;
            }
        }

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            return source.Subscribe(
                v => observer.OnNext(v),
                observer.OnError,
                observer.OnCompleted);
        }
    }
}
