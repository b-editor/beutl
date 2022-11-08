using Beutl.Framework;
using Beutl.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace Beutl.ViewModels.Editors;

public sealed class ColorEditorViewModel : BaseEditorViewModel<Color>
{
    public ColorEditorViewModel(IAbstractProperty<Color> property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AColor> Value { get; }
}
