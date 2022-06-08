using System.Reactive.Linq;

using BeUtl.Media;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using AColor = Avalonia.Media.Color;

namespace BeUtl.ViewModels.Editors;

public sealed class ColorEditorViewModel : BaseEditorViewModel<Color>
{
    public ColorEditorViewModel(PropertyInstance<Color> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .Select(x => x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<AColor> Value { get; }
}
