using BeUtl.Media;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace BeUtl.ViewModels.Editors;

public sealed class ColorEditorViewModel : BaseEditorViewModel<Color>
{
    public ColorEditorViewModel(IWrappedProperty<Color> property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AColor> Value { get; }
}
