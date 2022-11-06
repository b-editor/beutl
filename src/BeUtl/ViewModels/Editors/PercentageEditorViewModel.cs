using Beutl.Framework;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class PercentageEditorViewModel : BaseEditorViewModel<float>
{
    public PercentageEditorViewModel(IAbstractProperty<float> property)
        : base(property)
    {
        Text = property.GetObservable()
            .Select(x => Format(x))
            .ToReadOnlyReactivePropertySlim(Format(property.GetValue()))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string> Text { get; }

    private static string Format(float value)
    {
        return $"{value * 100:f}%";
    }
}
