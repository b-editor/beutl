using System.Reactive.Linq;

using BeUtl.Media;
using BeUtl.ProjectSystem;

using FluentAvalonia.UI.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class ColorEditorViewModel : BaseEditorViewModel<Color>
{
    public ColorEditorViewModel(Setter<Color> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .Select(x => (Color2)x.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Color2> Value { get; }
}
