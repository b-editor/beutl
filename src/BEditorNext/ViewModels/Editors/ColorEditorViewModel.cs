using System.Reactive.Linq;

using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using FluentAvalonia.UI.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

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

    //public ReadOnlyReactivePropertySlim<Color> Value { get; }

    public ReadOnlyReactivePropertySlim<Color2> Value { get; }
}
