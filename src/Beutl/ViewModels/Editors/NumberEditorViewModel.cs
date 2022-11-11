using System.Numerics;

using Beutl.Framework;
using Beutl.Services.Editors;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class NumberEditorViewModel<T> : BaseEditorViewModel<T>
    where T : struct, INumber<T>
{
    public NumberEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
        Text = property.GetObservable()
            .Select(Format)
            .ToReadOnlyReactivePropertySlim(Format(property.GetValue()))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string> Text { get; }

    private static string Format(T value)
    {
        return value.ToString() ?? string.Empty;
    }

    public T Decrement(T value, int increment)
    {
        unchecked
        {
            for (int i = 0; i < increment; i++)
            {
                value--;
            }
        }
        return value;
    }

    public T Increment(T value, int increment)
    {
        unchecked
        {
            for (int i = 0; i < increment; i++)
            {
                value++;
            }
        }
        return value;
    }

    public bool TryParse(string? s, out T result)
    {
        return T.TryParse(s, CultureInfo.CurrentUICulture, out result);
    }
}
