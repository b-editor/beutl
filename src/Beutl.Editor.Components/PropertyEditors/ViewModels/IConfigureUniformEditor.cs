using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public interface IConfigureUniformEditor
{
    ReactivePropertySlim<bool> IsUniformEditorEnabled { get; }
}
