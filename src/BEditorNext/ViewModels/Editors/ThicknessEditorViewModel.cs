
using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class ThicknessEditorViewModel : BaseEditorViewModel<Thickness>
{
    public ThicknessEditorViewModel(Setter<Thickness> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<Thickness> Value { get; }
}
