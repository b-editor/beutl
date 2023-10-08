using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public interface IConfigureUniformEditor
{
    ReactivePropertySlim<bool> IsUniformEditorEnabled { get; }
}
