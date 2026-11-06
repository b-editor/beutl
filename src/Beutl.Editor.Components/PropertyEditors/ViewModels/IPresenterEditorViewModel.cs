using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// An editor whose value can be a presenter of another object, which the user picks from the targets.
public interface IPresenterEditorViewModel : ITargetPickerViewModel
{
    ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }
}
